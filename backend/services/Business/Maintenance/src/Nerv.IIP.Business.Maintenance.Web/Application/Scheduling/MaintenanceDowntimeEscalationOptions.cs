namespace Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;

public sealed class MaintenanceDowntimeEscalationOptions
{
    public TimeSpan Threshold { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(5);
    public List<MaintenanceDowntimeEscalationScope> Scopes { get; set; } = [];
}

public sealed class MaintenanceDowntimeEscalationScope
{
    public string OrganizationId { get; set; } = string.Empty;
    public string EnvironmentId { get; set; } = string.Empty;
    public string[] PlannerRecipientRefs { get; set; } = [];
}
