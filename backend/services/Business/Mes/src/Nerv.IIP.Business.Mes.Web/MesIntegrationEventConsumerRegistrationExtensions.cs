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
        services.AddScoped<QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext>();
        services.AddScoped<StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted>();
        return services;
    }
}
