using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.Mes.Web.Tests;

// Contract: Governance. Authority: Issue #2234 and Review 5045814925.
public sealed class MesEfSourceRootApiSurfaceTests
{
    [Fact]
    public void EF_Core_public_source_surface_has_one_complete_exact_partition()
    {
        Assert.Equal(new Version(10, 0, 8, 0), typeof(DbContext).Assembly.GetName().Version);
        Assert.Equal(new Version(10, 0, 8, 0), typeof(RelationalDatabaseFacadeExtensions).Assembly.GetName().Version);

        var observed = SelectedOwners
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(method => Normalize(method)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = Partition.Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(136, observed.Length);
        Assert.Equal(expected, observed);
        Assert.Equal(16, Partition.Count(row => row.Value == ApiCategory.Root));
        Assert.Equal(24, Partition.Count(row => row.Value == ApiCategory.ExistingEntityConsumer));
        Assert.Equal(96, Partition.Count(row => row.Value == ApiCategory.StateMetadataCommand));
    }

    [Fact]
    public void Partition_has_no_duplicate_or_unknown_category_rows()
    {
        var rows = ParseManifest().ToArray();

        Assert.Equal(136, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.Signature).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, row => Assert.True(Enum.IsDefined(row.Category)));
    }

    internal static IReadOnlyDictionary<string, ApiCategory> Partition { get; } = ParseManifest()
        .ToDictionary(row => row.Signature, row => row.Category, StringComparer.Ordinal);

    private static IEnumerable<(string Signature, ApiCategory Category)> ParseManifest()
    {
        foreach (var line in ExpectedPartition.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('|');
            Assert.True(separator > 0, $"Invalid EF API partition row: {line}");
            var category = Enum.Parse<ApiCategory>(line[..separator], ignoreCase: false);
            yield return (line[(separator + 1)..], category);
        }
    }

    private static string Normalize(MethodInfo method) =>
        $"{method.DeclaringType!.Assembly.GetName().Name}|{method.DeclaringType.FullName}|{method}";

    private static readonly Type[] SelectedOwners =
    [
        typeof(DbContext),
        typeof(Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker),
        typeof(RelationalDatabaseFacadeExtensions),
        typeof(RelationalQueryableExtensions),
    ];

