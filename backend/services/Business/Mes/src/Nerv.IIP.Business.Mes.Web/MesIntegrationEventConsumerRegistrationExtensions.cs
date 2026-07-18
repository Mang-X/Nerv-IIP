using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Business.Mes.Web;

public static class MesIntegrationEventConsumerRegistrationExtensions
{
    public static IServiceCollection AddMesIntegrationEventConsumers(this IServiceCollection services)
    {
        services.AddScoped<AssetUnavailableIntegrationEventHandlerForReschedule>();
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
        return services;
    }
}
