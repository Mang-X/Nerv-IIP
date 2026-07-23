using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

public sealed record ConfigureWorkCenterCostRateCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkCenterId,
    decimal HourlyRate,
    string CurrencyCode,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string ChangedBy,
    string Reason,
    DateTimeOffset ChangedAtUtc) : ICommand<WorkCenterCostRateId>;

public sealed class ConfigureWorkCenterCostRateCommandValidator : AbstractValidator<ConfigureWorkCenterCostRateCommand>
{
    public ConfigureWorkCenterCostRateCommandValidator()
    {
        RuleFor(x => x.OrganizationId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).Must(BeNonBlank).MaximumLength(100);
        RuleFor(x => x.HourlyRate).GreaterThan(0m);
        RuleFor(x => x.CurrencyCode).Must(BeAsciiCurrencyCode);
        RuleFor(x => x.EffectiveFromUtc).Must(BeUtc);
        RuleFor(x => x.EffectiveToUtc)
            .Must(value => value is null || BeUtc(value.Value))
            .Must((command, value) => value is null || value > command.EffectiveFromUtc);
        RuleFor(x => x.ChangedBy).Must(BeCanonicalActor).MaximumLength(200);
        RuleFor(x => x.Reason).Must(BeNonBlank).MaximumLength(500);
        RuleFor(x => x.ChangedAtUtc).Must(BeUtc);
    }

    private static bool BeNonBlank(string value) => !string.IsNullOrWhiteSpace(value);

    private static bool BeAsciiCurrencyCode(string value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Trim().Length == 3
            && value.Trim().All(character => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));

    private static bool BeCanonicalActor(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Any(char.IsWhiteSpace)) return false;
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 && separator < value.Length - 1;
    }

    private static bool BeUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;
}

public sealed class ConfigureWorkCenterCostRateCommandHandler(
    ApplicationDbContext dbContext,
    IWorkCenterCostRateRevisionLock revisionLock)
    : ICommandHandler<ConfigureWorkCenterCostRateCommand, WorkCenterCostRateId>
{
    public async Task<WorkCenterCostRateId> Handle(ConfigureWorkCenterCostRateCommand request, CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var workCenterId = request.WorkCenterId.Trim();
        await revisionLock.AcquireAsync(organizationId, environmentId, workCenterId, cancellationToken);
        var databaseRevision = await dbContext.WorkCenterCostRates
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId)
            .Select(x => (int?)x.Revision)
            .MaxAsync(cancellationToken) ?? 0;
        var localRevision = dbContext.WorkCenterCostRates.Local
            .Where(x => x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkCenterId == workCenterId)
            .Select(x => x.Revision)
            .DefaultIfEmpty(0)
            .Max();

        var rate = WorkCenterCostRate.Define(
            organizationId,
            environmentId,
            workCenterId,
            request.HourlyRate,
            request.CurrencyCode,
            request.EffectiveFromUtc,
            request.EffectiveToUtc,
            Math.Max(databaseRevision, localRevision) + 1,
            request.ChangedBy,
            request.Reason,
            request.ChangedAtUtc);
        dbContext.WorkCenterCostRates.Add(rate);
        return rate.Id;
    }
}
