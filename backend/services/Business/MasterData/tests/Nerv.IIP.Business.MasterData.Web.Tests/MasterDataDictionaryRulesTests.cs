using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ReferenceDataAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Seed;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataDictionaryRulesTests
{
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
    public async Task MasterData_seed_disables_obsolete_system_dictionary_codes_without_deleting_them()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ReferenceDataCodes.Add(ReferenceDataCode.Create("org-001", "env-dev", "product-category", "finished-good", "Finished Good"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await new MasterDataSeedService(dbContext).SeedAsync("org-001", "env-dev", CancellationToken.None);

        var obsolete = await dbContext.ReferenceDataCodes.SingleAsync(x =>
            x.OrganizationId == "org-001" &&
            x.EnvironmentId == "env-dev" &&
            x.CodeSet == "product-category" &&
            x.Code == "finished-good",
            CancellationToken.None);
        Assert.True(obsolete.Disabled);
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
        Assert.Contains("batch-tracking-policy:legacy-lot", invalidBatch.Message, StringComparison.Ordinal);

        var invalidSerial = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            ValidCreateSkuCommand(SerialTrackingPolicy: "serialized"),
            CancellationToken.None));
        Assert.Contains("serial-tracking-policy:serialized", invalidSerial.Message, StringComparison.Ordinal);

        var invalidComplianceTag = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            ValidCreateSkuCommand(ComplianceTags: ["custom-cert"]),
            CancellationToken.None));
        Assert.Contains("compliance-tag:custom-cert", invalidComplianceTag.Message, StringComparison.Ordinal);
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

        Assert.Contains("material-type:legacy-material", invalidMaterialType.Message, StringComparison.Ordinal);
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
        Assert.Contains("system-managed reference data", update.Message, StringComparison.Ordinal);

        var disabled = await enableHandler.Handle(
            new SetMasterDataResourceEnabledCommand(
                "org-001",
                "env-dev",
                "reference-data",
                "raw-material",
                false,
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
        Assert.Contains("system enum reference data code set", invalid.Message, StringComparison.Ordinal);

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

        var unknownCodeSet = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new CreateReferenceDataCodeCommand(
                "org-001",
                "env-dev",
                "material-form",
                "powder",
                "Powder"),
            CancellationToken.None));
        Assert.Contains("not reserved", unknownCodeSet.Message, StringComparison.Ordinal);
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
            ["barcode-rule"] = ["code128", "customer-spec", "ean13", "gs1-128", "qr"],
            ["uom-dimension"] = ["area", "count", "length", "time", "volume", "weight"],
            ["partner-type"] = ["carrier", "customer", "supplier"],
            ["skill-level"] = ["expert", "intermediate", "junior", "senior"],
            ["quality-reason"] = ["dimension-ng", "missing-part", "scratch", "solder-defect"],
            ["compliance-tag"] = ["msd", "reach", "rohs", "ul"],
            ["device-status"] = ["fault", "idle", "maintenance", "running", "scrapped"],
            ["line-type"] = ["cell", "discrete", "flow"],
            ["work-center-type"] = ["section", "station-group", "work-center"]
        };
}
