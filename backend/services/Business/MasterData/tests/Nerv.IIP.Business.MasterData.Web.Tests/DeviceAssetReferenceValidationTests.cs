using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class DeviceAssetReferenceValidationTests
{
    [Fact]
    public async Task Create_WithSupplierCapablePartnerAndActiveParent_PersistsCanonicalParentPublicGuid()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.BusinessPartners.Add(BusinessPartner.Create(
            OrganizationId,
            EnvironmentId,
            "SUP-VALID",
            "customer",
            "Supplier-capable partner",
            ["customer", "supplier"],
            null));
        var parent = DeviceAsset.Register(
            OrganizationId,
            EnvironmentId,
            "DEV-PARENT",
            "Parent",
            "LINE-1",
            "WC-1");
        dbContext.DeviceAssets.Add(parent);
        await dbContext.SaveChangesAsync();

        var command = CreateCommand(
            "DEV-CHILD",
            supplierPartnerCode: "  SUP-VALID  ",
            parentDeviceId: $"  {{{parent.Id.ToString().ToUpperInvariant()}}}  ");
        await CreateHandler(dbContext).Handle(command, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var persisted = await dbContext.DeviceAssets.SingleAsync(x => x.Code == "DEV-CHILD");
        Assert.Equal("SUP-VALID", persisted.SupplierPartnerCode);
        Assert.Equal(parent.Id.ToString(), persisted.ParentDeviceId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("wrong-organization")]
    [InlineData("wrong-environment")]
    [InlineData("non-supplier")]
    public async Task Create_WithInvalidSupplierReference_ThrowsKnownException(string scenario)
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var referencedCode = $"SUP-{scenario}";
        if (scenario != "missing")
        {
            var partner = BusinessPartner.Create(
                scenario == "wrong-organization" ? "org-other" : OrganizationId,
                scenario == "wrong-environment" ? "env-other" : EnvironmentId,
                referencedCode,
                scenario == "non-supplier" ? "customer" : "supplier",
                scenario,
                scenario == "non-supplier" ? ["customer"] : ["supplier"],
                null);
            if (scenario == "disabled")
            {
                partner.Disable("test setup");
            }

            dbContext.BusinessPartners.Add(partner);
            await dbContext.SaveChangesAsync();
        }

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            CreateHandler(dbContext).Handle(
                CreateCommand($"DEV-{scenario}", supplierPartnerCode: referencedCode),
                CancellationToken.None));

        Assert.Contains("supplier", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("wrong-organization")]
    [InlineData("wrong-environment")]
    public async Task Create_WithInvalidParentReference_ThrowsKnownException(string scenario)
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parentId = Guid.CreateVersion7();
        if (scenario != "missing")
        {
            var parent = DeviceAsset.Register(
                scenario == "wrong-organization" ? "org-other" : OrganizationId,
                scenario == "wrong-environment" ? "env-other" : EnvironmentId,
                $"DEV-PARENT-{scenario}",
                "Parent",
                "LINE-1",
                "WC-1");
            if (scenario == "disabled")
            {
                parent.Disable("test setup");
            }

            dbContext.DeviceAssets.Add(parent);
            await dbContext.SaveChangesAsync();
            parentId = parent.Id.Id;
        }

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            CreateHandler(dbContext).Handle(
                CreateCommand($"DEV-{scenario}", parentDeviceId: parentId.ToString()),
                CancellationToken.None));

        Assert.Contains("parent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_WithMalformedParentPublicGuid_ThrowsKnownException()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.DeviceAssets.Add(DeviceAsset.Register(
            OrganizationId,
            EnvironmentId,
            "DEV-EXISTING",
            "Existing",
            "LINE-1",
            "WC-1"));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            CreateHandler(dbContext).Handle(
                CreateCommand("DEV-MALFORMED", parentDeviceId: "DEV-EXISTING"),
                CancellationToken.None));

        Assert.Contains("parent", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GUID", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_WithValidSupplierAndParentChanges_PersistsNormalizedReferences()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.BusinessPartners.Add(BusinessPartner.Create(
            OrganizationId,
            EnvironmentId,
            "SUP-UPDATE",
            "customer",
            "Supplier-capable partner",
            ["customer", "supplier"],
            null));
        var parent = NewDevice("DEV-UPDATE-PARENT");
        var device = NewDevice("DEV-UPDATE-CHILD");
        dbContext.DeviceAssets.AddRange(parent, device);
        await dbContext.SaveChangesAsync();

        await UpdateHandler(dbContext).Handle(
            UpdateCommand(
                device.Code,
                supplierPartnerCode: "  SUP-UPDATE  ",
                parentDeviceId: $" {{{parent.Id.ToString().ToUpperInvariant()}}} "),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal("SUP-UPDATE", device.SupplierPartnerCode);
        Assert.Equal(parent.Id.ToString(), device.ParentDeviceId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("wrong-organization")]
    [InlineData("wrong-environment")]
    [InlineData("non-supplier")]
    public async Task Update_WithInvalidSupplierChange_ThrowsBeforeMutation(string scenario)
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = NewDevice($"DEV-SUPPLIER-{scenario}");
        dbContext.DeviceAssets.Add(device);
        var referencedCode = $"SUP-UPDATE-{scenario}";
        if (scenario != "missing")
        {
            var partner = BusinessPartner.Create(
                scenario == "wrong-organization" ? "org-other" : OrganizationId,
                scenario == "wrong-environment" ? "env-other" : EnvironmentId,
                referencedCode,
                scenario == "non-supplier" ? "customer" : "supplier",
                scenario,
                scenario == "non-supplier" ? ["customer"] : ["supplier"],
                null);
            if (scenario == "disabled")
            {
                partner.Disable("test setup");
            }

            dbContext.BusinessPartners.Add(partner);
        }
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            UpdateHandler(dbContext).Handle(
                UpdateCommand(device.Code, model: "must-not-apply", supplierPartnerCode: referencedCode),
                CancellationToken.None));

        Assert.Contains("supplier", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Test device", device.Model);
        Assert.Empty(device.SupplierPartnerCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("wrong-organization")]
    [InlineData("wrong-environment")]
    [InlineData("malformed")]
    public async Task Update_WithInvalidParentChange_ThrowsBeforeMutation(string scenario)
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = NewDevice($"DEV-PARENT-{scenario}");
        dbContext.DeviceAssets.Add(device);
        var proposedParentId = Guid.CreateVersion7().ToString();
        if (scenario is not ("missing" or "malformed"))
        {
            var parent = DeviceAsset.Register(
                scenario == "wrong-organization" ? "org-other" : OrganizationId,
                scenario == "wrong-environment" ? "env-other" : EnvironmentId,
                $"TARGET-{scenario}",
                "Parent",
                "LINE-1",
                "WC-1");
            if (scenario == "disabled")
            {
                parent.Disable("test setup");
            }

            dbContext.DeviceAssets.Add(parent);
            await dbContext.SaveChangesAsync();
            proposedParentId = parent.Id.ToString();
        }
        else
        {
            await dbContext.SaveChangesAsync();
        }

        if (scenario == "malformed")
        {
            proposedParentId = "TARGET-BY-CODE";
        }

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            UpdateHandler(dbContext).Handle(
                UpdateCommand(device.Code, model: "must-not-apply", parentDeviceId: proposedParentId),
                CancellationToken.None));

        Assert.Contains("parent", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Test device", device.Model);
        Assert.Empty(device.ParentDeviceId);
    }

    [Fact]
    public async Task Update_WithEmptyReferenceValues_ClearsOptionalReferences()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var supplier = BusinessPartner.Create(
            OrganizationId,
            EnvironmentId,
            "SUP-CLEAR",
            "supplier",
            "Supplier");
        var parent = NewDevice("DEV-CLEAR-PARENT");
        dbContext.BusinessPartners.Add(supplier);
        dbContext.DeviceAssets.Add(parent);
        await dbContext.SaveChangesAsync();
        var device = NewDevice("DEV-CLEAR-CHILD")
            .WithLedger(
                null,
                null,
                string.Empty,
                null,
                supplier.Code,
                string.Empty,
                string.Empty,
                "LINE-1",
                string.Empty,
                parent.Id.ToString(),
                null);
        dbContext.DeviceAssets.Add(device);
        await dbContext.SaveChangesAsync();

        await UpdateHandler(dbContext).Handle(
            UpdateCommand(device.Code, supplierPartnerCode: "   ", parentDeviceId: string.Empty),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Empty(device.SupplierPartnerCode);
        Assert.Empty(device.ParentDeviceId);
    }

    [Fact]
    public async Task Update_WithSelfAsParent_ThrowsKnownException()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = NewDevice("DEV-SELF");
        dbContext.DeviceAssets.Add(device);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            UpdateHandler(dbContext).Handle(
                UpdateCommand(device.Code, parentDeviceId: device.Id.ToString()),
                CancellationToken.None));

        Assert.Contains("itself", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_WithDirectDescendantAsParent_ThrowsKnownException()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var first = NewDevice("DEV-CYCLE-A");
        var second = NewDevice("DEV-CYCLE-B");
        dbContext.DeviceAssets.AddRange(first, second);
        await dbContext.SaveChangesAsync();
        first.UpdateLedger(
            null, null, string.Empty, null, string.Empty, string.Empty, string.Empty,
            first.LineCode, string.Empty, second.Id.ToString(), null);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            UpdateHandler(dbContext).Handle(
                UpdateCommand(second.Code, parentDeviceId: first.Id.ToString()),
                CancellationToken.None));

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_WithMultiLevelDescendantAsParent_ThrowsKnownException()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var root = NewDevice("DEV-ROOT");
        var child = NewDevice("DEV-CHILD");
        var grandchild = NewDevice("DEV-GRANDCHILD");
        dbContext.DeviceAssets.AddRange(root, child, grandchild);
        await dbContext.SaveChangesAsync();
        child.UpdateLedger(
            null, null, string.Empty, null, string.Empty, string.Empty, string.Empty,
            child.LineCode, string.Empty, root.Id.ToString(), null);
        grandchild.UpdateLedger(
            null, null, string.Empty, null, string.Empty, string.Empty, string.Empty,
            grandchild.LineCode, string.Empty, child.Id.ToString(), null);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            UpdateHandler(dbContext).Handle(
                UpdateCommand(root.Code, parentDeviceId: grandchild.Id.ToString()),
                CancellationToken.None));

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_WhenProposedParentHasMalformedStoredAncestry_ThrowsKnownException()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = NewDevice("DEV-MALFORMED-ANCESTRY");
        var parent = NewDevice("DEV-MALFORMED-PARENT")
            .WithLedger(
                null, null, string.Empty, null, string.Empty, string.Empty, string.Empty,
                "LINE-1", string.Empty, "not-a-public-guid", null);
        dbContext.DeviceAssets.AddRange(device, parent);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            UpdateHandler(dbContext).Handle(
                UpdateCommand(device.Code, parentDeviceId: parent.Id.ToString()),
                CancellationToken.None));

        Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_WhenProposedParentAncestryAlreadyCycles_ThrowsKnownException()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = NewDevice("DEV-EXISTING-CYCLE-CANDIDATE");
        var first = NewDevice("DEV-EXISTING-CYCLE-A");
        var second = NewDevice("DEV-EXISTING-CYCLE-B");
        dbContext.DeviceAssets.AddRange(device, first, second);
        await dbContext.SaveChangesAsync();
        first.UpdateLedger(
            null, null, string.Empty, null, string.Empty, string.Empty, string.Empty,
            first.LineCode, string.Empty, second.Id.ToString(), null);
        second.UpdateLedger(
            null, null, string.Empty, null, string.Empty, string.Empty, string.Empty,
            second.LineCode, string.Empty, first.Id.ToString(), null);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            UpdateHandler(dbContext).Handle(
                UpdateCommand(device.Code, parentDeviceId: first.Id.ToString()),
                CancellationToken.None));

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_UnrelatedLegacyDeviceField_DoesNotRetroactivelyValidateOmittedReferences()
    {
        await using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var legacy = NewDevice("DEV-LEGACY")
            .WithLedger(
                null, null, string.Empty, null, "MISSING-LEGACY-SUPPLIER",
                string.Empty, string.Empty, "LINE-1", string.Empty, "LEGACY-PARENT-CODE", null);
        dbContext.DeviceAssets.Add(legacy);
        await dbContext.SaveChangesAsync();

        await UpdateHandler(dbContext).Handle(
            UpdateCommand(legacy.Code, model: "Updated model"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal("Updated model", legacy.Model);
        Assert.Equal("MISSING-LEGACY-SUPPLIER", legacy.SupplierPartnerCode);
        Assert.Equal("LEGACY-PARENT-CODE", legacy.ParentDeviceId);
    }

    private static RegisterDeviceAssetCommandHandler CreateHandler(ApplicationDbContext dbContext) =>
        new(new DeviceAssetRepository(dbContext), new DeviceAssetReferenceValidator(dbContext));

    private static UpdateMasterDataResourceCommandHandler UpdateHandler(ApplicationDbContext dbContext) =>
        new(dbContext, new ReferenceDataCodeRepository(dbContext));

    private static UpdateMasterDataResourceCommand UpdateCommand(
        string code,
        string? model = null,
        string? supplierPartnerCode = null,
        string? parentDeviceId = null) =>
        new(
            OrganizationId,
            EnvironmentId,
            "device-asset",
            code,
            Model: model,
            SupplierPartnerCode: supplierPartnerCode,
            ParentDeviceId: parentDeviceId);

    private static DeviceAsset NewDevice(string code) =>
        DeviceAsset.Register(
            OrganizationId,
            EnvironmentId,
            code,
            "Test device",
            "LINE-1",
            "WC-1");

    private static RegisterDeviceAssetCommand CreateCommand(
        string code,
        string? supplierPartnerCode = null,
        string? parentDeviceId = null) =>
        new(
            OrganizationId,
            EnvironmentId,
            code,
            "Test device",
            "LINE-1",
            "WC-1",
            "equipment",
            "ACME",
            $"SN-{code}",
            null,
            null,
            string.Empty,
            "normal",
            true,
            false,
            new Dictionary<string, string>(),
            SupplierPartnerCode: supplierPartnerCode,
            ParentDeviceId: parentDeviceId);

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"device-asset-reference-validation-{Guid.CreateVersion7():N}"));
        return services.BuildServiceProvider();
    }

    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
}
