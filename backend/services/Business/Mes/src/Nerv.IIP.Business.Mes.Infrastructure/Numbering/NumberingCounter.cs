#pragma warning disable S1144 // EF Core sets surrogate identifiers through materialization.
namespace Nerv.IIP.Business.Mes.Infrastructure.Numbering;

public sealed class NumberingCounter
{
    private NumberingCounter() { }

    public NumberingCounter(string organizationId, string environmentId, string documentType, string siteCode, string dateSegment, string prefix)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        DocumentType = documentType;
        SiteCode = siteCode;
        DateSegment = dateSegment;
        Prefix = prefix;
    }

    public long Id { get; private set; }
    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DocumentType { get; private set; } = string.Empty;
    public string SiteCode { get; private set; } = string.Empty;
    public string DateSegment { get; private set; } = string.Empty;
    public string Prefix { get; private set; } = string.Empty;
    public long CurrentValue { get; private set; }
    public long Version { get; private set; }

    public long Advance()
    {
        CurrentValue++;
        Version++;
        return CurrentValue;
    }
}
