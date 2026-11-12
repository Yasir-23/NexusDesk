using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NexusDesk.SharedKernel.Primitives;

namespace NexusDesk.Modules.Ticketing.RealTime;

public sealed class InProcessDomainEventDispatcher : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> Dispatchers = new();
    private readonly IServiceProvider _serviceProvider;

    public InProcessDomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            if (domainEvent == null)
            {
                continue;
            }

            var eventType = domainEvent.GetType();
            var dispatch = Dispatchers.GetOrAdd(eventType, CreateDispatcher);
            await dispatch(_serviceProvider, domainEvent, cancellationToken);
        }
    }

    private static Func<IServiceProvider, IDomainEvent, CancellationToken, Task> CreateDispatcher(Type eventType)
    {
        var method = typeof(InProcessDomainEventDispatcher)
            .GetMethod(nameof(DispatchTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(eventType);

        return (sp, evt, ct) => (Task)method.Invoke(null, [sp, evt, ct])!;
    }

    private static async Task DispatchTypedAsync<TEvent>(
        IServiceProvider serviceProvider,
        IDomainEvent domainEvent,
        CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        var handlers = serviceProvider.GetServices<IDomainEventHandler<TEvent>>();
        var typedEvent = (TEvent)domainEvent;

        foreach (var handler in handlers)
        {
            await handler.HandleAsync(typedEvent, cancellationToken);
        }
    }
}
