using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class RushWorkOrderRoutingTests
{
    // Regression: #4274. 单工序捷径、丢失标准工序或把覆盖应用到整条路由都会破坏此断言。
    [Fact]
    public async Task Rush_order_freezes_complete_route_and_overrides_only_selected_operation()
    {
        await using var provider = CreateProvider(MesRoutingSnapshotResult.Captured("engineering:PV-R", [
            new(10, "CUT", "WC-CUT", [], 20, false),
            new(20, "POLISH", "WC-POLISH", ["WC-POLISH-ALT"], 30, true, "POLISHING"),
            new(30, "PACK", "WC-PACK", [], 15, false)]));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var command = Command(20);
        var handler = ActivatorUtilities.CreateInstance<CreateRushWorkOrderCommandHandler>(scope.ServiceProvider);
        await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var operations = await db.OperationTasks.OrderBy(x => x.OperationSequence).ToListAsync();
        Assert.Equal(new[] { 10, 20, 30 }, operations.Select(x => x.OperationSequence));
        Assert.Equal(new[] { "CUT", "POLISH", "PACK" }, operations.Select(x => x.OperationCode));
        Assert.Equal(new[] { "WC-CUT", "WC-OVERRIDE", "WC-PACK" }, operations.Select(x => x.WorkCenterId));
        Assert.Equal(new[] { 20d, 60d, 15d }, operations.Select(x => x.Duration.TotalMinutes));
        Assert.Equal("WO-R-OP-10", operations[0].OperationTaskIdValue);
        Assert.Equal("OP-R-20", operations[1].OperationTaskIdValue);
        Assert.Equal("WO-R-OP-30", operations[2].OperationTaskIdValue);
        Assert.True(operations[1].RequiresQualityInspection);
        Assert.Equal("POLISHING", operations[1].RequiredSkillCode);
        Assert.Contains("WC-POLISH-ALT", operations[1].AlternativeWorkCenterIdList);
        Assert.All(operations, x => Assert.Equal("SF-ROD-01", x.SkuCode));
        Assert.All(operations, x => Assert.Equal(5m, x.PlannedQuantity));
        Assert.True((await db.WorkOrders.SingleAsync()).IsRush);
    }

    // Regression: #4274. 缺路由不能退回手工单工序，也不能落下一张不可排产工单。
    [Fact]
    public async Task Missing_route_rejects_rush_order_before_any_business_state_is_added()
    {
        await using var provider = CreateProvider(MesRoutingSnapshotResult.Missing("engineering:PV-R"));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = ActivatorUtilities.CreateInstance<CreateRushWorkOrderCommandHandler>(scope.ServiceProvider);
        await Assert.ThrowsAsync<MesRoutingSnapshotMissingException>(() => handler.Handle(Command(10), CancellationToken.None));
        Assert.Empty(db.WorkOrders.Local);
        Assert.Empty(db.OperationTasks.Local);
    }

    private static CreateRushWorkOrderCommand Command(int sequence) => new(
        "org-001", "env-dev", "WO-R", "SF-ROD-01", "PV-R", 5m,
        DateTimeOffset.Parse("2026-10-12T08:00:00Z"), "WC-OVERRIDE", "OP-R-20", sequence,
        TimeSpan.FromMinutes(60), DateTimeOffset.Parse("2026-10-10T08:00:00Z"));

    private static ServiceProvider CreateProvider(MesRoutingSnapshotResult route)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMediator, NoopMediator>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase($"rush-route-{Guid.NewGuid():N}"));
        services.AddScoped<IMesPlanningStore, PersistentMesPlanningStore>();
        services.AddSingleton<MesCodingService>();
        services.AddScoped<IMesSkuAvailabilityScopeCoordinator, PostgreSqlMesSkuAvailabilityScopeCoordinator>();
        services.AddSingleton<IMesMaterialRequirementSnapshotProvider>(NoRequirementSnapshotProvider.Instance);
        services.AddSingleton<IMesRoutingSnapshotProvider>(new RoutingProvider(route));
        return services.BuildServiceProvider();
    }

    private sealed class RoutingProvider(MesRoutingSnapshotResult result) : IMesRoutingSnapshotProvider
    {
        public Task<MesRoutingSnapshotResult> GetSnapshotAsync(MesRoutingSnapshotRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
