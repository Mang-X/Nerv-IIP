namespace Nerv.IIP.Business.Wms.Web.Application.Queries;

public sealed record TenantScope
{
    private TenantScope(string? organizationId, string? environmentId)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
    }

    public string? OrganizationId { get; }
    public string? EnvironmentId { get; }

    public static TenantScope From(string? organizationId, string? environmentId)
        => new(Normalize(organizationId), Normalize(environmentId));

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record OffsetPage
{
    private OffsetPage(int skip, int take)
    {
        Skip = skip;
        Take = take;
    }

    public int Skip { get; }
    public int Take { get; }

    public const int DefaultTake = 100;
    public const int MaxTake = 500;

    public static OffsetPage From(int skip, int take) =>
        new(Math.Max(0, skip), take <= 0 ? DefaultTake : Math.Min(take, MaxTake));
}

public static class ListQueryCriteria
{
    public static string? NormalizeKeyword(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
