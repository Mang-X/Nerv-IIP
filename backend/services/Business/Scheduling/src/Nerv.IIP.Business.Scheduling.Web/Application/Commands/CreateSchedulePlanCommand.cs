using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record CreateSchedulePlanCommand(SchedulingProblemContract Problem) : ICommand<SchedulePlanContract>;

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
    ISchedulingOperationOverrideOverlay? overrideOverlay = null) : ICommandHandler<CreateSchedulePlanCommand, SchedulePlanContract>
{
    public async Task<SchedulePlanContract> Handle(CreateSchedulePlanCommand request, CancellationToken cancellationToken)
    {
        var overlaidProblem = overrideOverlay is null
            ? request.Problem
            : await overrideOverlay.ApplyAsync(request.Problem, cancellationToken);
        var normalizedProblem = SchedulingProblemNormalizer.Normalize(overlaidProblem);
        var problemFingerprint = CalculateProblemFingerprint(normalizedProblem);
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
                throw new KnownException($"Schedule problem already exists with a different fingerprint, ProblemId = {request.Problem.ProblemId}");
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
                ?? throw new KnownException($"Schedule problem snapshot exists but generated plan was not found, ProblemId = {request.Problem.ProblemId}");
            return SchedulePlanContractMapper.ToContract(existingPlan);
        }

        var generatedAtUtc = timeProvider.GetUtcNow();
        var availability = await equipmentAvailabilityProvider.QueryAsync(overlaidProblem, cancellationToken);
        var materialReadiness = await materialReadinessProvider.QueryAsync(overlaidProblem, cancellationToken);
        var schedulingProblem = MaterialReadinessSchedulingAdapter.Apply(
            EquipmentAvailabilitySchedulingAdapter.Apply(overlaidProblem, availability),
            materialReadiness);
        var preview = scheduler.Schedule(schedulingProblem, $"plan-{Guid.CreateVersion7():N}", generatedAtUtc);
        var generated = SchedulePlanContractMapper.WithStatus(preview, SchedulePlanStatusContract.Generated);
        dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            overlaidProblem.ProblemId,
            overlaidProblem.ContractVersion,
            overlaidProblem.OrganizationId,
            overlaidProblem.EnvironmentId,
            problemFingerprint,
            JsonSerializer.Serialize(normalizedProblem, SchedulingJson.Options),
            overlaidProblem.HorizonStartUtc,
            overlaidProblem.HorizonEndUtc,
            generatedAtUtc));
        dbContext.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            overlaidProblem.OrganizationId,
            overlaidProblem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(generated)));
        return generated;
    }

    private static string CalculateProblemFingerprint(SchedulingProblemContract problem)
    {
        var normalizedProblem = SchedulingProblemNormalizer.Normalize(problem);
        var json = JsonSerializer.Serialize(normalizedProblem, SchedulingJson.Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
