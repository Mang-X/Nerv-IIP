using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class DeviceAssetReferenceConcurrencyPostgresTests
{
    [PostgresFact]
    public async Task OpposingParentAssignments_CannotBothCommit()
    {
        await using var database = await CreateDatabaseAsync();
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            setup.DeviceAssets.AddRange(NewDevice("DEV-A"), NewDevice("DEV-B"));
            await setup.SaveChangesAsync();
        }

        var barrier = new AsyncBarrier(2);
        await using var firstContext = CreateContext(database.ConnectionString);
        await using var secondContext = CreateContext(database.ConnectionString);
        var first = await firstContext.DeviceAssets.SingleAsync(x => x.Code == "DEV-A");
        var second = await secondContext.DeviceAssets.SingleAsync(x => x.Code == "DEV-B");
        var outcomes = await Task.WhenAll(
            RunUpdateAsync(
                firstContext,
                new UpdateMasterDataResourceCommand(
                    OrganizationId, EnvironmentId, "device-asset", first.Code,
                    ParentDeviceId: second.Id.ToString()),
                barrier),
            RunUpdateAsync(
                secondContext,
                new UpdateMasterDataResourceCommand(
                    OrganizationId, EnvironmentId, "device-asset", second.Code,
                    ParentDeviceId: first.Id.ToString()),
                barrier));

        Assert.Single(outcomes, outcome => outcome is not null);
        await using var verification = CreateContext(database.ConnectionString);
        await AssertParentReferencesAreValidAsync(verification);
    }

    [PostgresFact]
    public async Task SupplierAssignmentRacingDisable_CannotLeaveActiveDeviceWithInactiveSupplier()
    {
        await using var database = await CreateDatabaseAsync();
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            setup.BusinessPartners.Add(BusinessPartner.Create(
                OrganizationId,
                EnvironmentId,
                "SUP-RACE",
                "supplier",
                "Race supplier"));
            setup.DeviceAssets.Add(NewDevice("DEV-SUPPLIER-RACE"));
            await setup.SaveChangesAsync();
        }

        var barrier = new AsyncBarrier(2);
        await using var assignmentContext = CreateContext(database.ConnectionString);
        await using var disableContext = CreateContext(database.ConnectionString);
        var outcomes = await Task.WhenAll(
            RunUpdateAsync(
                assignmentContext,
                new UpdateMasterDataResourceCommand(
                    OrganizationId, EnvironmentId, "device-asset", "DEV-SUPPLIER-RACE",
                    SupplierPartnerCode: "SUP-RACE"),
                barrier),
            RunLifecycleAsync(
                disableContext,
                new SetMasterDataResourceEnabledCommand(
                    OrganizationId,
                    EnvironmentId,
                    "business-partner",
                    "SUP-RACE",
                    false,
                    "test:postgres",
                    "disable-supplier-race",
                    Reason: "race validation"),
                barrier));

        Assert.Single(outcomes, outcome => outcome is not null);
        await using var verification = CreateContext(database.ConnectionString);
        await AssertSupplierReferencesAreValidAsync(verification);
    }

    [PostgresFact]
    public async Task ParentAssignmentRacingDisable_CannotLeaveActiveChildWithInactiveParent()
    {
        await using var database = await CreateDatabaseAsync();
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            setup.DeviceAssets.AddRange(NewDevice("DEV-PARENT-RACE"), NewDevice("DEV-CHILD-RACE"));
            await setup.SaveChangesAsync();
        }

        var barrier = new AsyncBarrier(2);
        await using var assignmentContext = CreateContext(database.ConnectionString);
        await using var disableContext = CreateContext(database.ConnectionString);
        var parentId = (await assignmentContext.DeviceAssets.SingleAsync(x => x.Code == "DEV-PARENT-RACE")).Id;
        var outcomes = await Task.WhenAll(
            RunUpdateAsync(
                assignmentContext,
                new UpdateMasterDataResourceCommand(
                    OrganizationId, EnvironmentId, "device-asset", "DEV-CHILD-RACE",
                    ParentDeviceId: parentId.ToString()),
                barrier),
            RunLifecycleAsync(
                disableContext,
                new SetMasterDataResourceEnabledCommand(
                    OrganizationId,
                    EnvironmentId,
                    "device-asset",
                    "DEV-PARENT-RACE",
                    false,
                    "test:postgres",
                    "disable-parent-race",
                    Reason: "race validation"),
                barrier));

        Assert.Single(outcomes, outcome => outcome is not null);
        await using var verification = CreateContext(database.ConnectionString);
        await AssertParentReferencesAreValidAsync(verification);
    }

    private static async Task<Exception?> RunUpdateAsync(
        ApplicationDbContext dbContext,
        UpdateMasterDataResourceCommand command,
        AsyncBarrier barrier)
    {
        Exception? exception = null;
        try
        {
            await new UpdateMasterDataResourceCommandHandler(
                dbContext,
                new ReferenceDataCodeRepository(dbContext),
                new DeviceAssetReferenceValidator(dbContext),
                new PostgreSqlMasterDataReferenceScopeCoordinator(dbContext))
                .Handle(command, CancellationToken.None);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        await barrier.SignalAndWaitAsync();
        if (exception is null && dbContext.ChangeTracker.HasChanges())
        {
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        }

        return exception;
    }

    private static async Task<Exception?> RunLifecycleAsync(
        ApplicationDbContext dbContext,
        SetMasterDataResourceEnabledCommand command,
        AsyncBarrier barrier)
    {
        Exception? exception = null;
        try
        {
            await new SetMasterDataResourceEnabledCommandHandler(
                dbContext,
                referenceScopeCoordinator: new PostgreSqlMasterDataReferenceScopeCoordinator(dbContext))
                .Handle(command, CancellationToken.None);
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        await barrier.SignalAndWaitAsync();
        if (exception is null && dbContext.ChangeTracker.HasChanges())
        {
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        }

        return exception;
    }

    private static async Task AssertSupplierReferencesAreValidAsync(ApplicationDbContext dbContext)
    {
        var partners = await dbContext.BusinessPartners.AsNoTracking().ToArrayAsync();
        var activeDevices = await dbContext.DeviceAssets.AsNoTracking()
            .Where(x => !x.Disabled && x.SupplierPartnerCode != string.Empty)
            .ToArrayAsync();
        Assert.All(activeDevices, device =>
        {
            var supplier = Assert.Single(partners, partner =>
                partner.OrganizationId == device.OrganizationId &&
                partner.EnvironmentId == device.EnvironmentId &&
                partner.Code == device.SupplierPartnerCode &&
                !partner.Disabled);
            Assert.Contains(
                supplier.PartnerRoles,
                role => string.Equals(role, "supplier", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static async Task AssertParentReferencesAreValidAsync(ApplicationDbContext dbContext)
    {
        var devices = await dbContext.DeviceAssets.AsNoTracking().ToArrayAsync();
        var activeDevices = devices.Where(x => !x.Disabled).ToArray();
        foreach (var device in activeDevices.Where(x => x.ParentDeviceId.Length > 0))
        {
            Assert.True(Guid.TryParse(device.ParentDeviceId, out var parentPublicId));
            Assert.Contains(activeDevices, parent =>
                parent.OrganizationId == device.OrganizationId &&
                parent.EnvironmentId == device.EnvironmentId &&
                parent.Id.Id == parentPublicId);
        }

        foreach (var device in activeDevices)
        {
            var visited = new HashSet<Guid>();
            var current = device;
            while (current.ParentDeviceId.Length > 0)
            {
                Assert.True(visited.Add(current.Id.Id), $"Cycle detected from '{device.Code}'.");
                var parentId = Guid.Parse(current.ParentDeviceId);
                current = Assert.Single(activeDevices, candidate =>
                    candidate.OrganizationId == device.OrganizationId &&
                    candidate.EnvironmentId == device.EnvironmentId &&
                    candidate.Id.Id == parentId);
            }
        }
    }

    private static Task<PostgreSqlTestDatabase> CreateDatabaseAsync() =>
        PostgreSqlTestDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!,
            "nerv_masterdata_reference");

    private static ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataFacts.Schema))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static DeviceAsset NewDevice(string code) =>
        DeviceAsset.Register(
            OrganizationId,
            EnvironmentId,
            code,
            "Test device",
            "LINE-1",
            "WC-1");

    private sealed class AsyncBarrier(int participantCount)
    {
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public async Task SignalAndWaitAsync()
        {
            if (Interlocked.Increment(ref arrivals) == participantCount)
            {
                release.TrySetResult();
            }

            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
}
