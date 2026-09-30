using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Application.Urgency;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulePlanValidationContextTests
{
    // DomainInvariant / PublicContract: #4040 的方案依据不能由已排条或当前来源反推。
    [Fact]
    public async Task Created_and_loaded_plan_preserve_snapshot_validation_facts()
    {
        await using var db = CreateDbContext();
        var source = ShockAbsorberSchedulingFixture.CreateProblem();
        var order = source.Orders.First();
        var first = order.Operations.First() with { EligibleResourceIds = [], SetupMinutes = 17 };
        var successor = order.Operations.Skip(1).First() with
        {
            PredecessorOperationIds = [first.OperationId],
            DueUtc = source.HorizonStartUtc.AddMinutes(75),
        };
        var problem = source with
        {
            Orders = [order with { Operations = [first, successor] }],
            Resources = source.Resources.Select(x => x with { CapacityUnits = 3, UtilizationRate = 0.5m }).ToArray(),
        };
        var fixedReservation = new FixedWorkCenterReservation(order.OrderId, successor.OperationId,
            successor.OperationSequence, problem.Resources.First().WorkCenterId,
            problem.HorizonStartUtc, problem.HorizonStartUtc.AddMinutes(30), null);
        var external = fixedReservation with { OrderId = "external-order", OperationId = "external-operation" };
        var handler = CreatePlanHandler(db);
        var plan = await handler.Handle(new CreateSchedulePlanCommand(problem, [fixedReservation, external]), CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.DoesNotContain(plan.Assignments, x => x.OperationId == first.OperationId);
        var context = JsonSerializer.SerializeToElement(plan, SchedulingJson.Options).GetProperty("validationContext");
        Assert.Equal(problem.HorizonStartUtc, context.GetProperty("horizonStartUtc").GetDateTimeOffset());
        Assert.Equal(problem.HorizonEndUtc, context.GetProperty("horizonEndUtc").GetDateTimeOffset());
        var resource = context.GetProperty("resources").EnumerateArray().Single(x =>
            x.GetProperty("resourceId").GetString() == problem.Resources.First().ResourceId);
        Assert.Equal(3, resource.GetProperty("capacityUnits").GetInt32());
        Assert.Equal(0.5m, resource.GetProperty("utilizationRate").GetDecimal());
        Assert.Equal(problem.Resources.First().CalendarId, resource.GetProperty("calendarId").GetString());
        Assert.Equal(problem.Resources.First().WorkCenterId, resource.GetProperty("workCenterId").GetString());
        var operations = context.GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(2, operations.Length);
        Assert.Equal(17, operations.Single(x => x.GetProperty("operationId").GetString() == first.OperationId).GetProperty("setupMinutes").GetInt32());
        var operation = operations.Single(x => x.GetProperty("operationId").GetString() == successor.OperationId);
        Assert.Equal(order.OrderId, operation.GetProperty("orderId").GetString());
        Assert.Equal(successor.DueUtc, operation.GetProperty("dueUtc").GetDateTimeOffset());
        Assert.Equal(successor.DurationMinutes, operation.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(first.OperationId, Assert.Single(operation.GetProperty("predecessorOperationIds").EnumerateArray()).GetString());
        Assert.True(operation.GetProperty("isFixed").GetBoolean());
        Assert.False(operations.Single(x => x.GetProperty("operationId").GetString() == first.OperationId).GetProperty("isFixed").GetBoolean());
        Assert.Equal(2, context.GetProperty("fixedReservations").GetArrayLength());
        Assert.Contains(context.GetProperty("fixedReservations").EnumerateArray(), x => x.GetProperty("orderId").GetString() == external.OrderId);

        var roundTrip = JsonSerializer.Deserialize<SchedulePlanContract>(JsonSerializer.Serialize(plan, SchedulingJson.Options), SchedulingJson.Options)!;
        Assert.Equal(context.GetRawText(), JsonSerializer.SerializeToElement(roundTrip, SchedulingJson.Options).GetProperty("validationContext").GetRawText());
        var detail = await new GetSchedulePlanDetailQueryHandler(db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<GetSchedulePlanDetailQueryHandler>.Instance).Handle(
            new GetSchedulePlanDetailQuery(plan.PlanId, problem.OrganizationId, problem.EnvironmentId), CancellationToken.None);
        Assert.Equal(context.GetRawText(), JsonSerializer.SerializeToElement(detail, SchedulingJson.Options).GetProperty("validationContext").GetRawText());
        var replay = await handler.Handle(new CreateSchedulePlanCommand(problem, [fixedReservation, external]), CancellationToken.None);
        Assert.Equal(context.GetRawText(), JsonSerializer.SerializeToElement(replay, SchedulingJson.Options).GetProperty("validationContext").GetRawText());
    }

    [Fact]
    public async Task Revision_candidate_keeps_excluded_frozen_order_as_external_occupancy()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var excludedOrder = problem.Orders.First();
        var includedOrder = problem.Orders.Last();
        var operation = excludedOrder.Operations.First();
        var frozen = new FixedWorkCenterReservation(
            excludedOrder.OrderId, operation.OperationId, operation.OperationSequence,
            problem.Resources.Single(x => x.ResourceId == operation.PrimaryResourceId).WorkCenterId,
            problem.HorizonStartUtc, problem.HorizonStartUtc.AddHours(1), null);
        var createHandler = CreatePlanHandler(db);
        var basePlan = await createHandler.Handle(new CreateSchedulePlanCommand(problem, [frozen]), CancellationToken.None);
        await db.SaveChangesAsync();

        var result = await new CreateSchedulePlanRevisionCommandHandler(db, new CreatePlanSender(createHandler)).Handle(
            new CreateSchedulePlanRevisionCommand(basePlan.PlanId, problem.OrganizationId, problem.EnvironmentId,
                [includedOrder.OrderId], []), CancellationToken.None);

        var candidate = result.Candidate;
        Assert.All(candidate.Assignments, x => Assert.Equal(includedOrder.OrderId, x.OrderId));
        var context = Assert.IsType<SchedulePlanValidationContextContract>(candidate.ValidationContext);
        Assert.All(context.Operations, x => Assert.Equal(includedOrder.OrderId, x.OrderId));
        Assert.Equal(includedOrder.Operations.Count, context.Operations.Count);
        var reservation = Assert.Single(context.FixedReservations);
        Assert.Equal(excludedOrder.OrderId, reservation.OrderId);
        Assert.Equal(frozen.OperationId, reservation.OperationId);
        Assert.Equal(frozen.WorkCenterId, reservation.WorkCenterId);
        Assert.Equal(frozen.StartUtc, reservation.StartUtc);
        Assert.Equal(frozen.EndUtc, reservation.EndUtc);
        Assert.Null(reservation.ResourceId);
        Assert.True(Assert.Single(candidate.Assignments,
            x => x.OperationId == includedOrder.Operations.First().OperationId).StartUtc >= frozen.EndUtc);
    }

    private static CreateSchedulePlanCommandHandler CreatePlanHandler(ApplicationDbContext db) => new(
        db, new FiniteCapacityScheduler(), TimeProvider.System,
        new NoopSchedulingEquipmentAvailabilityProvider(), new NoopSchedulingMaterialReadinessProvider(),
        new SchedulingOperationOverrideOverlay(db), new OrderUrgencyService(db, TimeProvider.System),
        SchedulingEquipmentUnknownModeOption.Default);

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-validation-context-{Guid.NewGuid():N}").Options,
        new NoopMediator());

    private sealed class CreatePlanSender(CreateSchedulePlanCommandHandler handler) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            (TResponse)(object)await handler.Handle(Assert.IsType<CreateSchedulePlanCommand>(request), cancellationToken);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

}