    private const string ExpectedPartition = """
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Add(System.Object)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Add[TEntity](TEntity)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void add_SaveChangesFailed(System.EventHandler`1[Microsoft.EntityFrameworkCore.SaveChangesFailedEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void add_SavedChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.SavedChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void add_SavingChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.SavingChangesEventArgs])
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry] AddAsync(System.Object, System.Threading.CancellationToken)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity]] AddAsync[TEntity](TEntity, System.Threading.CancellationToken)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void AddRange(System.Collections.Generic.IEnumerable`1[System.Object])
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void AddRange(System.Object[])
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.Task AddRangeAsync(System.Collections.Generic.IEnumerable`1[System.Object], System.Threading.CancellationToken)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.Task AddRangeAsync(System.Object[])
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Attach(System.Object)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Attach[TEntity](TEntity)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void AttachRange(System.Collections.Generic.IEnumerable`1[System.Object])
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void AttachRange(System.Object[])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void Dispose()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask DisposeAsync()
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry(System.Object)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Entry[TEntity](TEntity)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Boolean Equals(System.Object)
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Object Find(System.Type, System.Object[])
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|TEntity Find[TEntity](System.Object[])
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[System.Object] FindAsync(System.Type, System.Object[], System.Threading.CancellationToken)
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[System.Object] FindAsync(System.Type, System.Object[])
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[TEntity] FindAsync[TEntity](System.Object[], System.Threading.CancellationToken)
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[TEntity] FindAsync[TEntity](System.Object[])
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Linq.IQueryable`1[TResult] FromExpression[TResult](System.Linq.Expressions.Expression`1[System.Func`1[System.Linq.IQueryable`1[TResult]]])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker get_ChangeTracker()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.DbContextId get_ContextId()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade get_Database()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.Metadata.IModel get_Model()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Int32 GetHashCode()
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Remove(System.Object)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Remove[TEntity](TEntity)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void remove_SaveChangesFailed(System.EventHandler`1[Microsoft.EntityFrameworkCore.SaveChangesFailedEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void remove_SavedChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.SavedChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void remove_SavingChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.SavingChangesEventArgs])
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void RemoveRange(System.Collections.Generic.IEnumerable`1[System.Object])
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void RemoveRange(System.Object[])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Int32 SaveChanges()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Int32 SaveChanges(Boolean)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.Task`1[System.Int32] SaveChangesAsync(Boolean, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.Task`1[System.Int32] SaveChangesAsync(System.Threading.CancellationToken)
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.DbSet`1[TEntity] Set[TEntity]()
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.DbSet`1[TEntity] Set[TEntity](System.String)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.String ToString()
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Update(System.Object)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Update[TEntity](TEntity)
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void UpdateRange(System.Collections.Generic.IEnumerable`1[System.Object])
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void UpdateRange(System.Object[])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void AcceptAllChanges()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_DetectedAllChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectedChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_DetectedEntityChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectedEntityChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_DetectingAllChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_DetectingEntityChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectEntityChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_StateChanged(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityStateChangedEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_StateChanging(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityStateChangingEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_Tracked(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackedEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_Tracking(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackingEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void CascadeChanges()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void Clear()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void DetectChanges()
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|System.Collections.Generic.IEnumerable`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry] Entries()
Root|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|System.Collections.Generic.IEnumerable`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity]] Entries[TEntity]()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Boolean Equals(System.Object)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Boolean get_AutoDetectChangesEnabled()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming get_CascadeDeleteTiming()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.DbContext get_Context()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.Infrastructure.DebugView get_DebugView()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming get_DeleteOrphansTiming()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Boolean get_LazyLoadingEnabled()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.QueryTrackingBehavior get_QueryTrackingBehavior()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Int32 GetHashCode()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Boolean HasChanges()
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_DetectedAllChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectedChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_DetectedEntityChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectedEntityChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_DetectingAllChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_DetectingEntityChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectEntityChangesEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_StateChanged(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityStateChangedEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_StateChanging(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityStateChangingEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_Tracked(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackedEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_Tracking(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackingEventArgs])
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_AutoDetectChangesEnabled(Boolean)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_CascadeDeleteTiming(Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_DeleteOrphansTiming(Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_LazyLoadingEnabled(Boolean)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_QueryTrackingBehavior(Microsoft.EntityFrameworkCore.QueryTrackingBehavior)
StateMetadataCommand|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|System.String ToString()
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void TrackGraph(System.Object, System.Action`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntryGraphNode])
ExistingEntityConsumer|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void TrackGraph[TState](System.Object, TState, System.Func`2[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntryGraphNode`1[TState],System.Boolean])
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction BeginTransaction(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.IsolationLevel)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction] BeginTransactionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.IsolationLevel, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void CloseConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task CloseConnectionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Int32 ExecuteSql(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Int32 ExecuteSqlInterpolated(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlInterpolatedAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Int32 ExecuteSqlRaw(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Collections.Generic.IEnumerable`1[System.Object])
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Int32 ExecuteSqlRaw(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Object[])
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlRawAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Collections.Generic.IEnumerable`1[System.Object], System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlRawAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Object[])
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlRawAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.String GenerateCreateScript(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Collections.Generic.IEnumerable`1[System.String] GetAppliedMigrations(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Collections.Generic.IEnumerable`1[System.String]] GetAppliedMigrationsAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Nullable`1[System.Int32] GetCommandTimeout(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.String GetConnectionString(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Data.Common.DbConnection GetDbConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Collections.Generic.IEnumerable`1[System.String] GetMigrations(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Collections.Generic.IEnumerable`1[System.String] GetPendingMigrations(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Collections.Generic.IEnumerable`1[System.String]] GetPendingMigrationsAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Boolean HasPendingModelChanges(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Boolean IsRelational(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void Migrate(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void Migrate(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task MigrateAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task MigrateAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void OpenConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task OpenConnectionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void SetCommandTimeout(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Nullable`1[System.Int32])
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void SetCommandTimeout(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.TimeSpan)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void SetConnectionString(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void SetDbConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbConnection, Boolean)
Root|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Linq.IQueryable`1[TResult] SqlQuery[TResult](Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString)
Root|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Linq.IQueryable`1[TResult] SqlQueryRaw[TResult](Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Object[])
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction UseTransaction(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbTransaction, System.Guid)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction UseTransaction(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbTransaction)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction] UseTransactionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbTransaction, System.Guid, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction] UseTransactionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbTransaction, System.Threading.CancellationToken)
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] AsSingleQuery[TEntity](System.Linq.IQueryable`1[TEntity])
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] AsSplitQuery[TEntity](System.Linq.IQueryable`1[TEntity])
StateMetadataCommand|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Data.Common.DbCommand CreateDbCommand(System.Linq.IQueryable)
Root|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] FromSql[TEntity](Microsoft.EntityFrameworkCore.DbSet`1[TEntity], System.FormattableString)
Root|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] FromSqlInterpolated[TEntity](Microsoft.EntityFrameworkCore.DbSet`1[TEntity], System.FormattableString)
Root|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] FromSqlRaw[TEntity](Microsoft.EntityFrameworkCore.DbSet`1[TEntity], System.String, System.Object[])
""";

    internal enum ApiCategory
    {
        Root,
        ExistingEntityConsumer,
        StateMetadataCommand,
    }
}
