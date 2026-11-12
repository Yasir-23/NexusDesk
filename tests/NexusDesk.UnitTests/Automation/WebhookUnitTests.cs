using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using NexusDesk.Modules.Automation.Application;
using NexusDesk.Modules.Automation.Domain;
using Xunit;

namespace NexusDesk.UnitTests.Automation;

public class WebhookUnitTests
{
    [Fact]
    public void WebhookSigner_ShouldGenerateExpectedHmacSha256Signature_AndVerifySuccessfully()
    {
        const string secret = "super-secret-webhook-key-min-16-chars";
        const string timestamp = "1731177000";
        const string rawPayload = """{"deliveryId":"f47ac10b-58cc-4372-a567-0e02b2c3d479","eventType":"ticket.sla_breached"}""";

        var signature = WebhookSigner.ComputeSignature(secret, timestamp, rawPayload);

        signature.Should().StartWith("sha256=");
        signature.Length.Should().Be("sha256=".Length + 64);

        // Manually compute expected signature to independently verify algorithm
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{rawPayload}"));
        var expectedHex = "sha256=" + Convert.ToHexString(expectedBytes).ToLowerInvariant();
        signature.Should().Be(expectedHex);

        // Positive verification
        WebhookSigner.VerifySignature(secret, timestamp, rawPayload, signature).Should().BeTrue();

        // Negative verification: altered body
        WebhookSigner.VerifySignature(secret, timestamp, rawPayload + " ", signature).Should().BeFalse();

        // Negative verification: altered timestamp
        WebhookSigner.VerifySignature(secret, "1731177001", rawPayload, signature).Should().BeFalse();

        // Negative verification: altered secret
        WebhookSigner.VerifySignature("different-secret-key-16-chars", timestamp, rawPayload, signature).Should().BeFalse();

        // Negative verification: malformed header
        WebhookSigner.VerifySignature(secret, timestamp, rawPayload, "md5=invalid").Should().BeFalse();
        WebhookSigner.VerifySignature(secret, timestamp, rawPayload, string.Empty).Should().BeFalse();
    }

    [Fact]
    public void DataProtection_ShouldEncryptAndDecryptWebhookSecret_WithSpecificPurpose()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = provider.CreateProtector(WebhookSigner.DataProtectionPurpose);

        const string plaintextSecret = "my-webhook-secret-token-32-chars";

        var encrypted = protector.Protect(plaintextSecret);
        encrypted.Should().NotBeNullOrWhiteSpace();
        encrypted.Should().NotBe(plaintextSecret);

        var decrypted = protector.Unprotect(encrypted);
        decrypted.Should().Be(plaintextSecret);

