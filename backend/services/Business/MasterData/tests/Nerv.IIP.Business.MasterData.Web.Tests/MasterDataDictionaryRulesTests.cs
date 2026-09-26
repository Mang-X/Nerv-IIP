using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;
using Nerv.IIP.Contracts.MasterData;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataDictionaryRulesTests
{
    [Fact]
    public void Priority_is_a_reserved_factory_custom_code_set_without_fabricated_seed_values()
    {
        Assert.True(MasterDataDictionaryRules.IsStandardCodeSet("priority"));
        Assert.False(MasterDataDictionaryRules.IsSystemEnumCodeSet("priority"));
        Assert.DoesNotContain(
            MasterDataDictionaryRules.StandardReferenceData,
            item => string.Equals(item.CodeSet, "priority", StringComparison.Ordinal));
    }

    /// <summary>
    /// `Sku.Create` 自己写死的两个受控码值必须落在各自码集里。
    /// oracle 取 <see cref="ExpectedDictionaryCodes"/>——它照 `docs/reference/master-data/dictionary.md`
    /// 抄写，并由 <see cref="MasterData_seed_creates_authoritative_dictionary_codes"/> 钉住 seed 与它一致，
    /// 因此独立于被测的 <c>Sku.Create</c>。不取 <c>StandardReferenceData</c>：那是 seed producer 自身，
    /// 与工厂方法同在实现一侧，两边一起改坏时这条断言会静默变绿。
    ///
    /// 护栏实际宽度（已实测，非票面转抄）：这里的 <c>Assert.Contains</c> 只钉「落在集合里」，不钉「等于
    /// 哪个值」。只改 <c>Sku.Create</c> 一个字段就够——例如只把 batchTrackingPolicy 从 "none" 单独换成
    /// 同码集内的 "mandatory"（serialTrackingPolicy 原样不动），不动 <see cref="ExpectedDictionaryCodes"/>
    /// 也不动 `StandardReferenceData`，本类全部 12 条用例照样全绿；不需要两个字段一起换，更不需要三处联动。
    /// 「取哪个值才对」这件事，<see cref="ExpectedDictionaryCodes"/> 和
    /// `docs/reference/master-data/dictionary.md` 都钉不住：文档把 "none"/"optional"/"mandatory"
    /// 平级列为标准码值，没有标注哪个是默认值；文档自身也明确自述不是运行时权威（见 `dictionary.md:3`）。
    /// </summary>
    [Fact]
    public void Sku_create_defaults_stay_inside_their_own_dictionary_code_sets()
    {
        var sku = Sku.Create("org-001", "env-dev", "SKU-DEFAULTS", "Default SKU", "pcs", "electronic");

        Assert.Contains(sku.BatchTrackingPolicy, ExpectedDictionaryCodes["batch-tracking-policy"], StringComparer.Ordinal);
        Assert.Contains(sku.SerialTrackingPolicy, ExpectedDictionaryCodes["serial-tracking-policy"], StringComparer.Ordinal);
    }

    /// <summary>
    /// #3747：<c>serial-tracking-policy</c> 码集在契约层的共享副本必须与 <see cref="ExpectedDictionaryCodes"/>
    /// 里的同名码集逐值相等。
    ///
    /// 这条盯的是 #3725 的**反向**失效方向：字典新增一个策略码时，引
    /// <see cref="MasterDataSerialTrackingPolicies"/> 做判定的消费方（MES 领域校验、业务网关报工的
    /// 400 判定）若不同步，会静默拒绝该码值——全链路只表现为报工 400，没有任何门禁会红。
    ///
    /// oracle 取 <see cref="ExpectedDictionaryCodes"/> 而**不**取 <c>StandardReferenceData</c>：
    /// 字典种子现在已经引用这份常量，两边取自同一侧时这条断言恒真。
    /// 射程边界同 <see cref="MasterData_seed_creates_authoritative_dictionary_codes"/> 的说明——
    /// oracle 是测试自己手抄的，它与 `docs/reference/master-data/dictionary.md` 抄错同一个值时本断言照样绿；
    /// 本断言承担的是「契约副本与 oracle 不分叉」，不是「oracle 抄对了文档」。
    /// </summary>
    [Fact]
    public void Serial_tracking_policy_contract_vocabulary_equals_dictionary_code_set()
    {
        Assert.Equal(
            ExpectedDictionaryCodes["serial-tracking-policy"].Order(StringComparer.Ordinal),
            MasterDataSerialTrackingPolicies.CanonicalValues.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// 钉住 seed 落库的每个码集与 <see cref="ExpectedDictionaryCodes"/> 里同名码集的**集合相等**
    /// （逐码集排序后 <c>Assert.Equal</c>）。这只保证 seed producer（`MasterDataSeedService` /
    /// `StandardReferenceData`）与测试自己手抄的 oracle 不互相漂移，不保证 oracle 本身抄对了
    /// `docs/reference/master-data/dictionary.md`——两边一起改错同一个值，这条断言照样绿。
    /// </summary>
    [Fact]
    public async Task MasterData_seed_creates_authoritative_dictionary_codes()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seed = new MasterDataSeedService(dbContext);

        await seed.SeedAsync("org-001", "env-dev", CancellationToken.None);

        foreach (var (codeSet, expectedCodes) in ExpectedDictionaryCodes)
        {
            var actualCodes = await dbContext.ReferenceDataCodes
                .Where(x =>
                    x.OrganizationId == "org-001" &&
                    x.EnvironmentId == "env-dev" &&
                    x.CodeSet == codeSet &&
                    !x.Disabled)
                .Select(x => x.Code)
                .OrderBy(x => x)
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(expectedCodes.Order(StringComparer.Ordinal), actualCodes);
        }
    }

    [Fact]
    public async Task MasterData_seed_creates_chinese_uom_names_with_authoritative_dimensions()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await new MasterDataSeedService(dbContext).SeedAsync("org-001", "env-dev", CancellationToken.None);

        var units = await dbContext.UnitsOfMeasure
            .Where(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev")
            .Select(x => new { x.Code, x.Name, x.DimensionType })
            .OrderBy(x => x.Code)
            .ToDictionaryAsync(x => x.Code, x => (x.Name, x.DimensionType), CancellationToken.None);

        Assert.Equal(("克", "weight"), units["g"]);
        Assert.Equal(("千克", "weight"), units["kg"]);
        Assert.Equal(("升", "volume"), units["l"]);
        Assert.Equal(("分钟", "time"), units["min"]);
        Assert.Equal(("件", "count"), units["pcs"]);
    }

    [Fact]
    public async Task MasterData_seed_disables_obsolete_system_dictionary_codes_without_deleting_them()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "product-category", "finished-good", "Finished Good"));
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "batch-tracking-policy", "lot", "Lot Tracking"));
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "serial-tracking-policy", "serial", "Serial Tracking"));
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "shelf-life-policy", "180d", "180 Days"));
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "shelf-life-policy", "365d", "365 Days"));
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "uom-dimension", "mass", "Mass"));
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "uom-dimension", "quantity", "Quantity"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new MasterDataSeedService(dbContext).SeedAsync("org-001", "env-dev", CancellationToken.None);

        foreach (var (codeSet, code) in new[]
        {
            ("product-category", "finished-good"),
            ("batch-tracking-policy", "lot"),
            ("serial-tracking-policy", "serial"),
            ("shelf-life-policy", "180d"),
            ("shelf-life-policy", "365d"),
            ("uom-dimension", "mass"),
            ("uom-dimension", "quantity")
        })
        {
            var obsolete = await dbContext.ReferenceDataCodes.SingleAsync(x =>
                x.OrganizationId == "org-001" &&
                x.EnvironmentId == "env-dev" &&
                x.CodeSet == codeSet &&
                x.Code == code,
                CancellationToken.None);
            Assert.True(obsolete.Disabled);
        }
    }

    [Fact]
    public async Task MasterData_seed_repairs_existing_authoritative_names_and_uom_dimensions()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "storage-condition", "dry", "Dry"));
        dbContext.UnitsOfMeasure.Add(Domain.AggregatesModel.UnitOfMeasureAggregate.UnitOfMeasure.Create("org-001", "env-dev", "kg", "Kilogram", "mass", 3, "half-up"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new MasterDataSeedService(dbContext).SeedAsync("org-001", "env-dev", CancellationToken.None);

        var dry = await dbContext.ReferenceDataCodes.SingleAsync(x =>
            x.OrganizationId == "org-001" &&
            x.EnvironmentId == "env-dev" &&
            x.CodeSet == "storage-condition" &&
            x.Code == "dry",
            CancellationToken.None);
        Assert.Equal("干燥防潮", dry.Name);

        var kg = await dbContext.UnitsOfMeasure.SingleAsync(x =>
            x.OrganizationId == "org-001" &&
            x.EnvironmentId == "env-dev" &&
            x.Code == "kg",
            CancellationToken.None);
        Assert.Equal("千克", kg.Name);
        Assert.Equal("weight", kg.DimensionType);
    }

    [Fact]
    public async Task Create_sku_command_validates_all_controlled_dictionary_fields()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedDictionaryAsync(dbContext);
        var handler = new CreateSkuCommandHandler(
            new SkuRepository(dbContext),
            new ReferenceDataCodeRepository(dbContext));

        var invalidBatch = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            ValidCreateSkuCommand(BatchTrackingPolicy: "legacy-lot"),
            CancellationToken.None));
        Assert.Equal("SKU 字段 'BatchTrackingPolicy' 的值不存在或未启用。", invalidBatch.Message);
        Assert.True(invalidBatch.Message.Length <= 60);

        var invalidSerial = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            ValidCreateSkuCommand(SerialTrackingPolicy: "serialized"),
            CancellationToken.None));
        Assert.Equal("SKU 字段 'SerialTrackingPolicy' 的值不存在或未启用。", invalidSerial.Message);

        var invalidComplianceTag = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            ValidCreateSkuCommand(ComplianceTags: ["custom-cert"]),
            CancellationToken.None));
        Assert.Equal("SKU 字段 'ComplianceTags' 的值不存在或未启用。", invalidComplianceTag.Message);
    }

    [Fact]
    public async Task Create_sku_command_hides_maximum_length_controlled_dictionary_value_from_rejection()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedDictionaryAsync(dbContext);
        var handler = new CreateSkuCommandHandler(
            new SkuRepository(dbContext),
            new ReferenceDataCodeRepository(dbContext));
        var maximumLengthCode = new string('x', 100);

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            ValidCreateSkuCommand(BatchTrackingPolicy: maximumLengthCode),
            CancellationToken.None));

        Assert.Equal("SKU 字段 'BatchTrackingPolicy' 的值不存在或未启用。", exception.Message);
        Assert.True(exception.Message.Length <= 60);
        Assert.DoesNotContain(maximumLengthCode, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seeded_dictionary_accepts_issue_355_create_sku_payload()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new MasterDataSeedService(dbContext).SeedAsync("org-001", "env-dev", CancellationToken.None);
        var handler = new CreateSkuCommandHandler(
            new SkuRepository(dbContext),
            new ReferenceDataCodeRepository(dbContext));

        var result = await handler.Handle(
            new CreateSkuCommand(
                "org-001",
                "env-dev",
                "SKU-DIAG-001",
                "Diagnostic SKU",
                "PCS",
                "electronic",
                "finished-goods",
                "none",
                "none",
                "none",
                "ambient",
                "code128",
                true,
                [],
                "diag-001"),
            CancellationToken.None);

        Assert.Equal("sku", result.ResourceType);
        Assert.Equal("SKU-DIAG-001", result.Code);
    }

    [Fact]
    public async Task Update_sku_command_validates_controlled_dictionary_fields()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedDictionaryAsync(dbContext);
        dbContext.Skus.Add(Sku.CreateIndustrial(
            "org-001",
            "env-dev",
            "SKU-001",
            "Electronic Assembly",
            "kg",
            "electronic",
            "finished-goods",
            "none",
            "none",
            "none",
            "ambient",
            "code128",
            true,
            ["rohs"]));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));

        var invalidMaterialType = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "sku",
                "SKU-001",
                MaterialType: "legacy-material"),
            CancellationToken.None));

        Assert.Equal("SKU 字段 'MaterialType' 的值不存在或未启用。", invalidMaterialType.Message);
    }

    [Fact]
    public async Task System_dictionary_codes_cannot_be_updated_but_can_be_disabled()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new MasterDataSeedService(dbContext).SeedAsync("org-001", "env-dev", CancellationToken.None);

        var updateHandler = new UpdateMasterDataResourceCommandHandler(dbContext, new ReferenceDataCodeRepository(dbContext));
        var enableHandler = new SetMasterDataResourceEnabledCommandHandler(dbContext);

        var update = await Assert.ThrowsAsync<KnownException>(() => updateHandler.Handle(
            new UpdateMasterDataResourceCommand(
                "org-001",
                "env-dev",
                "reference-data",
                "raw-material",
                "material-type",
                Name: "Renamed"),
            CancellationToken.None));
        Assert.Contains("系统管理的参考数据", update.Message, StringComparison.Ordinal);

        var disabled = await enableHandler.Handle(
            new SetMasterDataResourceEnabledCommand(
                "org-001",
                "env-dev",
                "reference-data",
                "raw-material",
                false,
                "test:actor",
                "op-reference-disable",
                "material-type",
                "retired"),
            CancellationToken.None);

        Assert.False(disabled.Active);
    }

    [Fact]
    public async Task Dictionary_code_sets_enforce_reserved_system_enum_governance()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new CreateReferenceDataCodeCommandHandler(new ReferenceDataCodeRepository(dbContext));

        var invalid = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateReferenceDataCodeCommand(
                "org-001",
                "env-dev",
                "material-type",
                "custom-material",
                "Custom Material"),
            CancellationToken.None));
        Assert.Contains("系统枚举代码集", invalid.Message, StringComparison.Ordinal);

        var productCategory = await handler.Handle(
            new CreateReferenceDataCodeCommand(
                "org-001",
                "env-dev",
                "product-category",
                "custom-category",
                "Custom Category"),
            CancellationToken.None);
        Assert.Equal("custom-category", productCategory.Code);

        var qualityReason = await handler.Handle(
            new CreateReferenceDataCodeCommand(
                "org-001",
                "env-dev",
                "quality-reason",
                "customer-return",
                "Customer Return"),
            CancellationToken.None);
        Assert.Equal("customer-return", qualityReason.Code);

        var skill = await handler.Handle(
            new CreateReferenceDataCodeCommand(
                "org-001",
                "env-dev",
                "skill",
                "packaging",
                "包装"),
            CancellationToken.None);
        Assert.Equal("packaging", skill.Code);

        var priority = await handler.Handle(
            new CreateReferenceDataCodeCommand(
                "org-001",
                "env-dev",
                "priority",
                "urgent-customer",
                "客户加急"),
            CancellationToken.None);
        Assert.Equal("urgent-customer", priority.Code);

        var unknownCodeSet = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateReferenceDataCodeCommand(
                "org-001",
                "env-dev",
                "material-form",
                "powder",
                "Powder"),
            CancellationToken.None));
        Assert.Contains("未在主数据字典规则中登记", unknownCodeSet.Message, StringComparison.Ordinal);
    }

    private static CreateSkuCommand ValidCreateSkuCommand(
        string BatchTrackingPolicy = "none",
        string SerialTrackingPolicy = "none",
        IReadOnlyCollection<string>? ComplianceTags = null)
    {
        return new CreateSkuCommand(
            "org-001",
            "env-dev",
            "SKU-001",
            "Electronic Assembly",
            "kg",
            "electronic",
            "finished-goods",
            BatchTrackingPolicy,
            SerialTrackingPolicy,
            "none",
            "ambient",
            "code128",
            true,
            ComplianceTags ?? ["rohs"]);
    }

    private static async Task SeedDictionaryAsync(ApplicationDbContext dbContext)
    {
        foreach (var (codeSet, codes) in ExpectedDictionaryCodes)
        {
            foreach (var code in codes)
            {
                dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", codeSet, code, code));
            }
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"master-data-dictionary-rules-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 多数码集手抄自 `docs/reference/master-data/dictionary.md`，供本文件用作独立 oracle；两者之间
    /// 没有任何机器校验，只靠这条注释指过去。已知例外：`uom-dimension` 这里是 10 码，文档
    /// （`dictionary.md:44`）只列 6 码（count/length/area/volume/weight/time），另外 4 码
    /// （force/torque/pressure/ratio）照抄的是 producer 侧 `MasterDataDictionaryRules.cs:80-83`，
    /// 与文档存在真实漂移（登记为独立事项，不在本 PR 修）。
    /// `dictionary.md` 自身也不是运行时权威：其 `:3` 明确自述「不是独立运行时事实源」，最终以 seed /
    /// ReferenceData 独立目录 API / 领域校验器 / 前端消费代码为准；这份手抄表同样不是权威，只是复核入口。
    /// 本类断言拿它验的是「内部各处一致」（seed 与它集合相等、`Sku.Create` 的默认值落在其码集内），
    /// 不是「抄得对不对」；把这里的取值和文档一起抄错，全类照样绿。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedDictionaryCodes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["material-type"] =
            [
                "consumable",
                "finished-goods",
                "packaging",
                "raw-material",
                "semi-finished",
                "spare-part",
                "tooling"
            ],
            ["product-category"] =
            [
                "assembly",
                "chemical",
                "electronic",
                "hardware",
                "mechanical",
                "plastic"
            ],
            ["batch-tracking-policy"] = ["mandatory", "none", "optional"],
            ["serial-tracking-policy"] = ["none", "on-production", "on-receipt", "on-shipment"],
            ["shelf-life-policy"] = ["expiry-controlled", "fefo", "fifo", "none"],
            ["storage-condition"] = ["ambient", "dry", "esd", "frozen", "hazardous", "refrigerated"],
            ["inventory-location"] = ["loc-fg-01", "loc-line-01", "loc-raw-01", "loc-semi-01"],
            ["barcode-rule"] = ["code128", "customer-spec", "ean13", "gs1-128", "qr"],
            ["uom-dimension"] = ["area", "count", "force", "length", "pressure", "ratio", "time", "torque", "volume", "weight"],
            ["partner-type"] = ["carrier", "customer", "supplier"],
            ["skill"] = ["assembly", "cnc-operation", "equipment-maintenance", "forklift", "inspection", "welding"],
            ["skill-level"] = ["expert", "intermediate", "junior", "senior"],
            ["operation"] = ["assembly", "cnc-operation", "inspection", "packaging", "welding"],
            ["quality-reason"] = ["dimension-ng", "missing-part", "scratch", "solder-defect"],
            ["compliance-tag"] = ["msd", "reach", "rohs", "ul"],
            ["asset-class"] = ["general-equipment", "logistics-equipment", "power-equipment", "special-equipment", "testing-equipment"],
            ["device-status"] = ["fault", "idle", "maintenance", "running", "scrapped"],
            ["line-type"] = ["cell", "discrete", "flow"],
            ["work-center-type"] = ["section", "station-group", "work-center"]
        };
}
