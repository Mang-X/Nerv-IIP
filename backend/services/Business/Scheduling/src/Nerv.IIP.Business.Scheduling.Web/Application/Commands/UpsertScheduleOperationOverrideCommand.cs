using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Commands;

public sealed record UpsertScheduleOperationOverrideCommand(
    string OrganizationId, string EnvironmentId, string PlanId, string OperationId,
    string ResourceId, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
    : ICommand<ScheduleOperationOverrideResponse>;

public sealed record ScheduleOperationOverrideResponse(
    string OperationId, string WorkOrderId, string ResourceId, string WorkCenterId,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, string LockReasonCode);

public sealed class UpsertScheduleOperationOverrideCommandValidator
    : AbstractValidator<UpsertScheduleOperationOverrideCommand>
{
    public UpsertScheduleOperationOverrideCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OperationId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ResourceId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc);
    }
}

public sealed class UpsertScheduleOperationOverrideCommandHandler(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    ISchedulingIntegrationEventContextAccessor contextAccessor)
    : ICommandHandler<UpsertScheduleOperationOverrideCommand, ScheduleOperationOverrideResponse>
{
    public async Task<ScheduleOperationOverrideResponse> Handle(
        UpsertScheduleOperationOverrideCommand request,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.SchedulePlans.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId && x.PlanId == request.PlanId, cancellationToken)
            ?? throw new KnownException($"Schedule plan was not found, PlanId = {request.PlanId}");
        var snapshot = await dbContext.ScheduleProblems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId && x.ProblemId == plan.ProblemId, cancellationToken)
            ?? throw new KnownException($"Schedule problem snapshot was not found, ProblemId = {plan.ProblemId}");
        SchedulingProblemContract problem;
        try
        {
            problem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)
                ?? throw new JsonException("The scheduling problem payload is empty.");
            problem = SchedulingProblemNormalizer.Normalize(problem);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NullReferenceException)
        {
            throw new KnownException(
                $"Schedule problem details are unavailable for manual override, ProblemId = {plan.ProblemId}");
        }
        var pair = problem.Orders
            .SelectMany(order => order.Operations.Select(operation => (Order: order, Operation: operation)))
            .SingleOrDefault(x => x.Operation.OperationId == request.OperationId);
        if (pair.Operation is null)
        {
            throw new KnownException($"Schedule operation was not found, OperationId = {request.OperationId}");
        }
        if (!pair.Operation.EligibleResourceIds.Contains(request.ResourceId, StringComparer.Ordinal))
        {
            throw new KnownException($"Resource is not eligible for operation, ResourceId = {request.ResourceId}, OperationId = {request.OperationId}");
        }
        var resource = problem.Resources.SingleOrDefault(x => x.ResourceId == request.ResourceId)
            ?? throw new KnownException($"Schedule resource was not found, ResourceId = {request.ResourceId}");
        var now = timeProvider.GetUtcNow();
        var actor = contextAccessor.GetContext().Actor;
        var fact = await dbContext.ScheduleOperationOverrides.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId &&
            x.OperationId == request.OperationId, cancellationToken);
        if (fact is null)
        {
            fact = ScheduleOperationOverride.Create(
                request.OrganizationId, request.EnvironmentId, pair.Order.OrderId,
                request.OperationId, pair.Operation.OperationSequence, request.ResourceId,
                resource.WorkCenterId, request.StartUtc, request.EndUtc, "manual-override",
                "scheduling-api", null, actor, now, now);
            dbContext.ScheduleOperationOverrides.Add(fact);
        }
        else
        {
            fact.TryReplace(request.ResourceId, resource.WorkCenterId, request.StartUtc,
                request.EndUtc, "manual-override", "scheduling-api", null, actor, now, now);
        }

        return new ScheduleOperationOverrideResponse(
            fact.OperationId, fact.WorkOrderId, fact.ResourceId, fact.WorkCenterId,
            fact.StartUtc, fact.EndUtc, fact.LockReasonCode);
    }
}
