using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Testing.PostgreSql;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Npgsql;

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

        var commitWindow = new AsyncCommitWindow(2);
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
                commitWindow),
            RunUpdateAsync(
                secondContext,
                new UpdateMasterDataResourceCommand(
                    OrganizationId, EnvironmentId, "device-asset", second.Code,
                    ParentDeviceId: first.Id.ToString()),
                commitWindow));

        AssertSingleKnownException(outcomes);
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

        var commitWindow = new AsyncCommitWindow(2);
        await using var assignmentContext = CreateContext(database.ConnectionString);
        await using var disableContext = CreateContext(database.ConnectionString);
        var outcomes = await Task.WhenAll(
            RunUpdateAsync(
                assignmentContext,
                new UpdateMasterDataResourceCommand(
                    OrganizationId, EnvironmentId, "device-asset", "DEV-SUPPLIER-RACE",
                    SupplierPartnerCode: "SUP-RACE"),
                commitWindow),
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
                commitWindow));

        AssertSingleKnownException(outcomes);
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

        var commitWindow = new AsyncCommitWindow(2);
        await using var assignmentContext = CreateContext(database.ConnectionString);
        await using var disableContext = CreateContext(database.ConnectionString);
        var parentId = (await assignmentContext.DeviceAssets.SingleAsync(x => x.Code == "DEV-PARENT-RACE")).Id;
        var outcomes = await Task.WhenAll(
            RunUpdateAsync(
                assignmentContext,
                new UpdateMasterDataResourceCommand(
                    OrganizationId, EnvironmentId, "device-asset", "DEV-CHILD-RACE",
                    ParentDeviceId: parentId.ToString()),
                commitWindow),
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
                commitWindow));

        AssertSingleKnownException(outcomes);
        await using var verification = CreateContext(database.ConnectionString);
        await AssertParentReferencesAreValidAsync(verification);
    }

    [PostgresFact]
    public async Task SupplierDisableRacingChildReEnable_CannotCommitInvalidReference()
    {
        await using var database = await CreateDatabaseAsync();
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            var supplier = BusinessPartner.Create(
                OrganizationId,
                EnvironmentId,
                "SUP-REENABLE-RACE",
                "supplier",
                "Re-enable race supplier");
            var child = NewDevice("DEV-REENABLE-SUPPLIER-RACE")
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
            child.Disable("test setup");
            setup.BusinessPartners.Add(supplier);
            setup.DeviceAssets.Add(child);
            await setup.SaveChangesAsync();
        }

        var commitWindow = new AsyncCommitWindow(2);
        await using var enableContext = CreateContext(database.ConnectionString);
        await using var disableContext = CreateContext(database.ConnectionString);
        var outcomes = await Task.WhenAll(
            RunLifecycleAsync(
                enableContext,
                new SetMasterDataResourceEnabledCommand(
                    OrganizationId,
                    EnvironmentId,
                    "device-asset",
                    "DEV-REENABLE-SUPPLIER-RACE",
                    true,
                    "test:postgres",
                    "reenable-supplier-race",
                    Reason: "race validation"),
                commitWindow),
            RunLifecycleAsync(
                disableContext,
                new SetMasterDataResourceEnabledCommand(
                    OrganizationId,
                    EnvironmentId,
                    "business-partner",
                    "SUP-REENABLE-RACE",
                    false,
                    "test:postgres",
                    "disable-reenable-supplier-race",
                    Reason: "race validation"),
                commitWindow));

        AssertSingleKnownException(outcomes);
        await using var verification = CreateContext(database.ConnectionString);
        await AssertSupplierReferencesAreValidAsync(verification);
    }

    [PostgresFact]
    public async Task ParentDisableRacingChildReEnable_CannotCommitInvalidReference()
    {
        await using var database = await CreateDatabaseAsync();
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            var parent = NewDevice("DEV-REENABLE-PARENT-RACE");
            setup.DeviceAssets.Add(parent);
            await setup.SaveChangesAsync();
            var child = NewDevice("DEV-REENABLE-CHILD-RACE")
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
            child.Disable("test setup");
            setup.DeviceAssets.Add(child);
            await setup.SaveChangesAsync();
        }

        var commitWindow = new AsyncCommitWindow(2);
        await using var enableContext = CreateContext(database.ConnectionString);
        await using var disableContext = CreateContext(database.ConnectionString);
        var outcomes = await Task.WhenAll(
            RunLifecycleAsync(
                enableContext,
                new SetMasterDataResourceEnabledCommand(
                    OrganizationId,
                    EnvironmentId,
                    "device-asset",
                    "DEV-REENABLE-CHILD-RACE",
                    true,
                    "test:postgres",
                    "reenable-parent-race",
                    Reason: "race validation"),
                commitWindow),
            RunLifecycleAsync(
                disableContext,
                new SetMasterDataResourceEnabledCommand(
                    OrganizationId,
                    EnvironmentId,
                    "device-asset",
                    "DEV-REENABLE-PARENT-RACE",
                    false,
                    "test:postgres",
                    "disable-reenable-parent-race",
                    Reason: "race validation"),
                commitWindow));

        AssertSingleKnownException(outcomes);
        await using var verification = CreateContext(database.ConnectionString);
        await AssertParentReferencesAreValidAsync(verification);
    }

    [PostgresFact]
    public async Task SupplierAssignmentRacingRoleRemoval_CannotCommitInvalidReference()
    {
        await using var database = await CreateDatabaseAsync();
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            setup.BusinessPartners.Add(BusinessPartner.Create(
                OrganizationId,
                EnvironmentId,
                "SUP-ROLE-RACE",
                "supplier",
                "Role race supplier",
                ["supplier", "customer"],
                null));
            setup.DeviceAssets.Add(NewDevice("DEV-ROLE-RACE"));
            await setup.SaveChangesAsync();
        }

        var commitWindow = new AsyncCommitWindow(2);
        await using var assignmentContext = CreateContext(database.ConnectionString);
        await using var roleContext = CreateContext(database.ConnectionString);
        var outcomes = await Task.WhenAll(
            RunUpdateAsync(
                assignmentContext,
                new UpdateMasterDataResourceCommand(
                    OrganizationId,
                    EnvironmentId,
                    "device-asset",
                    "DEV-ROLE-RACE",
                    SupplierPartnerCode: "SUP-ROLE-RACE"),
                commitWindow),
            RunUpdateAsync(
                roleContext,
                new UpdateMasterDataResourceCommand(
                    OrganizationId,
                    EnvironmentId,
                    "business-partner",
                    "SUP-ROLE-RACE",
                    PartnerRoles: ["customer"]),
                commitWindow));

        AssertSingleKnownException(outcomes);
        await using var verification = CreateContext(database.ConnectionString);
        await AssertSupplierReferencesAreValidAsync(verification);
    }

    [PostgresFact]
    public async Task JoinedTransaction_SavesBeforeReturnRetainsLockAndCallerRollbackRemovesMutation()
    {
        await using var database = await CreateDatabaseAsync();
        await using var dbContext = CreateContext(database.ConnectionString);
        await dbContext.Database.MigrateAsync();
        var unitOfWork = (ITransactionUnitOfWork)dbContext;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        unitOfWork.CurrentTransaction = transaction;
        var coordinator = new PostgreSqlMasterDataReferenceScopeCoordinator(dbContext);

        await coordinator.ExecuteAsync(
            OrganizationId,
            EnvironmentId,
            token =>
            {
                dbContext.DeviceAssets.Add(NewDevice("DEV-JOINED-ROLLBACK"));
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Assert.Same(transaction, unitOfWork.CurrentTransaction);
        Assert.False(dbContext.ChangeTracker.HasChanges());
        await using (var observer = CreateContext(database.ConnectionString))
        {
            Assert.False(await observer.DeviceAssets.AnyAsync(x => x.Code == "DEV-JOINED-ROLLBACK"));
        }

        await using (var contender = CreateContext(database.ConnectionString))
        await using (var contenderTransaction = await contender.Database.BeginTransactionAsync())
        {
            await contender.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '250ms'");
            var lockKey = $"masterdata-reference:{OrganizationId}:{EnvironmentId}";
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                contender.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))"));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        }

        await transaction.RollbackAsync();
        unitOfWork.CurrentTransaction = null;
        await using var verification = CreateContext(database.ConnectionString);
        Assert.False(await verification.DeviceAssets.AnyAsync(x => x.Code == "DEV-JOINED-ROLLBACK"));
    }

    [PostgresFact]
    public async Task JoinedTransaction_CancellationPropagatesWithoutOwningCallerTransaction()
    {
        await using var database = await CreateDatabaseAsync();
        await using var dbContext = CreateContext(database.ConnectionString);
        await dbContext.Database.MigrateAsync();
        var unitOfWork = (ITransactionUnitOfWork)dbContext;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        unitOfWork.CurrentTransaction = transaction;
        var actionCalled = false;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PostgreSqlMasterDataReferenceScopeCoordinator(dbContext).ExecuteAsync(
                OrganizationId,
                EnvironmentId,
                _ =>
                {
                    actionCalled = true;
                    return Task.FromResult(true);
                },
                cancellation.Token));

        Assert.False(actionCalled);
        Assert.Same(transaction, unitOfWork.CurrentTransaction);
        await transaction.RollbackAsync();
        unitOfWork.CurrentTransaction = null;
    }

    private static void AssertSingleKnownException(IReadOnlyCollection<Exception?> outcomes)
    {
        var exception = Assert.Single(outcomes, outcome => outcome is not null);
        Assert.IsType<KnownException>(exception);
        Assert.Single(outcomes, outcome => outcome is null);
    }

    private static async Task<Exception?> RunUpdateAsync(
        ApplicationDbContext dbContext,
        UpdateMasterDataResourceCommand command,
        AsyncCommitWindow commitWindow)
    {
        var unitOfWork = (ITransactionUnitOfWork)dbContext;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        unitOfWork.CurrentTransaction = transaction;
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

        await commitWindow.SignalAndWaitAsync();
        try
        {
            if (exception is null)
            {
                await transaction.CommitAsync();
            }
            else
            {
                await transaction.RollbackAsync();
            }
        }
        catch (Exception caught)
        {
            exception = caught;
            await transaction.RollbackAsync();
        }
        finally
        {
            unitOfWork.CurrentTransaction = null;
        }

        return exception;
    }

    private static async Task<Exception?> RunLifecycleAsync(
        ApplicationDbContext dbContext,
        SetMasterDataResourceEnabledCommand command,
        AsyncCommitWindow commitWindow)
    {
        var unitOfWork = (ITransactionUnitOfWork)dbContext;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        unitOfWork.CurrentTransaction = transaction;
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

        await commitWindow.SignalAndWaitAsync();
        try
        {
            if (exception is null)
            {
                await transaction.CommitAsync();
            }
            else
            {
                await transaction.RollbackAsync();
            }
        }
        catch (Exception caught)
        {
            exception = caught;
            await transaction.RollbackAsync();
        }
        finally
        {
            unitOfWork.CurrentTransaction = null;
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

    private sealed class AsyncCommitWindow(int participantCount)
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

            await Task.WhenAny(release.Task, Task.Delay(TimeSpan.FromMilliseconds(300)));
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
