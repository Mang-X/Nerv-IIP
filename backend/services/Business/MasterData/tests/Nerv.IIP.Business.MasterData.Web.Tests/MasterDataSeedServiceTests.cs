using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

/// <summary>
/// 产品基线主数据 seed：显示名必须为中文；重复执行幂等，已存在的租户事实一律不覆写、不停用（#3811）。
/// </summary>
public sealed class MasterDataSeedServiceTests
{
    [Fact]
    public async Task Seed_uses_chinese_display_names()
    {
        await using var db = CreateDbContext();

        await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");

        // 显示名的权威是产品文档 docs/product/master-data/design.md §5.3：
        // DAY=白班(08:00-20:00)、NIGHT=夜班(20:00-08:00,跨天)。这两条断言钉的是「种子与该约定一致」，
        // 不是「DAY 这个码天生叫什么」——#3473 之前它们被写成「早班」「晚班」，其中「早班」还与
        // 设定集种子的 EARLY(08–16) 撞名，PDA 班次选择器里出现两条「早班」，操作工分不清选哪个。
        Assert.Equal("白班", (await db.Shifts.SingleAsync(x => x.Code == "DAY")).Name);
        Assert.Equal("夜班", (await db.Shifts.SingleAsync(x => x.Code == "NIGHT")).Name);
        Assert.Equal("标准工作日历", (await db.WorkCalendars.SingleAsync(x => x.Code == "STANDARD")).Name);
        Assert.Equal("千克", (await db.UnitsOfMeasure.SingleAsync(x => x.Code == "kg")).Name);
        Assert.Equal(
            "原料库",
            (await db.ReferenceDataCodes.SingleAsync(x => x.CodeSet == "inventory-location" && x.Code == "loc-raw-01")).Name);
        Assert.Equal("生产工单", (await db.CodeRules.SingleAsync(x => x.RuleKey == "work-order")).DisplayName);
        Assert.Equal("工艺路线", (await db.CodeRules.SingleAsync(x => x.RuleKey == "routing")).DisplayName);

        Assert.DoesNotContain(
            await db.CodeRules.Select(x => x.DisplayName).ToArrayAsync(),
            name => name.Any(ch => ch is >= 'a' and <= 'z'));
    }

    [Fact]
    public async Task Seed_is_idempotent_and_keeps_tenant_facts()
    {
        await using var db = CreateDbContext();
        db.Departments.Add(Department.Create("org-001", "env-dev", "DEPT-PROD", "制造中心", null));
        await db.SaveChangesAsync();

        var seed = new MasterDataSeedService(db);
        await seed.SeedAsync("org-001", "env-dev");
        await seed.SeedAsync("org-001", "env-dev");

        Assert.Equal("制造中心", (await db.Departments.SingleAsync(x => x.Code == "DEPT-PROD")).Name);
        Assert.Equal(5, await db.Departments.CountAsync());
        Assert.Equal(
            MasterDataDictionaryRules.StandardReferenceData.Count(x => x.CodeSet == "inventory-location"),
            await db.ReferenceDataCodes.CountAsync(x =>
                x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                x.CodeSet == "inventory-location"));
    }

    /// <summary>
    /// #3811 验收：租户改过的编码规则、字典、计量单位在服务重启（重复 seed）后保持不变。
    /// 以前 seed 对已有编码规则无条件回写种子定义、对字典与计量单位改回种子名称，并停用一批旧码；
    /// 默认开启后每次重启都会跑，所以必须只补缺。
    /// </summary>
    [Fact]
    public async Task Seed_never_rewrites_or_disables_rows_the_tenant_already_has()
    {
        await using var db = CreateDbContext();
        await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");

        var workOrderRule = await db.CodeRules.SingleAsync(x => x.RuleKey == "work-order");
        var tenantSegments = """[{"type":"literal","value":"WO-T"}]""";
        workOrderRule.ReplaceDefinition("租户工单号", workOrderRule.AppliesTo, workOrderRule.Scope, tenantSegments, false, workOrderRule.Version + 1);
        var dry = await db.ReferenceDataCodes.SingleAsync(x => x.CodeSet == "storage-condition" && x.Code == "dry");
        dry.Update("防潮库");
        var raw = await db.ReferenceDataCodes.SingleAsync(x => x.CodeSet == "material-type" && x.Code == "raw-material");
        raw.Disable("租户停用");
        var kg = await db.UnitsOfMeasure.SingleAsync(x => x.Code == "kg");
        kg.Update("公斤", "mass", 2, "half-even");
        db.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "uom-dimension", "mass", "质量"));
        await db.SaveChangesAsync();
        var versionCount = await db.CodeRuleVersions.CountAsync();

        await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");

        workOrderRule = await db.CodeRules.SingleAsync(x => x.RuleKey == "work-order");
        Assert.Equal("租户工单号", workOrderRule.DisplayName);
        Assert.Equal(tenantSegments, workOrderRule.SegmentsJson);
        Assert.False(workOrderRule.IsActive);
        Assert.Equal(versionCount, await db.CodeRuleVersions.CountAsync());
        Assert.Equal("防潮库", (await db.ReferenceDataCodes.SingleAsync(x => x.CodeSet == "storage-condition" && x.Code == "dry")).Name);
        Assert.True((await db.ReferenceDataCodes.SingleAsync(x => x.CodeSet == "material-type" && x.Code == "raw-material")).Disabled);
        Assert.False((await db.ReferenceDataCodes.SingleAsync(x => x.CodeSet == "uom-dimension" && x.Code == "mass")).Disabled);
        kg = await db.UnitsOfMeasure.SingleAsync(x => x.Code == "kg");
        Assert.Equal(("公斤", "mass", 2, "half-even"), (kg.Name, kg.DimensionType, kg.Precision, kg.RoundingMode));
    }

    /// <summary>
    /// #3473 的真不变量：**同一套栈上两个种子并排跑完之后，班次显示名两两可区分**。
    ///
    /// 光钉「DAY 叫什么」钉不住这件事——撞名是跨种子的（常规种子的 DAY 08:00–20:00／720 分
    /// 与设定集种子的 EARLY 08:00–16:00／480 分是两个真不同的班次，只是名字起重了），
    /// 任一种子单独看都自洽。操作工在班次选择器里只看得到显示名，重名即不可选。
    /// </summary>
    [Fact]
    public async Task Shift_display_names_stay_distinguishable_across_both_seeds()
    {
        await using var db = CreateDbContext();

        await new MasterDataSeedService(db).SeedAsync("org-001", "env-dev");
        await new WalkthroughSeedService(db).SeedAsync("org-001", "env-dev");

        var shifts = await db.Shifts
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev")
            .Select(x => new { x.Code, x.Name })
            .ToArrayAsync();

        Assert.Equal(
            ["DAY", "EARLY", "MIDDLE", "NIGHT"],
            shifts.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal));
        var duplicated = shifts
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}={string.Join('+', group.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal))}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([], duplicated);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"master-data-seed-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new SeedTestMediator());
    }

    private sealed class SeedTestMediator : IMediator
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
