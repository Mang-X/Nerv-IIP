using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;

namespace Nerv.IIP.Business.Mes.Web.Application.Andon;

public sealed class AndonEscalationOptions
{
    public const string SectionName = "Mes:AndonEscalation";
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(30);
    public List<AndonEscalationPolicy> Policies { get; set; } = [];

    public bool IsValid() => ScanInterval > TimeSpan.Zero
        && Policies.All(p => ValidId(p.OrganizationId) && ValidId(p.EnvironmentId) && ValidId(p.RecipientId)
            && p.Category is not null && Enum.IsDefined(p.Category.Value) && p.UnclaimedTimeout > TimeSpan.Zero)
        && Policies.Select(p => (p.OrganizationId, p.EnvironmentId, p.Category)).Distinct().Count() == Policies.Count;

    private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value == value.Trim();
}

public sealed class AndonEscalationPolicy
{
    public string OrganizationId { get; set; } = string.Empty;
    public string EnvironmentId { get; set; } = string.Empty;
    public AndonCallCategory? Category { get; set; }
    public TimeSpan UnclaimedTimeout { get; set; }
    public string RecipientId { get; set; } = string.Empty;
}
