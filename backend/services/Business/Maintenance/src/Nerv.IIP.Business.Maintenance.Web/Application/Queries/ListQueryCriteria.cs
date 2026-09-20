using System.Linq.Expressions;

namespace Nerv.IIP.Business.Maintenance.Web.Application.Queries;

public sealed record TenantScope
{
    private TenantScope(string organizationId, string environmentId)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
    }

    public string OrganizationId { get; }
    public string EnvironmentId { get; }

    public static TenantScope From(string organizationId, string environmentId)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            throw new KnownException("组织标识不能为空。");
        }

        if (string.IsNullOrWhiteSpace(environmentId))
        {
            throw new KnownException("环境标识不能为空。");
        }

        return new TenantScope(organizationId.Trim(), environmentId.Trim());
    }
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
    public const int MaxTake = 200;

    public static OffsetPage From(int skip, int take) =>
        new(Math.Max(0, skip), Math.Clamp(take, 1, MaxTake));
}

public sealed record SearchTerm
{
    private SearchTerm(string? value) => Value = value;

    public string? Value { get; }

    public static SearchTerm From(string? value) =>
        new(string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant());
}

public static class ListQueryValidationExtensions
{
    public static void AddTenantRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string>> organizationId,
        Expression<Func<T, string>> environmentId)
    {
        validator.RuleFor(organizationId)
            .NotEmpty().WithMessage("组织标识不能为空。");
        validator.RuleFor(environmentId)
            .NotEmpty().WithMessage("环境标识不能为空。");
    }
}
