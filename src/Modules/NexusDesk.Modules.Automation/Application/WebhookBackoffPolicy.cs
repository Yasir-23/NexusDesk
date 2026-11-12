namespace NexusDesk.Modules.Automation.Application;

public static class WebhookBackoffPolicy
{
    public const int MaxDeliveryAttempts = 3;

    /// <summary>
    /// Calculates the exponential backoff duration based on retry attempt (1-based)
    /// and whether fast test backoff is enabled.
    /// Fast: 1st -> 2s, 2nd -> 10s, 3rd -> 30s
    /// Prod: 1st -> 30s, 2nd -> 5m, 3rd -> 30m
    /// </summary>
    public static TimeSpan GetBackoff(int attempt, bool useFastTestBackoff)
    {
        if (useFastTestBackoff)
        {
            return attempt switch
            {
                1 => TimeSpan.FromSeconds(2),
                2 => TimeSpan.FromSeconds(10),
                3 => TimeSpan.FromSeconds(30),
                _ => TimeSpan.FromSeconds(30)
            };
        }

        return attempt switch
        {
            1 => TimeSpan.FromSeconds(30),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromMinutes(30)
        };
    }
}
