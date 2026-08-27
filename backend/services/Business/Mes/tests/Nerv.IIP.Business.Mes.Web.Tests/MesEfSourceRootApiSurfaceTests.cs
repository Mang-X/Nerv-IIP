using System.Reflection;
using Microsoft.CodeAnalysis;
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

        var observedMethods = SelectedOwners
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .ToArray();
        var observed = observedMethods
            .Select(NormalizeManifestSignature)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = Partition.Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(136, observed.Length);
        Assert.Equal(expected, observed);
        Assert.Equal(136, observedMethods.Select(CreateIdentity).Distinct().Count());
        Assert.Equal(16, Partition.Count(row => row.Value.Category == ApiCategory.Root));
        Assert.Equal(24, Partition.Count(row => row.Value.Category == ApiCategory.ExistingEntityConsumer));
        Assert.Equal(96, Partition.Count(row => row.Value.Category == ApiCategory.StateMetadataCommand));
        Assert.Equal(12, Partition.Count(row => row.Value.Evidence.Kind == EntityEvidenceKind.ClosedGenericTypeArgument));
        Assert.Equal(3, Partition.Count(row => row.Value.Evidence.Kind == EntityEvidenceKind.RuntimeTypeArgument));
        Assert.Single(Partition, row => row.Value.Evidence.Kind == EntityEvidenceKind.AllTrackedEntities);
        Assert.Equal(120, Partition.Count(row => row.Value.Evidence.Kind == EntityEvidenceKind.NoEntitySource));
    }

    [Fact]
    public void Partition_has_no_duplicate_or_unknown_category_rows()
    {
        var rows = ParseManifest().ToArray();

        Assert.Equal(136, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.Signature).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, row =>
        {
            Assert.True(Enum.IsDefined(row.Rule.Category));
            Assert.True(Enum.IsDefined(row.Rule.Evidence.Kind));
            Assert.Equal(
                row.Rule.Category == ApiCategory.Root,
                row.Rule.Evidence.Kind != EntityEvidenceKind.NoEntitySource);
        });
    }

    internal static IReadOnlyDictionary<string, ApiRule> Partition { get; } = ParseManifest()
        .ToDictionary(row => row.Signature, row => row.Rule, StringComparer.Ordinal);

    private static IReadOnlyDictionary<EfApiIdentity, ApiRule> RulesByIdentity { get; } = SelectedOwners
        .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        .ToDictionary(CreateIdentity, method => Partition[NormalizeManifestSignature(method)]);

    internal static bool TryGetRule(IMethodSymbol method, out ApiRule rule)
    {
        var definition = method.ReducedFrom ?? method.OriginalDefinition;
        if (RulesByIdentity.TryGetValue(CreateIdentity(definition), out var found))
        {
            rule = found;
            return true;
        }

        rule = null!;
        return false;
    }

    internal static bool TryGetDynamicRule(
        string owner,
        string metadataName,
        int genericArity,
        int argumentCount,
        out ApiRule rule)
    {
        var candidates = RulesByIdentity
            .Where(pair => pair.Key.Owner == owner
                && pair.Key.MetadataName == metadataName
                && pair.Key.GenericArity == genericArity
                && pair.Key.ParameterTypes.Length == argumentCount
                && pair.Value.Category == ApiCategory.Root)
            .Select(pair => pair.Value)
            .Distinct()
            .ToArray();
        rule = candidates.Length == 1 ? candidates[0] : null!;
        return candidates.Length == 1;
    }

    internal static bool HasReflectedGenericRoot(string owner, string metadataName, int genericArity) =>
        RulesByIdentity.Any(pair => pair.Key.Owner == owner
            && pair.Key.MetadataName == metadataName
            && pair.Key.GenericArity == genericArity
            && pair.Value is { Category: ApiCategory.Root, Evidence.Kind: EntityEvidenceKind.ClosedGenericTypeArgument });

    private static IEnumerable<(string Signature, ApiRule Rule)> ParseManifest()
    {
        foreach (var line in ExpectedPartition.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var first = line.IndexOf('|');
            var second = line.IndexOf('|', first + 1);
            Assert.True(first > 0 && second > first, $"Invalid EF API partition row: {line}");
            var category = Enum.Parse<ApiCategory>(line[..first], ignoreCase: false);
            var evidence = ParseEvidence(line[(first + 1)..second]);
            yield return (line[(second + 1)..], new ApiRule(category, evidence));
        }
    }

    private static EntityEvidencePolicy ParseEvidence(string value)
    {
        var fields = value.Split(':');
        return Enum.Parse<EntityEvidenceKind>(fields[0], ignoreCase: false) switch
        {
            EntityEvidenceKind.ClosedGenericTypeArgument => new(EntityEvidenceKind.ClosedGenericTypeArgument, int.Parse(fields[1]), false),
            EntityEvidenceKind.RuntimeTypeArgument => new(EntityEvidenceKind.RuntimeTypeArgument, int.Parse(fields[1]), bool.Parse(fields[2])),
            EntityEvidenceKind.AllTrackedEntities => new(EntityEvidenceKind.AllTrackedEntities, -1, false),
            _ => new(EntityEvidenceKind.NoEntitySource, -1, false),
        };
    }

    private static string NormalizeManifestSignature(MethodInfo method) =>
        $"{method.DeclaringType!.Assembly.GetName().Name}|{method.DeclaringType.FullName}|{method}";

    private static EfApiIdentity CreateIdentity(MethodInfo method) => new(
        method.DeclaringType!.Assembly.GetName().Name!,
        method.DeclaringType.FullName!,
        method.Name,
        method.GetGenericArguments().Length,
        method.IsStatic,
        method.GetParameters().Select(parameter => NormalizeType(parameter.ParameterType)).ToArray());

    private static EfApiIdentity CreateIdentity(IMethodSymbol method) => new(
        method.ContainingAssembly.Name,
        method.ContainingType.ToDisplayString(),
        method.MetadataName,
        method.Arity,
        method.IsStatic,
        method.Parameters.Select(parameter => NormalizeType(parameter.Type, parameter.RefKind)).ToArray());

    private static string NormalizeType(Type type)
    {
        if (type.IsByRef)
        {
            return $"{NormalizeType(type.GetElementType()!)}&";
        }

        if (type.IsArray)
        {
            return $"{NormalizeType(type.GetElementType()!)}[{new string(',', type.GetArrayRank() - 1)}]";
        }

        if (type.IsGenericParameter)
        {
            return $"{(type.DeclaringMethod is null ? "!" : "!!")}{type.GenericParameterPosition}";
        }

        if (!type.IsGenericType)
        {
            return type.FullName!;
        }

        return $"{type.GetGenericTypeDefinition().FullName}<{string.Join(',', type.GetGenericArguments().Select(NormalizeType))}>";
    }

    private static string NormalizeType(ITypeSymbol type, RefKind refKind = RefKind.None)
    {
        var suffix = refKind == RefKind.None ? string.Empty : "&";
        return type switch
        {
            IArrayTypeSymbol array => $"{NormalizeType(array.ElementType)}[{new string(',', array.Rank - 1)}]{suffix}",
            ITypeParameterSymbol parameter => $"{(parameter.TypeParameterKind == TypeParameterKind.Method ? "!!" : "!")}{parameter.Ordinal}{suffix}",
            INamedTypeSymbol named when named.IsGenericType =>
                $"{GetMetadataName(named.OriginalDefinition)}<{string.Join(',', named.TypeArguments.Select(argument => NormalizeType(argument)))}>{suffix}",
            INamedTypeSymbol named => $"{GetMetadataName(named)}{suffix}",
            _ => $"{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal)}{suffix}",
        };
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } containingType)
        {
            return $"{GetMetadataName(containingType)}+{type.MetadataName}";
        }

        var ns = type.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(ns) ? type.MetadataName : $"{ns}.{type.MetadataName}";
    }

    private static Type[] SelectedOwners =>
    [
        typeof(DbContext),
        typeof(Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker),
        typeof(RelationalDatabaseFacadeExtensions),
        typeof(RelationalQueryableExtensions),
    ];

    private const string ExpectedPartition = """
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Add(System.Object)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Add[TEntity](TEntity)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void add_SaveChangesFailed(System.EventHandler`1[Microsoft.EntityFrameworkCore.SaveChangesFailedEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void add_SavedChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.SavedChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void add_SavingChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.SavingChangesEventArgs])
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry] AddAsync(System.Object, System.Threading.CancellationToken)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity]] AddAsync[TEntity](TEntity, System.Threading.CancellationToken)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void AddRange(System.Collections.Generic.IEnumerable`1[System.Object])
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void AddRange(System.Object[])
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.Task AddRangeAsync(System.Collections.Generic.IEnumerable`1[System.Object], System.Threading.CancellationToken)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.Task AddRangeAsync(System.Object[])
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Attach(System.Object)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Attach[TEntity](TEntity)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void AttachRange(System.Collections.Generic.IEnumerable`1[System.Object])
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void AttachRange(System.Object[])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void Dispose()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask DisposeAsync()
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry(System.Object)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Entry[TEntity](TEntity)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Boolean Equals(System.Object)
Root|RuntimeTypeArgument:0:true|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Object Find(System.Type, System.Object[])
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|TEntity Find[TEntity](System.Object[])
Root|RuntimeTypeArgument:0:true|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[System.Object] FindAsync(System.Type, System.Object[], System.Threading.CancellationToken)
Root|RuntimeTypeArgument:0:true|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[System.Object] FindAsync(System.Type, System.Object[])
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[TEntity] FindAsync[TEntity](System.Object[], System.Threading.CancellationToken)
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.ValueTask`1[TEntity] FindAsync[TEntity](System.Object[])
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Linq.IQueryable`1[TResult] FromExpression[TResult](System.Linq.Expressions.Expression`1[System.Func`1[System.Linq.IQueryable`1[TResult]]])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker get_ChangeTracker()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.DbContextId get_ContextId()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade get_Database()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.Metadata.IModel get_Model()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Int32 GetHashCode()
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Remove(System.Object)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Remove[TEntity](TEntity)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void remove_SaveChangesFailed(System.EventHandler`1[Microsoft.EntityFrameworkCore.SaveChangesFailedEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void remove_SavedChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.SavedChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void remove_SavingChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.SavingChangesEventArgs])
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void RemoveRange(System.Collections.Generic.IEnumerable`1[System.Object])
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void RemoveRange(System.Object[])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Int32 SaveChanges()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Int32 SaveChanges(Boolean)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.Task`1[System.Int32] SaveChangesAsync(Boolean, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.Threading.Tasks.Task`1[System.Int32] SaveChangesAsync(System.Threading.CancellationToken)
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.DbSet`1[TEntity] Set[TEntity]()
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.DbSet`1[TEntity] Set[TEntity](System.String)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|System.String ToString()
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Update(System.Object)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity] Update[TEntity](TEntity)
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void UpdateRange(System.Collections.Generic.IEnumerable`1[System.Object])
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.DbContext|Void UpdateRange(System.Object[])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void AcceptAllChanges()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_DetectedAllChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectedChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_DetectedEntityChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectedEntityChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_DetectingAllChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_DetectingEntityChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectEntityChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_StateChanged(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityStateChangedEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_StateChanging(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityStateChangingEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_Tracked(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackedEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void add_Tracking(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackingEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void CascadeChanges()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void Clear()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void DetectChanges()
Root|AllTrackedEntities|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|System.Collections.Generic.IEnumerable`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry] Entries()
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|System.Collections.Generic.IEnumerable`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry`1[TEntity]] Entries[TEntity]()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Boolean Equals(System.Object)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Boolean get_AutoDetectChangesEnabled()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming get_CascadeDeleteTiming()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.DbContext get_Context()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.Infrastructure.DebugView get_DebugView()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming get_DeleteOrphansTiming()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Boolean get_LazyLoadingEnabled()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Microsoft.EntityFrameworkCore.QueryTrackingBehavior get_QueryTrackingBehavior()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Int32 GetHashCode()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Boolean HasChanges()
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_DetectedAllChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectedChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_DetectedEntityChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectedEntityChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_DetectingAllChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_DetectingEntityChanges(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.DetectEntityChangesEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_StateChanged(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityStateChangedEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_StateChanging(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityStateChangingEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_Tracked(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackedEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void remove_Tracking(System.EventHandler`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityTrackingEventArgs])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_AutoDetectChangesEnabled(Boolean)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_CascadeDeleteTiming(Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_DeleteOrphansTiming(Microsoft.EntityFrameworkCore.ChangeTracking.CascadeTiming)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_LazyLoadingEnabled(Boolean)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void set_QueryTrackingBehavior(Microsoft.EntityFrameworkCore.QueryTrackingBehavior)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|System.String ToString()
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void TrackGraph(System.Object, System.Action`1[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntryGraphNode])
ExistingEntityConsumer|NoEntitySource|Microsoft.EntityFrameworkCore|Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker|Void TrackGraph[TState](System.Object, TState, System.Func`2[Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntryGraphNode`1[TState],System.Boolean])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction BeginTransaction(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.IsolationLevel)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction] BeginTransactionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.IsolationLevel, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void CloseConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task CloseConnectionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Int32 ExecuteSql(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Int32 ExecuteSqlInterpolated(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlInterpolatedAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Int32 ExecuteSqlRaw(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Collections.Generic.IEnumerable`1[System.Object])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Int32 ExecuteSqlRaw(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Object[])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlRawAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Collections.Generic.IEnumerable`1[System.Object], System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlRawAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Object[])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Int32] ExecuteSqlRawAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.String GenerateCreateScript(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Collections.Generic.IEnumerable`1[System.String] GetAppliedMigrations(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Collections.Generic.IEnumerable`1[System.String]] GetAppliedMigrationsAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Nullable`1[System.Int32] GetCommandTimeout(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.String GetConnectionString(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Data.Common.DbConnection GetDbConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Collections.Generic.IEnumerable`1[System.String] GetMigrations(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Collections.Generic.IEnumerable`1[System.String] GetPendingMigrations(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[System.Collections.Generic.IEnumerable`1[System.String]] GetPendingMigrationsAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Boolean HasPendingModelChanges(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Boolean IsRelational(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void Migrate(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void Migrate(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task MigrateAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task MigrateAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void OpenConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task OpenConnectionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void SetCommandTimeout(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Nullable`1[System.Int32])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void SetCommandTimeout(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.TimeSpan)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void SetConnectionString(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Void SetDbConnection(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbConnection, Boolean)
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Linq.IQueryable`1[TResult] SqlQuery[TResult](Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.FormattableString)
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Linq.IQueryable`1[TResult] SqlQueryRaw[TResult](Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.String, System.Object[])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction UseTransaction(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbTransaction, System.Guid)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction UseTransaction(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbTransaction)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction] UseTransactionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbTransaction, System.Guid, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions|System.Threading.Tasks.Task`1[Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction] UseTransactionAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade, System.Data.Common.DbTransaction, System.Threading.CancellationToken)
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] AsSingleQuery[TEntity](System.Linq.IQueryable`1[TEntity])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] AsSplitQuery[TEntity](System.Linq.IQueryable`1[TEntity])
StateMetadataCommand|NoEntitySource|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Data.Common.DbCommand CreateDbCommand(System.Linq.IQueryable)
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] FromSql[TEntity](Microsoft.EntityFrameworkCore.DbSet`1[TEntity], System.FormattableString)
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] FromSqlInterpolated[TEntity](Microsoft.EntityFrameworkCore.DbSet`1[TEntity], System.FormattableString)
Root|ClosedGenericTypeArgument:0|Microsoft.EntityFrameworkCore.Relational|Microsoft.EntityFrameworkCore.RelationalQueryableExtensions|System.Linq.IQueryable`1[TEntity] FromSqlRaw[TEntity](Microsoft.EntityFrameworkCore.DbSet`1[TEntity], System.String, System.Object[])
""";

    internal enum ApiCategory
    {
        Root,
        ExistingEntityConsumer,
        StateMetadataCommand,
    }

    internal enum EntityEvidenceKind
    {
        NoEntitySource,
        ClosedGenericTypeArgument,
        RuntimeTypeArgument,
        AllTrackedEntities,
    }

    internal sealed record EntityEvidencePolicy(EntityEvidenceKind Kind, int Position, bool UnknownFailClosed);
    internal sealed record ApiRule(ApiCategory Category, EntityEvidencePolicy Evidence);
    private record EfApiIdentity(
        string Assembly,
        string Owner,
        string MetadataName,
        int GenericArity,
        bool IsStatic,
        string[] ParameterTypes)
    {
        public virtual bool Equals(EfApiIdentity? other) => other is not null
            && Assembly == other.Assembly
            && Owner == other.Owner
            && MetadataName == other.MetadataName
            && GenericArity == other.GenericArity
            && IsStatic == other.IsStatic
            && ParameterTypes.SequenceEqual(other.ParameterTypes, StringComparer.Ordinal);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Assembly, StringComparer.Ordinal);
            hash.Add(Owner, StringComparer.Ordinal);
            hash.Add(MetadataName, StringComparer.Ordinal);
            hash.Add(GenericArity);
            hash.Add(IsStatic);
            foreach (var parameter in ParameterTypes)
            {
                hash.Add(parameter, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
