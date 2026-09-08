using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web;

public static class MesIntegrationEventConsumerRegistrationExtensions
{
    public static IServiceCollection AddMesIntegrationEventConsumers(this IServiceCollection services)
    {
        services.AddScoped<MesAssetUnavailableCanonicalProcessor>();
        services.AddScoped<IMesAssetUnavailableCanonicalProcessor>(provider => provider.GetRequiredService<MesAssetUnavailableCanonicalProcessor>());
        services.AddScoped<AssetUnavailableIntegrationEventHandlerForReschedule>();
        services.AddScoped<AssetUnavailableV2IntegrationEventHandlerForReschedule>();
        services.AddScoped<IIntegrationEventDeadLetterReplayHandler, MesAssetUnavailableDeadLetterReplayHandler>();
        services.AddScoped<IntegrationEventDeadLetterReplayExecutor>();
        services.AddScoped<AssetRestoredIntegrationEventHandlerForReschedule>();
        services.AddScoped<NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect>();
        services.AddScoped<PlanningSuggestionAcceptedIntegrationEventHandlerForCreateMesWorkOrder>();
        services.AddScoped<EngineeringChangeReleasedIntegrationEventHandlerForMesWip>();
        services.AddScoped<ProductionVersionCreatedIntegrationEventHandlerForBindMesWorkOrders>();
        services.AddScoped<QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext>();
        services.AddScoped<StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted>();
        services.AddScoped<StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed>();
        services.AddScoped<InventoryReservationExpiredIntegrationEventHandlerForMarkMesRequestExpired>();
        services.AddScoped<SchedulePlanReleasedIntegrationEventHandlerForDispatch>();
        services.AddScoped<SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch>();
        services.AddScoped<SchedulePlanInvalidatedIntegrationEventHandlerForMarkInvalidated>();
        services.AddScoped<SkuDisabledIntegrationEventHandlerForProjectMesSkuAvailability>();
        return services;
    }
}
