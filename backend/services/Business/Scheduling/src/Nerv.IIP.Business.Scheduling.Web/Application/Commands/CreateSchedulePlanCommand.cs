using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record CreateSchedulePlanCommand(
    SchedulingProblemContract Problem,
    IReadOnlyCollection<FixedWorkCenterReservation>? FixedReservations = null) : ICommand<SchedulePlanContract>;

public sealed class CreateSchedulePlanCommandValidator : AbstractValidator<CreateSchedulePlanCommand>
{
    public CreateSchedulePlanCommandValidator()
    {
        RuleFor(x => x.Problem).NotNull();
        RuleFor(x => x.Problem.OrganizationId).NotEmpty().MaximumLength(64).When(x => x.Problem is not null);
        RuleFor(x => x.Problem.EnvironmentId).NotEmpty().MaximumLength(64).When(x => x.Problem is not null);
        RuleFor(x => x.Problem.HorizonEndUtc).GreaterThan(x => x.Problem.HorizonStartUtc).When(x => x.Problem is not null);
        RuleFor(x => x.Problem).Custom((problem, context) =>
        {
            foreach (var error in SchedulingProblemNormalizer.ValidateForErrors(problem))
            {
                context.AddFailure(error);
            }
        });
    }
}

public sealed class CreateSchedulePlanCommandHandler(
    ApplicationDbContext dbContext,
    FiniteCapacityScheduler scheduler,
    TimeProvider timeProvider,
    ISchedulingEquipmentAvailabilityProvider equipmentAvailabilityProvider,
    ISchedulingMaterialReadinessProvider materialReadinessProvider,
    ISchedulingOperationOverrideOverlay overrideOverlay,
    OrderUrgencyService urgencyService,
    SchedulingEquipmentUnknownModeOption equipmentUnknownMode) : ICommandHandler<CreateSchedulePlanCommand, SchedulePlanContract>
{
    public async Task<SchedulePlanContract> Handle(CreateSchedulePlanCommand request, CancellationToken cancellationToken)
    {
        var overlaidProblem = await overrideOverlay.ApplyAsync(request.Problem, cancellationToken);
        var availability = await equipmentAvailabilityProvider.QueryAsync(overlaidProblem, cancellationToken);
        var materialReadiness = await materialReadinessProvider.QueryAsync(overlaidProblem, cancellationToken);
        var schedulingProblem = SchedulingProblemNormalizer.Normalize(
            MaterialReadinessSchedulingAdapter.Apply(
                EquipmentAvailabilitySchedulingAdapter.Apply(overlaidProblem, availability, equipmentUnknownMode.Mode),
                materialReadiness));
        var fixedReservations = request.FixedReservations ?? [];
        var problemFingerprint = CalculateProblemFingerprint(schedulingProblem, fixedReservations);
        var existingSnapshot = await dbContext.ScheduleProblems.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == overlaidProblem.OrganizationId &&
                    x.EnvironmentId == overlaidProblem.EnvironmentId &&
                    x.ProblemId == overlaidProblem.ProblemId,
                cancellationToken);
        if (existingSnapshot is not null)
        {
            if (!string.Equals(existingSnapshot.ProblemFingerprint, problemFingerprint, StringComparison.Ordinal))
            {
                throw new KnownException($"排程问题已存在但指纹不同，请刷新后重试，问题 ID = {request.Problem.ProblemId}");
            }

            var existingPlan = await dbContext.SchedulePlans.AsNoTracking()
                .Include(x => x.Assignments)
                .Include(x => x.ResourceLoads)
                .Include(x => x.Conflicts)
                .Include(x => x.UnscheduledOperations)
                .AsSplitQuery()
                .SingleOrDefaultAsync(
                    x => x.OrganizationId == request.Problem.OrganizationId &&
                        x.EnvironmentId == request.Problem.EnvironmentId &&
                        x.ProblemId == request.Problem.ProblemId,
                    cancellationToken)
                ?? throw new KnownException($"排程问题快照已存在但未找到生成方案，请重新生成，问题 ID = {request.Problem.ProblemId}");
            var existingPlanContract = SchedulePlanContractMapper.ToContract(existingPlan, schedulingProblem);
            await urgencyService.CapturePlanAsync(
                schedulingProblem,
                existingPlanContract,
                problemFingerprint,
                timeProvider.GetUtcNow(),
                cancellationToken);
            return existingPlanContract;
        }

        var generatedAtUtc = timeProvider.GetUtcNow();
        var preview = scheduler.ScheduleNormalized(schedulingProblem, $"plan-{Guid.CreateVersion7():N}", generatedAtUtc, fixedReservations)
            with { ProblemFingerprint = problemFingerprint };
        var generated = SchedulePlanContractMapper.WithStatus(preview, SchedulePlanStatusContract.Generated);
        dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            overlaidProblem.ProblemId,
            overlaidProblem.ContractVersion,
            overlaidProblem.OrganizationId,
            overlaidProblem.EnvironmentId,
            problemFingerprint,
            SchedulingFrozenOccupancy.SerializeSnapshot(schedulingProblem, fixedReservations),
            overlaidProblem.HorizonStartUtc,
            overlaidProblem.HorizonEndUtc,
            generatedAtUtc));
        dbContext.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            overlaidProblem.OrganizationId,
            overlaidProblem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(generated)));
        await urgencyService.CapturePlanAsync(
            schedulingProblem, generated, problemFingerprint, generatedAtUtc, cancellationToken);
        return generated;
    }

    private static string CalculateProblemFingerprint(
        SchedulingProblemContract problem,
        IReadOnlyCollection<FixedWorkCenterReservation> fixedReservations)
    {
        var normalizedProblem = SchedulingProblemNormalizer.Normalize(problem);
        var json = SchedulingFrozenOccupancy.SerializeSnapshot(normalizedProblem, fixedReservations);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
