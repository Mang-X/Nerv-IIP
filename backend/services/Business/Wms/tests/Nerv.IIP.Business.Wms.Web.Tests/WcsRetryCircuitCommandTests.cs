using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Commands;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WcsRetryCircuitCommandTests
{
    [Fact]
    public async Task Dispatch_rejects_a_retry_before_its_scheduled_time()
    {
        var now = new DateTimeOffset(2026, 7, 10, 0, 0, 30, TimeSpan.Zero);
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-RETRY-001");
        dbContext.Add(warehouseTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var wcsTask = WcsTask.Dispatch("org-001", "env-dev", warehouseTask.Id, "agv", "EXT-001", "{}", "AGV-01");
        wcsTask.Fail("E001", "blocked aisle", now.UtcDateTime.AddSeconds(-30));
        dbContext.Add(wcsTask);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new DispatchWcsTaskCommandHandler(dbContext, new WcsTestTimeProvider(now));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new DispatchWcsTaskCommand(warehouseTask.Id, "agv", "EXT-002", "{}", "AGV-01"),
            CancellationToken.None));

        Assert.Contains("not due", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dispatch_fails_fast_with_a_clear_reason_when_the_device_circuit_is_open()
    {
        var now = new DateTimeOffset(2026, 7, 10, 0, 3, 0, TimeSpan.Zero);
        await using var provider = WmsTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouseTask = CreateWarehouseTask("WT-CIRCUIT-001");
        var circuit = WcsDispatchCircuit.Create("org-001", "env-dev", "agv", "AGV-01");
        circuit.RecordFailure(now.UtcDateTime.AddMinutes(-2), 3);
        circuit.RecordFailure(now.UtcDateTime.AddMinutes(-1), 3);
        circuit.RecordFailure(now.UtcDateTime, 3);
        dbContext.AddRange(warehouseTask, circuit);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new DispatchWcsTaskCommandHandler(dbContext, new WcsTestTimeProvider(now));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new DispatchWcsTaskCommand(warehouseTask.Id, "agv", "EXT-CIRCUIT-001", "{}", "AGV-01"),
            CancellationToken.None));

        Assert.Contains("circuit is open", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WarehouseTask CreateWarehouseTask(string taskNo) =>
        WarehouseTask.CreatePutaway("org-001", "env-dev", taskNo, "IN-001", "10", "SKU-001", "pcs", "SITE-01", "RECV-01", "STAGE-01", 3m);

}

internal sealed class WcsTestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
