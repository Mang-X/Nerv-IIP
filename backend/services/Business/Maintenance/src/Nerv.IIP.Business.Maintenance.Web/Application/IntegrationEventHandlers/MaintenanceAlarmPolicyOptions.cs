using Microsoft.Extensions.Options;

namespace Nerv.IIP.Business.Maintenance.Web.Application.IntegrationEventHandlers;

public sealed class MaintenanceAlarmPolicyOptions
{
    public const string SectionName = "Maintenance:AlarmPolicy";
    public List<MaintenanceAlarmPolicyEntry> Entries { get; set; } = [];

    public string? ResolveReasonCode(string organizationId, string environmentId, string alarmCode) =>
        Entries.SingleOrDefault(entry =>
            entry.OrganizationId == organizationId && entry.EnvironmentId == environmentId &&
            (entry.AlarmCode is null || entry.AlarmCode == alarmCode))?.AssetUnavailableReasonCode;
}

public sealed class MaintenanceAlarmPolicyEntry
{
    public string OrganizationId { get; set; } = "";
    public string EnvironmentId { get; set; } = "";
    public string? AlarmCode { get; set; }
    public string Mode { get; set; } = "";
    public string? AssetUnavailableReasonCode { get; set; }
}

public sealed class MaintenanceAlarmPolicyOptionsValidator : IValidateOptions<MaintenanceAlarmPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, MaintenanceAlarmPolicyOptions options)
    {
        for (var i = 0; i < options.Entries.Count; i++)
        {
            var entry = options.Entries[i];
            if (string.IsNullOrWhiteSpace(entry.OrganizationId) || string.IsNullOrWhiteSpace(entry.EnvironmentId) ||
                entry.AlarmCode is not null && string.IsNullOrWhiteSpace(entry.AlarmCode))
            {
                return ValidateOptionsResult.Fail($"Maintenance:AlarmPolicy:Entries:{i} requires a scope and a nonblank exact AlarmCode when supplied.");
            }

            if (!(entry.Mode == "WorkOrderOnly" && entry.AssetUnavailableReasonCode is null ||
                  entry.Mode == "WorkOrderAndOccupy" && entry.AssetUnavailableReasonCode is { Length: > 0 and <= 100 }))
            {
                return ValidateOptionsResult.Fail($"Maintenance:AlarmPolicy:Entries:{i} requires WorkOrderOnly without a reason or WorkOrderAndOccupy with an exact reason code.");
            }

            if (options.Entries.Take(i).Any(other => other.OrganizationId == entry.OrganizationId &&
                other.EnvironmentId == entry.EnvironmentId &&
                (other.AlarmCode is null || entry.AlarmCode is null || other.AlarmCode == entry.AlarmCode)))
            {
                return ValidateOptionsResult.Fail($"Maintenance:AlarmPolicy:Entries:{i} overlaps another entry in the same scope.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
