using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.IntegrationEvents;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Maintenance.Web;

public static class MaintenanceDeadLetterReplayServiceCollectionExtensions
{
    public static IServiceCollection AddMaintenanceDeadLetterReplay(this IServiceCollection services)
    {
        services.AddObservedIntegrationEventDeadLetterStore();
        Add<AlarmRaisedIntegrationEvent, OpenWorkOrderWhenAlarmRaisedHandler>(services, OpenWorkOrderWhenAlarmRaisedHandler.ConsumerName);
        Add<AlarmClearedIntegrationEvent, MarkWorkOrderAlarmClearedHandler>(services, MarkWorkOrderAlarmClearedHandler.ConsumerName);
        Add<DeviceAssetChangedIntegrationEvent, PauseMaintenancePlansWhenDeviceDisabledHandler>(services, PauseMaintenancePlansWhenDeviceDisabledHandler.ConsumerName);
        return services;
    }

    private static void Add<TEvent, TConsumer>(IServiceCollection services, string consumerName)
        where TEvent : IIntegrationEventEnvelope
        where TConsumer : class, IIntegrationEventHandler<TEvent>
    {
        services.AddIntegrationEventDeadLetterReplay<TEvent, MaintenanceReplayConsumer<TEvent, TConsumer>>(consumerName,
            (consumer, integrationEvent, token) => consumer.HandleAsync(integrationEvent, token),
            (provider, integrationEvent, token) => provider.GetRequiredService<ApplicationDbContext>()
                .ProcessedIntegrationEvents.AsNoTracking().AnyAsync(
                    x => x.ConsumerName == consumerName && x.IdempotencyKey == integrationEvent.IdempotencyKey, token));
    }
}

// CAP 的消费事务过滤器不在 HTTP 重放调用栈上；使用同一服务 UoW 提交 inbox、工单/计划和 outbox。
internal sealed class MaintenanceReplayConsumer<TEvent, TConsumer>(TConsumer consumer, ITransactionUnitOfWork unitOfWork)
    where TEvent : IIntegrationEventEnvelope
    where TConsumer : class, IIntegrationEventHandler<TEvent>
{
    public async Task HandleAsync(TEvent integrationEvent, CancellationToken token)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(token);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await consumer.HandleAsync(integrationEvent, token);
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(token);
            await unitOfWork.CommitAsync(token);
        }
        catch
        {
            await unitOfWork.RollbackAsync(token);
            throw;
        }
        finally
        {
            unitOfWork.CurrentTransaction = null;
        }
    }
}
