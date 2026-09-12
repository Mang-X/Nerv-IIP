using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Contracts.BarcodeLabel;

public sealed record GetTemplateAssetRetirementRequest(
    string OrganizationId, string EnvironmentId, Guid TemplateId, string FileId);

public sealed record TemplateAssetRetirementResponse(
    string FileId, string? Checksum, Guid? DecisionId, TemplateAssetRetirementStatus? Status);

[JsonConverter(typeof(TemplateAssetRetirementStatusJsonConverter))]
public enum TemplateAssetRetirementStatus
{
    [EnumMember(Value = "pending")]
    Pending,
    [EnumMember(Value = "quota-released")]
    QuotaReleased,
    [EnumMember(Value = "execution-outcome-unknown")]
    ExecutionOutcomeUnknown,
    [EnumMember(Value = "replay-window-expired")]
    ReplayWindowExpired,
}

public sealed class TemplateAssetRetirementStatusJsonConverter()
    : JsonStringEnumConverter<TemplateAssetRetirementStatus>(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false);
