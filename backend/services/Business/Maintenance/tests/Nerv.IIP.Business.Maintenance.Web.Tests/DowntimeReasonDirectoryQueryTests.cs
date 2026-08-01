using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Infrastructure;
using Nerv.IIP.Business.Maintenance.Web.Application.Queries;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class DowntimeReasonDirectoryQueryTests
{
    [Fact]
    public async Task Keyword_and_scope_are_applied_before_total_and_paging()
    {
        await using var db = CreateDbContext();
        db.DowntimeReasons.AddRange(
            DowntimeReason.Create("org-a", "env-a", "ELEC-01", "电气故障", "breakdown", "availability"),
            DowntimeReason.Create("org-a", "env-a", "ELEC-02", "电柜过热", "breakdown", "availability"),
            DowntimeReason.Create("org-a", "env-a", "MECH-01", "机械故障", "breakdown", "availability"),
            DowntimeReason.Create("org-b", "env-a", "ELEC-OTHER", "电气故障", "breakdown", "availability"));
        await db.SaveChangesAsync();

        var response = await new ListDowntimeReasonsQueryHandler(db).Handle(
            new ListDowntimeReasonsQuery("org-a", "env-a", Keyword: "电", Skip: 1, Take: 1),
            CancellationToken.None);

        Assert.Equal(2, response.Total);
        var item = Assert.Single(response.Items);
        Assert.Equal("ELEC-02", item.ReasonCode);
    }

    [Fact]
    public async Task Keyword_matches_reason_code_category_and_loss_category()
    {
        await using var db = CreateDbContext();
        db.DowntimeReasons.AddRange(
            DowntimeReason.Create("org-a", "env-a", "PM-01", "计划保养", "planned", "planned-maintenance"),
            DowntimeReason.Create("org-a", "env-a", "WAIT-01", "等待物料", "waiting", "performance"));
        await db.SaveChangesAsync();

        var byCode = await Handle(db, "pm-01");
        var byCategory = await Handle(db, "planned");
        var byLoss = await Handle(db, "maintenance");

        Assert.Equal("PM-01", Assert.Single(byCode.Items).ReasonCode);
        Assert.Equal("PM-01", Assert.Single(byCategory.Items).ReasonCode);
        Assert.Equal("PM-01", Assert.Single(byLoss.Items).ReasonCode);
    }

    private static Task<PagedMaintenanceListResponse<DowntimeReasonListItem>> Handle(ApplicationDbContext db, string keyword) =>
        new ListDowntimeReasonsQueryHandler(db).Handle(
            new ListDowntimeReasonsQuery("org-a", "env-a", Keyword: keyword),
            CancellationToken.None);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"downtime-directory-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
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
