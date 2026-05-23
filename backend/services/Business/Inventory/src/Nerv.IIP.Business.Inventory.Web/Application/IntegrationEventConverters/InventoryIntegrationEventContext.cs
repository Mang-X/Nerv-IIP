using System.Diagnostics;
using System.Security.Claims;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEvents;

namespace Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;

public sealed record InventoryIntegrationEventContext(
    string CorrelationId,
    string CausationId,
    string Actor);

public interface IInventoryIntegrationEventContextAccessor
{
    InventoryIntegrationEventContext GetContext();
}

public sealed class HttpInventoryIntegrationEventContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IInventoryIntegrationEventContextAccessor
{
    public InventoryIntegrationEventContext GetContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var correlationId = FirstNonBlank(
            httpContext?.Request.Headers["x-correlation-id"].FirstOrDefault(),
            Activity.Current?.TraceId.ToString(),
            $"corr-{Guid.CreateVersion7():N}");
        var causationId = FirstNonBlank(
            httpContext?.Request.Headers["x-causation-id"].FirstOrDefault(),
            Activity.Current?.SpanId.ToString(),
            $"cause-{Guid.CreateVersion7():N}");
        var actor = FirstNonBlank(
            httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            httpContext?.User.Identity?.Name,
            $"system:{InventoryIntegrationEventSources.BusinessInventory}");

        return new InventoryIntegrationEventContext(correlationId, causationId, actor);
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!;
    }
}