        // Verify that a different purpose fails to unprotect
        var otherProtector = provider.CreateProtector("Other.Purpose");
        var act = () => otherProtector.Unprotect(encrypted);
        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 10)]
    [InlineData(3, 30)]
    [InlineData(4, 30)]
    public void WebhookBackoffPolicy_ShouldReturnExactDurations_InFastTestMode(int attempt, int expectedSeconds)
    {
        var duration = WebhookBackoffPolicy.GetBackoff(attempt, useFastTestBackoff: true);
        duration.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void WebhookBackoffPolicy_ShouldReturnExactDurations_InProductionMode()
    {
        WebhookBackoffPolicy.GetBackoff(1, useFastTestBackoff: false).Should().Be(TimeSpan.FromSeconds(30));
        WebhookBackoffPolicy.GetBackoff(2, useFastTestBackoff: false).Should().Be(TimeSpan.FromMinutes(5));
        WebhookBackoffPolicy.GetBackoff(3, useFastTestBackoff: false).Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void OutboxMessage_RetryTransitions_ShouldTransitionToDeadLettered_After3Failures()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var message = OutboxMessage.CreatePending(
            messageId,
            tenantId,
            "ticket.created",
            "{}",
            now);

        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.RetryCount.Should().Be(0);

        // Attempt 1 fails
        var next1 = now.Add(WebhookBackoffPolicy.GetBackoff(1, useFastTestBackoff: true));
        message.RecordRetry("HTTP 503", next1);
        message.RetryCount.Should().Be(1);
        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.NextRetryAtUtc.Should().Be(next1);
        message.LastError.Should().Be("HTTP 503");

        // Attempt 2 fails
        var next2 = now.Add(WebhookBackoffPolicy.GetBackoff(2, useFastTestBackoff: true));
        message.RecordRetry("HTTP 503", next2);
        message.RetryCount.Should().Be(2);
        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.NextRetryAtUtc.Should().Be(next2);

        // Attempt 3 fails -> DeadLettered
        message.MarkDeadLettered("HTTP 503", now.AddSeconds(12));
        message.RetryCount.Should().Be(3);
        message.Status.Should().Be(OutboxMessageStatus.DeadLettered);
        message.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void OutboxMessage_MarkProcessed_ShouldUpdateStatusAndProcessedTime()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var message = OutboxMessage.CreatePending(Guid.NewGuid(), tenantId, "ticket.created", "{}", now);

        var processedAt = now.AddSeconds(5);
        message.MarkProcessed(processedAt);

        message.Status.Should().Be(OutboxMessageStatus.Processed);
        message.ProcessedAtUtc.Should().Be(processedAt);
        message.LastError.Should().BeNull();
    }

    [Fact]
    public void WebhookSubscription_Create_ShouldRejectNonHttpsAndNonLocalhostUrls()
    {
        var tenantId = Guid.NewGuid();

        // Insecure external URL
        var res1 = WebhookSubscription.Create(
            tenantId,
            "http://external-receiver.com/hook",
            "enc_secret",
            ["ticket.created"]);
        res1.IsFailure.Should().BeTrue();
        res1.Error.Code.Should().Be("Webhook.InvalidTargetUrlScheme");

        // Valid HTTPS URL
        var res2 = WebhookSubscription.Create(
            tenantId,
            "https://external-receiver.com/hook",
            "enc_secret",
            ["ticket.created"]);
        res2.IsSuccess.Should().BeTrue();

        // Valid localhost HTTP URL
        var res3 = WebhookSubscription.Create(
            tenantId,
            "http://localhost:8080/hook",
            "enc_secret",
            ["ticket.created"]);
        res3.IsSuccess.Should().BeTrue();

        // Valid 127.0.0.1 HTTP URL
        var res4 = WebhookSubscription.Create(
            tenantId,
            "http://127.0.0.1:9090/hook",
            "enc_secret",
            ["ticket.created"]);
        res4.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void WebhookSubscription_MatchesEvent_ShouldMatchSpecificAndWildcardSubscriptions()
    {
        var tenantId = Guid.NewGuid();

        var subSpecific = WebhookSubscription.Create(
            tenantId,
            "https://api.example.com/hook",
            "enc_secret",
            ["ticket.created", "ticket.resolved"]).Value;

        subSpecific.MatchesEvent("ticket.created").Should().BeTrue();
        subSpecific.MatchesEvent("ticket.resolved").Should().BeTrue();
        subSpecific.MatchesEvent("ticket.sla_breached").Should().BeFalse();

        var subWildcard = WebhookSubscription.Create(
            tenantId,
            "https://api.example.com/hook",
            "enc_secret",
            ["*"]).Value;

        subWildcard.MatchesEvent("ticket.created").Should().BeTrue();
        subWildcard.MatchesEvent("ticket.sla_breached").Should().BeTrue();
        subWildcard.MatchesEvent("ticket.resolved").Should().BeTrue();

        // Deactivated subscription must not match
        subWildcard.Toggle();
        subWildcard.IsActive.Should().BeFalse();
        subWildcard.MatchesEvent("ticket.created").Should().BeFalse();
    }
}
