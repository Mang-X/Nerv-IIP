using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;

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
    SchedulingPlanGenerator generator,
    TimeProvider timeProvider,
    OrderUrgencyService urgencyService) : ICommandHandler<CreateSchedulePlanCommand, SchedulePlanContract>
{
    public async Task<SchedulePlanContract> Handle(CreateSchedulePlanCommand request, CancellationToken cancellationToken)
    {
        var generatedAtUtc = timeProvider.GetUtcNow();
        var generation = await generator.GenerateAsync(
            request.Problem,
            $"plan-{Guid.CreateVersion7():N}",
            generatedAtUtc,
            cancellationToken);
        var baseProblem = generation.Constraints.BaseProblem;
        var effectiveProblem = generation.Constraints.EffectiveProblem;
        var normalizedProblem = SchedulingProblemNormalizer.Normalize(baseProblem);
        var problemFingerprint = CalculateProblemFingerprint(normalizedProblem);
        var existingSnapshot = await dbContext.ScheduleProblems.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == baseProblem.OrganizationId &&
                    x.EnvironmentId == baseProblem.EnvironmentId &&
                    x.ProblemId == baseProblem.ProblemId,
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
                    x => x.OrganizationId == baseProblem.OrganizationId &&
                        x.EnvironmentId == baseProblem.EnvironmentId &&
                        x.ProblemId == baseProblem.ProblemId,
                    cancellationToken)
                ?? throw new KnownException($"Schedule problem snapshot exists but generated plan was not found, ProblemId = {request.Problem.ProblemId}");
            var existingPlanContract = SchedulePlanContractMapper.ToContract(
                existingPlan,
                existingSnapshot.EngineInputFingerprint);
            await urgencyService.CapturePlanAsync(
                effectiveProblem,
                existingPlanContract,
                CalculateProblemFingerprint(effectiveProblem),
                generatedAtUtc,
                cancellationToken);
            return existingPlanContract;
        }

        var urgencyInputFingerprint = CalculateProblemFingerprint(effectiveProblem);
        var normalizedEngineInput = SchedulingProblemNormalizer.Normalize(effectiveProblem);
        var engineInputJson = JsonSerializer.Serialize(normalizedEngineInput, SchedulingJson.Options);
        var generated = SchedulePlanContractMapper.WithProvenance(
            SchedulePlanContractMapper.WithStatus(
                generation.Plan,
                SchedulePlanStatusContract.Generated),
            generation,
            urgencyInputFingerprint);
        dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            baseProblem.ProblemId,
            baseProblem.ContractVersion,
            baseProblem.OrganizationId,
            baseProblem.EnvironmentId,
            problemFingerprint,
            JsonSerializer.Serialize(normalizedProblem, SchedulingJson.Options),
            baseProblem.HorizonStartUtc,
            baseProblem.HorizonEndUtc,
            generatedAtUtc,
            urgencyInputFingerprint,
            engineInputJson));
        dbContext.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            baseProblem.OrganizationId,
            baseProblem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(generated),
            SchedulePlanContractMapper.ToExecutionTrace(generated)));
        await urgencyService.CapturePlanAsync(
            effectiveProblem, generated, urgencyInputFingerprint, generatedAtUtc, cancellationToken);
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
