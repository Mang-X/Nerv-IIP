using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingOperationOverrideOverlayTests
{
    [Fact]
    public async Task Apply_replaces_existing_lock_with_persisted_override()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-overrides-{Guid.NewGuid():N}").Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var order = problem.Orders.First();
        var operation = order.Operations.First();
        var resource = problem.Resources.First(x => operation.EligibleResourceIds.Contains(x.ResourceId));
        var start = problem.HorizonStartUtc.AddHours(1);
        dbContext.ScheduleOperationOverrides.Add(ScheduleOperationOverride.Create(
            problem.OrganizationId, problem.EnvironmentId, order.OrderId, operation.OperationId,
            operation.OperationSequence, resource.ResourceId, resource.WorkCenterId,
            start, start.AddMinutes(operation.DurationMinutes), "manual-override", "scheduling-api",
            null, "user:planner", start, start));
        await dbContext.SaveChangesAsync();

        var result = await new SchedulingOperationOverrideOverlay(dbContext).ApplyAsync(problem, CancellationToken.None);

        var locked = Assert.Single(result.LockedAssignments, x => x.OperationId == operation.OperationId);
        Assert.Equal(resource.ResourceId, locked.ResourceId);
        Assert.Equal(start, locked.StartUtc);
        Assert.Equal("manual-override", locked.LockReasonCode);
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
