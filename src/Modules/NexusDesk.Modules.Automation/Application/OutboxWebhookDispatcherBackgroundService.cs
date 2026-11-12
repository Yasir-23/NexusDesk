using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusDesk.Modules.Automation.Domain;
using NexusDesk.Modules.Automation.Persistence;
using NexusDesk.Modules.Tenants.Persistence;
using NexusDesk.SharedKernel.MultiTenancy;

namespace NexusDesk.Modules.Automation.Application;

public sealed class OutboxWebhookDispatcherBackgroundService : BackgroundService
{
    public const string HttpClientName = "WebhookDispatcher";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtector _protector;
    private readonly ILogger<OutboxWebhookDispatcherBackgroundService> _logger;
    private readonly TimeSpan _pollingInterval;
    private readonly bool _useFastTestBackoff;

    public OutboxWebhookDispatcherBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILogger<OutboxWebhookDispatcherBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _protector = dataProtectionProvider.CreateProtector(WebhookSigner.DataProtectionPurpose);
        _logger = logger;

        var pollingSeconds = configuration.GetValue<int?>("Outbox:PollingIntervalSeconds") ?? 5;
        if (pollingSeconds < 1)
        {
            pollingSeconds = 5;
        }
        _pollingInterval = TimeSpan.FromSeconds(pollingSeconds);

        var configuredFastBackoff = configuration.GetValue<bool?>("Outbox:UseFastTestBackoff");
        _useFastTestBackoff = configuredFastBackoff ?? !hostEnvironment.IsProduction();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_pollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingOutboxMessagesOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error during outbox webhook dispatch cycle.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    [AllowCrossTenantQuery("Dispatcher polls pending outbox messages across all active tenants")]
    public async Task<int> ProcessPendingOutboxMessagesOnceAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantDescriptor> activeTenants;
        using (var catalogScope = _scopeFactory.CreateScope())
        {
            var catalogDb = catalogScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            activeTenants = await catalogDb.Tenants
                .AsNoTracking()
                .Where(t => t.Status == TenantStatus.Active)
                .Select(t => new TenantDescriptor(
                    t.Id,
                    t.Subdomain,
                    t.IsolationStrategy,
                    t.CustomConnectionString))
                .ToListAsync(cancellationToken);
        }

