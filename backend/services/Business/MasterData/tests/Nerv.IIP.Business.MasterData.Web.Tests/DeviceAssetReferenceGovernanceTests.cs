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

public sealed class DeviceAssetReferenceGovernanceTests
{
    [Fact]
    public async Task ReEnable_WithInactiveStoredSupplier_ThrowsKnownExceptionWithoutMutation()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var supplier = BusinessPartner.Create(
            OrganizationId,
            EnvironmentId,
            "SUP-REENABLE",
            "supplier",
            "Supplier");
        var device = NewDevice("DEV-REENABLE-SUPPLIER")
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
                string.Empty,
                null);
        dbContext.BusinessPartners.Add(supplier);
        dbContext.DeviceAssets.Add(device);
        await dbContext.SaveChangesAsync();
        device.Disable("test setup");
        supplier.Disable("test setup");
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            LifecycleHandler(dbContext).Handle(
                EnableDevice(device.Code, "reenable-inactive-supplier"),
                CancellationToken.None));

        Assert.Contains("supplier", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(device.Disabled);
    }

    [Fact]
    public async Task ReEnable_WithInactiveStoredParent_ThrowsKnownExceptionWithoutMutation()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parent = NewDevice("DEV-REENABLE-PARENT");
        dbContext.DeviceAssets.Add(parent);
        await dbContext.SaveChangesAsync();
        var child = NewDevice("DEV-REENABLE-CHILD")
            .WithLedger(
                null,
                null,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                "LINE-1",
                string.Empty,
                parent.Id.ToString(),
                null);
        dbContext.DeviceAssets.Add(child);
        await dbContext.SaveChangesAsync();
        child.Disable("test setup");
        parent.Disable("test setup");
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            LifecycleHandler(dbContext).Handle(
                EnableDevice(child.Code, "reenable-inactive-parent"),
                CancellationToken.None));

        Assert.Contains("parent", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(child.Disabled);
    }

    [Fact]
    public async Task ReEnable_WithMalformedStoredParentAncestry_ThrowsKnownExceptionWithoutMutation()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ancestor = NewDevice("DEV-REENABLE-MALFORMED-ANCESTOR")
            .WithLedger(
                null,
                null,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                "LINE-1",
                string.Empty,
                "legacy-parent-code",
                null);
        dbContext.DeviceAssets.Add(ancestor);
        await dbContext.SaveChangesAsync();
        var child = NewDevice("DEV-REENABLE-MALFORMED-CHILD")
            .WithLedger(
                null,
                null,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                "LINE-1",
                string.Empty,
                ancestor.Id.ToString(),
                null);
        dbContext.DeviceAssets.Add(child);
        await dbContext.SaveChangesAsync();
        child.Disable("test setup");
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            LifecycleHandler(dbContext).Handle(
                EnableDevice(child.Code, "reenable-malformed-ancestry"),
                CancellationToken.None));

        Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(child.Disabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReferencedSupplierRoleRemoval_ThrowsKnownExceptionWithoutMutation(
        bool changePrimaryRole)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var partner = BusinessPartner.Create(
            OrganizationId,
            EnvironmentId,
            "SUP-REFERENCED",
            "supplier",
            "Referenced supplier",
            changePrimaryRole ? ["supplier"] : ["supplier", "customer"],
            null);
        var device = NewDevice("DEV-REFERENCING-SUPPLIER")
            .WithLedger(
                null,
                null,
                string.Empty,
                null,
                partner.Code,
                string.Empty,
                string.Empty,
                "LINE-1",
                string.Empty,
                string.Empty,
                null);
        dbContext.BusinessPartners.Add(partner);
        dbContext.DeviceAssets.Add(device);
        await dbContext.SaveChangesAsync();

        var command = new UpdateMasterDataResourceCommand(
            OrganizationId,
            EnvironmentId,
            "business-partner",
            partner.Code,
            Name: "Must not apply",
            PartnerType: changePrimaryRole ? "customer" : null,
            PartnerRoles: changePrimaryRole ? null : ["customer"]);
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            UpdateHandler(dbContext).Handle(command, CancellationToken.None));

        Assert.Contains("supplier", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            partner.PartnerRoles,
            role => string.Equals(role, "supplier", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Referenced supplier", partner.Name);
    }

    [Fact]
    public async Task ReferencedSupplierUpdate_RetainingMixedCaseSupplierAndMultipleRoles_Succeeds()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var partner = BusinessPartner.Create(
            OrganizationId,
            EnvironmentId,
            "SUP-RETAIN",
            "supplier",
            "Referenced supplier",
            ["supplier", "customer"],
            null);
        var device = NewDevice("DEV-RETAIN-SUPPLIER")
            .WithLedger(
                null,
                null,
                string.Empty,
                null,
                partner.Code,
                string.Empty,
                string.Empty,
                "LINE-1",
                string.Empty,
                string.Empty,
                null);
        dbContext.BusinessPartners.Add(partner);
        dbContext.DeviceAssets.Add(device);
        await dbContext.SaveChangesAsync();

        await UpdateHandler(dbContext).Handle(
            new UpdateMasterDataResourceCommand(
                OrganizationId,
                EnvironmentId,
                "business-partner",
                partner.Code,
                Name: "Updated supplier",
                PartnerRoles: ["customer", "SuPpLiEr", "service"]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal("Updated supplier", partner.Name);
        Assert.Equal(["customer", "SuPpLiEr", "service"], partner.PartnerRoles);
    }

    [Fact]
    public async Task ParentDisable_WithBracedUppercaseStoredPublicGuid_ThrowsKnownException()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parent = NewDevice("DEV-LEGACY-PARENT");
        dbContext.DeviceAssets.Add(parent);
        await dbContext.SaveChangesAsync();
        var child = NewDevice("DEV-LEGACY-CHILD")
            .WithLedger(
                null,
                null,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                "LINE-1",
                string.Empty,
                $"{{{parent.Id.ToString().ToUpperInvariant()}}}",
                null);
        dbContext.DeviceAssets.Add(child);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            LifecycleHandler(dbContext).Handle(
                new SetMasterDataResourceEnabledCommand(
                    OrganizationId,
                    EnvironmentId,
                    "device-asset",
                    parent.Code,
                    false,
                    "test:review",
                    "disable-legacy-parent",
                    Reason: "review regression"),
                CancellationToken.None));

        Assert.Contains("child", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(parent.Disabled);
    }

    private static UpdateMasterDataResourceCommandHandler UpdateHandler(ApplicationDbContext dbContext) =>
        new(
            dbContext,
            new ReferenceDataCodeRepository(dbContext),
            new DeviceAssetReferenceValidator(dbContext),
            new PostgreSqlMasterDataReferenceScopeCoordinator(dbContext));

    private static SetMasterDataResourceEnabledCommandHandler LifecycleHandler(ApplicationDbContext dbContext) =>
        new(
            dbContext,
            referenceScopeCoordinator: new PostgreSqlMasterDataReferenceScopeCoordinator(dbContext));

    private static SetMasterDataResourceEnabledCommand EnableDevice(string code, string operationId) =>
        new(
            OrganizationId,
            EnvironmentId,
            "device-asset",
            code,
            true,
            "test:review",
            operationId,
            Reason: "review regression");

    private static DeviceAsset NewDevice(string code) =>
        DeviceAsset.Register(
            OrganizationId,
            EnvironmentId,
            code,
            "Test device",
            "LINE-1",
            "WC-1");

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"device-asset-reference-governance-{Guid.CreateVersion7():N}"));
        return services.BuildServiceProvider();
    }

    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
}
