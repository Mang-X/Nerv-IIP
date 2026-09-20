using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Business.Erp.Web.Application.Queries.Finance;

[JsonConverter(typeof(MachineOverheadReadStatusJsonConverter))]
public enum MachineOverheadReadStatus
{
    [EnumMember(Value = "available")]
    Available,
    [EnumMember(Value = "notApplicable")]
    NotApplicable,
    [EnumMember(Value = "unavailable")]
    Unavailable,
}

public sealed class MachineOverheadReadStatusJsonConverter()
    : JsonStringEnumConverter<MachineOverheadReadStatus>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);
