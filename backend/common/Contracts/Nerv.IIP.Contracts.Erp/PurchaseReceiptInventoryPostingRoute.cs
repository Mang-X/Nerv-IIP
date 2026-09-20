using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Contracts.Erp;

[JsonConverter(typeof(PurchaseReceiptInventoryPostingRouteJsonConverter))]
public enum PurchaseReceiptInventoryPostingRoute
{
    [EnumMember(Value = "direct")]
    Direct = 0,
    [EnumMember(Value = "wms")]
    Wms = 1,
}

public sealed class PurchaseReceiptInventoryPostingRouteJsonConverter()
    : JsonStringEnumConverter<PurchaseReceiptInventoryPostingRoute>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);
