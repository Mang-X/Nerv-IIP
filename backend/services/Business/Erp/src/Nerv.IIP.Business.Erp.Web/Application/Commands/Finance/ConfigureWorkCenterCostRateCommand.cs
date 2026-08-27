using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Infrastructure.Repositories;
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
        RuleFor(x => x.OrganizationId).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(100);
        RuleFor(x => x.WorkCenterId).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(100);
        RuleFor(x => x.HourlyRate).GreaterThan(0m);
        RuleFor(x => x.CurrencyCode).Must(WorkCenterRateCanonicalization.IsAsciiCurrencyCode);
        RuleFor(x => x.EffectiveFromUtc).NotEmpty().Must(BeUtc);
        RuleFor(x => x.EffectiveToUtc)
            .Must(value => value is null || BeUtc(value.Value))
            .Must((command, value) => value is null || value > command.EffectiveFromUtc);
        RuleFor(x => x.ChangedBy).Must(WorkCenterRateCanonicalization.IsCanonicalActor).MaximumLength(200);
        RuleFor(x => x.Reason).Must(WorkCenterRateCanonicalization.IsNonBlank).MaximumLength(500);
        RuleFor(x => x.ChangedAtUtc).Must(BeUtc);
    }

    private static bool BeUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;
}

public sealed class ConfigureWorkCenterCostRateCommandHandler(
    IWorkCenterCostRateRepository rates,
    IWorkCenterRateRevisionAllocator revisions)
    : ICommandHandler<ConfigureWorkCenterCostRateCommand, WorkCenterCostRateId>
{
    public async Task<WorkCenterCostRateId> Handle(ConfigureWorkCenterCostRateCommand request, CancellationToken cancellationToken)
    {
        var allocation = await revisions.AllocateAsync(WorkCenterRateKind.Labor,
            request.OrganizationId, request.EnvironmentId, request.WorkCenterId, null,
            request.CurrencyCode, cancellationToken);

        var rate = WorkCenterCostRate.Define(
            allocation.OrganizationId, allocation.EnvironmentId, allocation.WorkCenterId,
            request.HourlyRate,
            allocation.CurrencyCode,
            request.EffectiveFromUtc,
            request.EffectiveToUtc,
            allocation.Revision,
            request.ChangedBy,
            request.Reason,
            request.ChangedAtUtc);
        await rates.AddAsync(rate, cancellationToken);
        return rate.Id;
    }
}
