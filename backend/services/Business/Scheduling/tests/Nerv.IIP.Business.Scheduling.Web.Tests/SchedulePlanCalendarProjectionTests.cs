using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

/// <summary>
/// 工作日历 / 不可用窗口进入计划读面这条链:排程输出带出来、落库后重新读出来仍带得出来。
/// 甘特的日历底纹与「阻塞」图例都只认这两组事实,所以它们不能在任一跳上悄悄丢掉。
/// </summary>
public sealed class SchedulePlanCalendarProjectionTests
{
    [Fact]
    public void Scheduler_output_carries_calendar_shifts_and_block_windows()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();

        var plan = new FiniteCapacityScheduler().Schedule(
            problem,
            "plan-calendar-001",
            new DateTimeOffset(2026, 6, 1, 7, 0, 0, TimeSpan.Zero));

        var calendar = Assert.Single(plan.Calendars!);
        Assert.Equal("CAL-DAY", calendar.CalendarId);
        Assert.Equal(2, calendar.ShiftWindows.Count);
        Assert.Contains("DEV-OIL-01", calendar.ResourceIds);
        Assert.Contains("WC-OIL-SEAL", calendar.WorkCenterIds);

        var block = Assert.Single(plan.BlockWindows!);
        Assert.Equal("DEV-OIL-01", block.ResourceId);
        Assert.Equal("WC-OIL-SEAL", block.WorkCenterId);
        Assert.Equal(ScheduleBlockKindContract.Maintenance, block.Kind);
        Assert.Equal("maintenance", block.ReasonCode);
    }

    [Fact]
    public void Projection_drops_windows_outside_the_horizon()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem() with
        {
            UnavailabilityWindows =
            [
                new SchedulingUnavailabilityWindowContract(
                    ResourceId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL-SEAL",
                    // 计划期是 6/1 08:00 – 6/2 16:00,这一条整体落在展望期之后。
                    StartUtc: new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero),
                    ReasonCode: "maintenance")
            ]
        };

        Assert.Empty(SchedulePlanCalendarProjector.ProjectBlockWindows(problem));
    }

    [Theory]
    [InlineData("maintenance.preventive", ScheduleBlockKindContract.Maintenance)]
    [InlineData("inspection", ScheduleBlockKindContract.Maintenance)]
    [InlineData("line-change", ScheduleBlockKindContract.LineChange)]
    [InlineData("changeover.setup", ScheduleBlockKindContract.Changeover)]
    [InlineData("downtime.planned", ScheduleBlockKindContract.Downtime)]
    [InlineData("", ScheduleBlockKindContract.Downtime)]
    [InlineData("something-nobody-mapped-yet", ScheduleBlockKindContract.Downtime)]
    public void Block_kind_classification_never_drops_an_unknown_reason_code(
        string reasonCode,
        ScheduleBlockKindContract expected)
    {
        Assert.Equal(expected, SchedulePlanCalendarProjector.ClassifyBlockKind(reasonCode));
    }

    [Fact]
    public async Task Plan_detail_read_face_projects_the_persisted_problem_snapshot()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        await using var dbContext = CreateDbContext();
        var generated = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().Schedule(
                problem,
                "plan-calendar-002",
                new DateTimeOffset(2026, 6, 1, 7, 0, 0, TimeSpan.Zero)),
            SchedulePlanStatusContract.Generated);
        dbContext.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            "org-001",
            "prod",
            SchedulePlanContractMapper.ToDomainSnapshot(generated)));
        dbContext.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId,
            problem.ContractVersion,
            "org-001",
            "prod",
            "fingerprint-001",
            JsonSerializer.Serialize(problem, SchedulingJson.Options),
            problem.HorizonStartUtc,
            problem.HorizonEndUtc,
            new DateTimeOffset(2026, 6, 1, 7, 0, 0, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();

        var detail = await new GetSchedulePlanDetailQueryHandler(dbContext).Handle(
            new GetSchedulePlanDetailQuery("plan-calendar-002", "org-001", "prod"),
            CancellationToken.None);

        Assert.Equal("CAL-DAY", Assert.Single(detail.Calendars!).CalendarId);
        Assert.Equal(ScheduleBlockKindContract.Maintenance, Assert.Single(detail.BlockWindows!).Kind);
    }

    [Fact]
    public async Task Plan_detail_read_face_stays_silent_when_the_problem_snapshot_is_missing()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        await using var dbContext = CreateDbContext();
        var generated = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().Schedule(
                problem,
                "plan-calendar-003",
                new DateTimeOffset(2026, 6, 1, 7, 0, 0, TimeSpan.Zero)),
            SchedulePlanStatusContract.Generated);
        dbContext.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            "org-001",
            "prod",
            SchedulePlanContractMapper.ToDomainSnapshot(generated)));
        await dbContext.SaveChangesAsync();

        var detail = await new GetSchedulePlanDetailQueryHandler(dbContext).Handle(
            new GetSchedulePlanDetailQuery("plan-calendar-003", "org-001", "prod"),
            CancellationToken.None);

        // 没有问题快照就不带日历——读面宁可少说,也不编一份日历出来。
        Assert.Null(detail.Calendars);
        Assert.Null(detail.BlockWindows);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-plan-calendar-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }
}