        var totalProcessed = 0;
        foreach (var tenant in activeTenants)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                totalProcessed += await ProcessTenantOutboxMessagesOnceAsync(tenant, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed processing outbox messages for tenant {TenantId} ({Subdomain}).",
                    tenant.TenantId,
                    tenant.Subdomain);
            }
        }

        return totalProcessed;
    }

    public async Task<int> ProcessTenantOutboxMessagesOnceAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        TenantDescriptor? descriptor;
        using (var catalogScope = _scopeFactory.CreateScope())
        {
            var catalogDb = catalogScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            descriptor = await catalogDb.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new TenantDescriptor(
                    t.Id,
                    t.Subdomain,
                    t.IsolationStrategy,
                    t.CustomConnectionString))
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (descriptor == null)
        {
            return 0;
        }

        return await ProcessTenantOutboxMessagesOnceAsync(descriptor, cancellationToken);
    }

    private async Task<int> ProcessTenantOutboxMessagesOnceAsync(
        TenantDescriptor tenant,
        CancellationToken cancellationToken)
    {
        using var tenantScope = _scopeFactory.CreateScope();
        var tenantAccessor = tenantScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        tenantAccessor.TenantContext = new TenantContext
        {
            TenantId = tenant.TenantId,
            Subdomain = tenant.Subdomain,
            IsolationStrategy = tenant.IsolationStrategy,
            CustomConnectionString = tenant.CustomConnectionString,
            Status = TenantStatus.Active
        };

        var automationDb = tenantScope.ServiceProvider.GetRequiredService<AutomationDbContext>();
        var now = DateTimeOffset.UtcNow;

        var pendingMessages = await automationDb.OutboxMessages
            .Where(m => m.TenantId == tenant.TenantId &&
                        m.Status == OutboxMessageStatus.Pending &&
                        m.NextRetryAtUtc <= now)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
        {
            return 0;
        }

        var activeSubscriptions = await automationDb.WebhookSubscriptions
            .Where(w => w.TenantId == tenant.TenantId && w.IsActive)
            .ToListAsync(cancellationToken);

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var processedCount = 0;

        foreach (var message in pendingMessages)
        {
            var matchingSubscriptions = activeSubscriptions
                .Where(s => s.MatchesEvent(message.EventType))
                .ToList();

            if (matchingSubscriptions.Count == 0)
            {
                message.MarkProcessed(now);
                processedCount++;
                continue;
            }

            var allSucceeded = true;
            string? firstFailureError = null;
            int? lastStatusCode = null;
            WebhookSubscription? failedSubscription = null;

            foreach (var subscription in matchingSubscriptions)
            {
                var (success, statusCode, error) = await AttemptDeliveryAsync(
                    httpClient,
                    subscription,
                    message,
                    cancellationToken);

                if (!success)
                {
                    allSucceeded = false;
                    firstFailureError ??= error;
                    lastStatusCode = statusCode;
                    failedSubscription ??= subscription;
                    break;
                }
            }

            if (allSucceeded)
            {
                message.MarkProcessed(DateTimeOffset.UtcNow);
                processedCount++;
            }
            else
            {
                var failureReason = firstFailureError ?? "Delivery failed";
                if (message.RetryCount + 1 >= WebhookBackoffPolicy.MaxDeliveryAttempts)
                {
                    message.MarkDeadLettered(failureReason, DateTimeOffset.UtcNow);

                    var deadLetter = DeadLetterWebhook.Create(
                        message.TenantId,
                        message.Id,
                        failedSubscription?.Id ?? Guid.Empty,
                        failedSubscription?.TargetUrl ?? string.Empty,
                        message.EventType,
                        message.PayloadJson,
                        attemptCount: message.RetryCount,
                        lastHttpStatusCode: lastStatusCode,
                        lastError: failureReason,
                        utcNow: DateTimeOffset.UtcNow);

                    automationDb.DeadLetterWebhooks.Add(deadLetter);
                }
                else
                {
                    var nextRetry = DateTimeOffset.UtcNow.Add(
                        WebhookBackoffPolicy.GetBackoff(message.RetryCount + 1, _useFastTestBackoff));
                    message.RecordRetry(failureReason, nextRetry);
                }
            }
        }

        await automationDb.SaveChangesAsync(cancellationToken);
        return processedCount;
    }

    private async Task<(bool Success, int? StatusCode, string? Error)> AttemptDeliveryAsync(
        HttpClient httpClient,
        WebhookSubscription subscription,
        OutboxMessage message,
        CancellationToken cancellationToken)
    {
        string secret;
        try
        {
            secret = _protector.Unprotect(subscription.EncryptedSecret);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed decrypting secret for webhook subscription {SubscriptionId}.", subscription.Id);
            return (false, null, "Failed decrypting secret: " + ex.Message);
        }

        var deliveryId = Guid.NewGuid().ToString();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = WebhookSigner.ComputeSignature(secret, timestamp, message.PayloadJson);

        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.TargetUrl);
        request.Content = new StringContent(message.PayloadJson, Encoding.UTF8, "application/json");

        request.Headers.Add(WebhookSigner.HeaderEvent, message.EventType);
        request.Headers.Add(WebhookSigner.HeaderDelivery, deliveryId);
        request.Headers.Add(WebhookSigner.HeaderTimestamp, timestamp);
        request.Headers.Add(WebhookSigner.HeaderSignature256, signature);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await httpClient.SendAsync(request, cts.Token);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return (true, statusCode, null);
            }

            var error = $"HTTP {statusCode} {response.ReasonPhrase}";
            return (false, statusCode, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, null, "Delivery timed out after 5 seconds.");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private sealed record TenantDescriptor(
        Guid TenantId,
        string Subdomain,
        IsolationStrategy IsolationStrategy,
        string? CustomConnectionString);
}
