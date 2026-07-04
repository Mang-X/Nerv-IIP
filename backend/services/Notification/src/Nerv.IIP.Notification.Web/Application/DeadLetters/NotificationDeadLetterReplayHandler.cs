using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Application.DeadLetters;

public sealed class NotificationDeadLetterReplayHandler(IServiceProvider serviceProvider) : IIntegrationEventDeadLetterReplayHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool CanReplay(IntegrationEventDeadLetterMessage message)
    {
        return ResolveEventType(message.EventClrType) is { } eventType
            && typeof(IIntegrationEventEnvelope).IsAssignableFrom(eventType)
            && ResolveHandlerType(eventType) is not null;
    }

    public async Task ReplayAsync(IntegrationEventDeadLetterMessage message, CancellationToken cancellationToken)
    {
        var eventType = ResolveEventType(message.EventClrType)
            ?? throw new InvalidOperationException($"Integration event CLR type '{message.EventClrType}' was not found.");
        if (!typeof(IIntegrationEventEnvelope).IsAssignableFrom(eventType))
        {
            throw new InvalidOperationException($"Integration event CLR type '{message.EventClrType}' is not an integration event envelope.");
        }

        var handlerType = ResolveHandlerType(eventType)
            ?? throw new InvalidOperationException($"No notification integration handler is registered for '{message.EventClrType}'.");
        var integrationEvent = JsonSerializer.Deserialize(message.EventJson, eventType, SerializerOptions)
            ?? throw new InvalidOperationException($"Dead-letter payload '{message.Id}' could not be deserialized.");

        using var scope = serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetService(handlerType)
            ?? ActivatorUtilities.CreateInstance(scope.ServiceProvider, handlerType);
        var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        var handleAsync = handlerInterface.GetMethod("HandleAsync")
            ?? throw new InvalidOperationException($"Handler '{handlerType.FullName}' does not expose HandleAsync.");
        var task = (Task?)handleAsync.Invoke(handler, [integrationEvent, cancellationToken])
            ?? throw new InvalidOperationException($"Handler '{handlerType.FullName}' returned no replay task.");
        await task;
    }

    private static Type? ResolveEventType(string eventClrType)
    {
        if (string.IsNullOrWhiteSpace(eventClrType))
        {
            return null;
        }

        return Type.GetType(eventClrType, throwOnError: false)
            ?? AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(eventClrType, throwOnError: false))
                .FirstOrDefault(type => type is not null);
    }

    private static Type? ResolveHandlerType(Type eventType)
    {
        var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        return typeof(Program).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .FirstOrDefault(type => handlerInterface.IsAssignableFrom(type));
    }
}
