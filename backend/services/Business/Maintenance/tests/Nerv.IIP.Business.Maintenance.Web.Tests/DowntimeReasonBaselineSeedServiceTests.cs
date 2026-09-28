using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.DowntimeReasonAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Seed;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

/// <summary>
/// #3855：新环境里停机原因目录为空时，登记设备占用、完工登记停机都走不通。
/// 产品基线预置一套标准原因，按码只补缺，不覆盖租户维护过的原因。
/// </summary>
public sealed class DowntimeReasonBaselineSeedServiceTests
{
    private static readonly string[] ExpectedCodes =
    [
        "DT-MECH", "DT-ELEC", "DT-TOOL", "DT-SETUP", "DT-MINOR", "DT-PROC",
        "DT-QUALITY", "DT-MATERIAL", "DT-LABOR", "DT-UTILITY", "DT-PM", "DT-NOPLAN",
    ];

    [Fact]
    public async Task Seeds_twelve_standard_reasons_into_an_empty_catalog()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();

        var written = await new DowntimeReasonBaselineSeedService(db).SeedAsync("org-001", "env-dev");

        Assert.Equal(12, written);
        var reasons = await db.DowntimeReasons
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev")
            .ToListAsync();
        Assert.Equal(ExpectedCodes.Order(StringComparer.Ordinal), reasons.Select(x => x.ReasonCode).Order(StringComparer.Ordinal));
        // 分类与损失类别都落在受控码表里（控制台按码显示中文），计划停机不计入 OEE 损失。
        Assert.All(reasons, x => Assert.Contains(x.ReasonCategory, new[] { "breakdown", "setup", "minor-stop", "process", "quality", "material", "labor", "external", "planned" }));
        Assert.All(reasons, x => Assert.Contains(x.LossCategory, new[] { "availability", "performance", "planned" }));
        Assert.Equal("planned", reasons.Single(x => x.ReasonCode == "DT-PM").LossCategory);
        Assert.Equal("performance", reasons.Single(x => x.ReasonCode == "DT-MINOR").LossCategory);
    }

    [Fact]
    public async Task Is_idempotent_and_keeps_tenant_maintained_reasons()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();
        db.DowntimeReasons.Add(DowntimeReason.Create("org-001", "env-dev", "DT-MECH", "机械故障（租户改名）", "breakdown", "availability"));
        await db.SaveChangesAsync();
        var seed = new DowntimeReasonBaselineSeedService(db);

        var first = await seed.SeedAsync("org-001", "env-dev");
        var second = await seed.SeedAsync("org-001", "env-dev");

        Assert.Equal(11, first);
        Assert.Equal(0, second);
        Assert.Equal(12, await db.DowntimeReasons.CountAsync(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev"));
        Assert.Equal("机械故障（租户改名）", (await db.DowntimeReasons.SingleAsync(x => x.ReasonCode == "DT-MECH")).Description);
    }

    [Fact]
    public async Task Seeds_only_the_requested_tenant()
    {
        await using var db = MaintenanceEndpointContractTests.CreateTestDbContext();

        await new DowntimeReasonBaselineSeedService(db).SeedAsync("org-001", "env-dev");

        Assert.False(await db.DowntimeReasons.AnyAsync(x => x.OrganizationId != "org-001" || x.EnvironmentId != "env-dev"));
    }

    [Fact]
    public void World_history_demo_codes_are_a_subset_of_the_baseline()
    {
        // 演示种子与产品基线都按码补缺；同名码必须同义，演示种子不能引入基线之外的另一套原因体系。
        var demoCodes = WorldHistoryDeviceSpec.DowntimeReasons.Select(x => x.Code);
        Assert.Empty(demoCodes.Except(DowntimeReasonBaselineSeedService.Reasons.Select(x => x.Code)));
    }
}
