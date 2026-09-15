using System.Linq.Expressions;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries;

public sealed record TenantScope
{
    private TenantScope(string organizationId, string environmentId)
    {
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
    }

    public string OrganizationId { get; }
    public string EnvironmentId { get; }

    public static TenantScope From(string organizationId, string environmentId) =>
        new(organizationId.Trim(), environmentId.Trim());
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

    public static OffsetPage From(int skip, int take, int maxTake = MaxTake) =>
        new(Math.Max(0, skip), Math.Clamp(take, 1, maxTake));
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
        validator.RuleFor(organizationId).NotEmpty().MaximumLength(100);
        validator.RuleFor(environmentId).NotEmpty().MaximumLength(100);
    }

    public static void AddOffsetPageRules<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> skip,
        Expression<Func<T, int>> take,
        int maxTake = OffsetPage.MaxTake)
    {
        validator.RuleFor(skip).GreaterThanOrEqualTo(0);
        validator.RuleFor(take).InclusiveBetween(1, maxTake);
    }

    public static void AddSearchTermRule<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> keyword,
        int maxLength = 200)
    {
        validator.RuleFor(keyword).MaximumLength(maxLength);
    }
}
