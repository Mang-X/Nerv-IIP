using Nerv.IIP.Contracts.Inventory;

namespace Nerv.IIP.Business.Inventory.Web.Application.Expiry;

public sealed class StockReservationExpirationOptions
{
    public bool Enabled { get; set; } = true;

    public int BatchSize { get; set; } = 100;

    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan WmsDefaultLifetime { get; set; } = TimeSpan.FromHours(2);

    public TimeSpan MesDefaultLifetime { get; set; } = TimeSpan.FromHours(8);

    public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromHours(4);

    public TimeSpan ResolveLifetime(string sourceService)
    {
        var lifetime = sourceService.Trim().ToLowerInvariant() switch
        {
            "wms" or InventoryIntegrationEventSources.BusinessWms => WmsDefaultLifetime,
            "mes" or InventoryIntegrationEventSources.BusinessMes => MesDefaultLifetime,
            _ => DefaultLifetime,
        };
        return lifetime > TimeSpan.Zero ? lifetime : TimeSpan.FromHours(4);
    }
}
