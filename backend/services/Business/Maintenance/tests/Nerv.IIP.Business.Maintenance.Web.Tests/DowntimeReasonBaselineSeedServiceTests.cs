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
        // 逐条钉住分类与损失类别（TPM 六大损失 / OEE 口径）：设备故障、换型、工艺、质量、缺料、缺人、公用工程
        // 中断计可用率损失；小停机 / 空转计性能损失；计划保养与无生产计划是计划停机，不计入损失。
        var expected = new Dictionary<string, (string Category, string Loss)>
        {
            ["DT-MECH"] = ("breakdown", "availability"),
            ["DT-ELEC"] = ("breakdown", "availability"),
            ["DT-TOOL"] = ("breakdown", "availability"),
            ["DT-SETUP"] = ("setup", "availability"),
            ["DT-MINOR"] = ("minor-stop", "performance"),
            ["DT-PROC"] = ("process", "availability"),
            ["DT-QUALITY"] = ("quality", "availability"),
            ["DT-MATERIAL"] = ("material", "availability"),
            ["DT-LABOR"] = ("labor", "availability"),
            ["DT-UTILITY"] = ("external", "availability"),
            ["DT-PM"] = ("planned", "planned"),
            ["DT-NOPLAN"] = ("planned", "planned"),
        };
        Assert.All(reasons, x => Assert.Equal(expected[x.ReasonCode], (x.ReasonCategory, x.LossCategory)));
        Assert.All(reasons, x =>
        {
            Assert.Contains(x.ReasonCategory, DowntimeReasonVocabulary.ReasonCategories.All);
            Assert.Contains(x.LossCategory, DowntimeReasonVocabulary.LossCategories.All);
        });
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
