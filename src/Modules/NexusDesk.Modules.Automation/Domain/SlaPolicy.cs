using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Automation.Domain;

public sealed class SlaPolicy : Entity<Guid>, ITenantScoped
{
    public static readonly IReadOnlyDictionary<string, (int FirstResponseMinutes, int ResolutionMinutes)> SystemDefaults =
        new Dictionary<string, (int FirstResponseMinutes, int ResolutionMinutes)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Low"] = (1440, 4320),
            ["Medium"] = (480, 1440),
            ["High"] = (120, 480),
            ["Urgent"] = (60, 240)
        };

    public Guid TenantId { get; private set; }
    public string Priority { get; private set; }
    public int FirstResponseTargetMinutes { get; private set; }
    public int ResolutionTargetMinutes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    private SlaPolicy(
        Guid id,
        Guid tenantId,
        string priority,
        int firstResponseTargetMinutes,
        int resolutionTargetMinutes,
        DateTimeOffset createdAtUtc) : base(id)
    {
        TenantId = tenantId;
        Priority = priority;
        FirstResponseTargetMinutes = firstResponseTargetMinutes;
        ResolutionTargetMinutes = resolutionTargetMinutes;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    private SlaPolicy() : base()
    {
        Priority = string.Empty;
    }

    public static bool TryNormalizePriority(string? priority, out string normalized)
    {
        if (!string.IsNullOrWhiteSpace(priority))
        {
            var trimmed = priority.Trim();
            foreach (var key in SystemDefaults.Keys)
            {
                if (string.Equals(trimmed, key, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = key;
                    return true;
                }
            }
        }

        normalized = string.Empty;
        return false;
    }

    public static Result<SlaPolicy> Create(
        Guid tenantId,
        string priority,
        int firstResponseTargetMinutes,
        int resolutionTargetMinutes,
        DateTimeOffset? utcNow = null)
    {
        if (tenantId == Guid.Empty)
        {
            return Result.Failure<SlaPolicy>(Error.Validation("SlaPolicy.InvalidTenant", "Tenant ID is required."));
        }

        if (!TryNormalizePriority(priority, out var normalizedPriority))
        {
            return Result.Failure<SlaPolicy>(Error.Validation(
                "SlaPolicy.InvalidPriority",
                "Priority must be one of: Low, Medium, High, Urgent."));
        }

        if (firstResponseTargetMinutes <= 0)
        {
            return Result.Failure<SlaPolicy>(Error.Validation(
                "SlaPolicy.InvalidFirstResponseTarget",
                "First response target minutes must be greater than 0."));
        }

        if (resolutionTargetMinutes <= 0)
        {
            return Result.Failure<SlaPolicy>(Error.Validation(
                "SlaPolicy.InvalidResolutionTarget",
                "Resolution target minutes must be greater than 0."));
        }

        var now = utcNow ?? DateTimeOffset.UtcNow;
        return Result.Success(new SlaPolicy(
            Guid.NewGuid(),
            tenantId,
            normalizedPriority,
            firstResponseTargetMinutes,
            resolutionTargetMinutes,
            now));
    }

    public Result UpdateTargets(int firstResponseTargetMinutes, int resolutionTargetMinutes, DateTimeOffset? utcNow = null)
    {
        if (firstResponseTargetMinutes <= 0)
        {
            return Result.Failure(Error.Validation(
                "SlaPolicy.InvalidFirstResponseTarget",
                "First response target minutes must be greater than 0."));
        }

        if (resolutionTargetMinutes <= 0)
        {
            return Result.Failure(Error.Validation(
                "SlaPolicy.InvalidResolutionTarget",
                "Resolution target minutes must be greater than 0."));
        }

        FirstResponseTargetMinutes = firstResponseTargetMinutes;
        ResolutionTargetMinutes = resolutionTargetMinutes;
        UpdatedAtUtc = utcNow ?? DateTimeOffset.UtcNow;

        return Result.Success();
    }
}
