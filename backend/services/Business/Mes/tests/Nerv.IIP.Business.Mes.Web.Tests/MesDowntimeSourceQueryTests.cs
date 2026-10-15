using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>#4442 DomainInvariant：公开读面保留事件已保存的来源，不从其它事件补猜。EF InMemory fixture。</summary>
public sealed class MesDowntimeSourceQueryTests
{
    [Theory]
    [InlineData("WO-SOURCE", "OP-SOURCE")]
    [InlineData("WO-SOURCE", null)]
    [InlineData(null, null)]
    public async Task List_keeps_each_recorded_source_and_nullable_values_after_recovery(
        string? workOrderId, string? operationTaskId)
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var startedAt = DateTimeOffset.Parse("2026-10-15T08:00:00Z");
        var recorder = new RecordDowntimeEventCommandHandler(dbContext);
        var target = await recorder.Handle(new RecordDowntimeEventCommand(
            "org-001", "env-dev", workOrderId, operationTaskId,
            "WC-SOURCE", "DEVICE-SOURCE", "DT-MECH", startedAt, null), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var other = await recorder.Handle(new RecordDowntimeEventCommand(
            "org-001", "env-dev", "WO-OTHER", "OP-OTHER",
            "WC-SOURCE", "DEVICE-OTHER", "DT-MECH", startedAt.AddMinutes(1), null), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var query = new ListDowntimeEventsQuery("org-001", "env-dev", "WC-SOURCE", null);
        var reader = new ListDowntimeEventsQueryHandler(dbContext, new FakeTimeProvider(startedAt.AddHours(2)));
        var before = await reader.Handle(query, CancellationToken.None);
        Assert.Equal(2, before.Total);
        var targetRow = Assert.Single(before.Items, x => x.DowntimeEventId == target.ReferenceId);
        Assert.Equal(workOrderId, targetRow.WorkOrderId);
        Assert.Equal(operationTaskId, targetRow.OperationTaskId);
        Assert.Equal("WC-SOURCE", targetRow.WorkCenterId);
        Assert.Equal("DEVICE-SOURCE", targetRow.DeviceAssetId);
        Assert.Equal("Open", targetRow.Status);
        var otherRow = Assert.Single(before.Items, x => x.DowntimeEventId == other.ReferenceId);
        Assert.Equal("WO-OTHER", otherRow.WorkOrderId);
        Assert.Equal("OP-OTHER", otherRow.OperationTaskId);
        Assert.Equal("DEVICE-OTHER", otherRow.DeviceAssetId);

        var recoveredAt = startedAt.AddHours(1);
        await new ConfirmDowntimeRecoveryCommandHandler(dbContext).Handle(
            new ConfirmDowntimeRecoveryCommand("org-001", "env-dev", target.ReferenceId, recoveredAt),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var after = await reader.Handle(query, CancellationToken.None);
        var recovered = Assert.Single(after.Items, x => x.DowntimeEventId == target.ReferenceId);
        Assert.Equal(targetRow with { Status = "Recovered", RecoveredAtUtc = recoveredAt }, recovered);
        Assert.Equal(otherRow, Assert.Single(after.Items, x => x.DowntimeEventId == other.ReferenceId));
    }
}
