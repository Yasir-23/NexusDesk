namespace NexusDesk.SharedKernel.Time;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
    DateTime UtcDateTime { get; }
}

public class SystemClock : ISystemClock
{
    private readonly TimeProvider _timeProvider;

    public SystemClock() : this(TimeProvider.System)
    {
    }

    public SystemClock(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();
    public DateTime UtcDateTime => _timeProvider.GetUtcNow().UtcDateTime;
}
