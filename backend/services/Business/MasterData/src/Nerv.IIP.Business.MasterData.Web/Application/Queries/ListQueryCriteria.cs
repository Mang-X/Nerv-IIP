using System.Linq.Expressions;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record TenantScope(string OrganizationId, string EnvironmentId)
{
    public static TenantScope From(string organizationId, string environmentId) =>
        new(organizationId.Trim(), environmentId.Trim());
}

public sealed record OffsetPage(int Skip, int Take)
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;

    public static OffsetPage From(int skip, int take) =>
        new(Math.Max(0, skip), Math.Clamp(take, 1, MaxTake));
}

public sealed record SearchTerm(string? Value)
{
    public static SearchTerm From(string? value) =>
        new(string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant());
}

public sealed record MasterDataListQueryCriteria(
    TenantScope Tenant,
    OffsetPage Page,
    SearchTerm Keyword);

public static class ListMasterDataResourcesQueryCriteriaExtensions
{
    public static MasterDataListQueryCriteria ToCriteria(this ListMasterDataResourcesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return new MasterDataListQueryCriteria(
            TenantScope.From(query.OrganizationId, query.EnvironmentId),
            OffsetPage.From(query.Skip, query.Take),
            SearchTerm.From(query.Keyword));
    }
}

public static class ListQueryValidationExtensions
{
    public static void AddTenantRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string>> organizationId,
        Expression<Func<T, string>> environmentId)
    {
        validator.RuleFor(organizationId)
            .NotEmpty().WithMessage("组织标识不能为空。")
            .MaximumLength(64).WithMessage("组织标识不能超过 64 个字符。");
        validator.RuleFor(environmentId)
            .NotEmpty().WithMessage("环境标识不能为空。")
            .MaximumLength(64).WithMessage("环境标识不能超过 64 个字符。");
    }

    public static void AddOffsetPageRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> skip,
        Expression<Func<T, int>> take)
    {
        validator.RuleFor(skip)
            .GreaterThanOrEqualTo(0).WithMessage("skip 不能小于 0。");
        validator.RuleFor(take)
            .InclusiveBetween(1, OffsetPage.MaxTake).WithMessage("take 必须在 1 至 500 之间。");
    }

    public static void AddKeywordRule<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> keyword)
    {
        validator.RuleFor(keyword)
            .MaximumLength(200).WithMessage("关键字不能超过 200 个字符。");
    }
}
