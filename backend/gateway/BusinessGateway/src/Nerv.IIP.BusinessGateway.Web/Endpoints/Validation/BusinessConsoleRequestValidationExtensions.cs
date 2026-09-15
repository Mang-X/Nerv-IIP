using System.Linq.Expressions;
using FluentValidation;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Validation;

internal static class BusinessConsoleRequestValidationExtensions
{
    public static void Tenant<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string>> organizationId,
        Expression<Func<T, string>> environmentId)
    {
        validator.RuleFor(organizationId).NotEmpty().MaximumLength(100);
        validator.RuleFor(environmentId).NotEmpty().MaximumLength(100);
    }

    public static void OffsetPagination<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> skip,
        Expression<Func<T, int>> take,
        int minimumTake,
        int maximumTake)
    {
        validator.RuleFor(skip).GreaterThanOrEqualTo(0);
        validator.Take(take, minimumTake, maximumTake);
    }

    public static void Take<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> take,
        int minimum,
        int maximum) =>
        validator.RuleFor(take).InclusiveBetween(minimum, maximum);

    public static void OptionalKeyword<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> keyword) =>
        validator.RuleFor(keyword).MaximumLength(100);
}
