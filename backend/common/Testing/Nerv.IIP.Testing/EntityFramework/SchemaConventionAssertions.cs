using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Nerv.IIP.Testing.EntityFramework;

public sealed record JsonColumnRule(Type EntityType, string PropertyName);

public sealed record StringKeyRule(Type EntityType, string PropertyName);

public static class SchemaConventionAssertions
{
    public static IReadOnlyList<string> BusinessTablesHaveComments(
        DbContext dbContext,
        string serviceName,
        IEnumerable<Type> businessEntityTypes)
    {
        var failures = new List<string>();
        foreach (var entityType in ResolveEntityTypes(dbContext, serviceName, businessEntityTypes))
        {
            if (string.IsNullOrWhiteSpace(entityType.GetComment()))
            {
                failures.Add($"{serviceName}: table '{FormatTable(entityType)}' mapped from '{entityType.ClrType.Name}' is missing a table comment.");
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> BusinessColumnsHaveComments(
        DbContext dbContext,
        string serviceName,
        IEnumerable<Type> businessEntityTypes)
    {
        var failures = new List<string>();
        foreach (var entityType in ResolveEntityTypes(dbContext, serviceName, businessEntityTypes))
        {
            var storeObject = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
            foreach (var property in entityType.GetProperties())
            {
                if (property.IsShadowProperty() || property.GetColumnName(storeObject) is null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(property.GetComment()))
                {
                    failures.Add($"{serviceName}: column '{FormatTable(entityType)}.{property.GetColumnName(storeObject)}' mapped from '{entityType.ClrType.Name}.{property.Name}' is missing a column comment.");
                }
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> JsonColumnsHaveCompatibilityComments(
        DbContext dbContext,
        string serviceName,
        IEnumerable<JsonColumnRule> rules)
    {
        var failures = new List<string>();
        foreach (var rule in rules)
        {
            var entityType = ResolveEntityType(dbContext, serviceName, rule.EntityType);
            var property = entityType.FindProperty(rule.PropertyName);
            if (property is null)
            {
                failures.Add($"{serviceName}: JSON rule references missing property '{rule.EntityType.Name}.{rule.PropertyName}'.");
                continue;
            }

            var comment = property.GetComment();
            var normalized = comment?.ToLowerInvariant() ?? string.Empty;
            var hasFormat = normalized.Contains("json", StringComparison.Ordinal);
            var hasProducer = normalized.Contains("producer", StringComparison.Ordinal)
                || normalized.Contains("produced", StringComparison.Ordinal);
            var hasConsumer = normalized.Contains("consumer", StringComparison.Ordinal)
                || normalized.Contains("consumed", StringComparison.Ordinal);
            var hasCompatibility = normalized.Contains("compatib", StringComparison.Ordinal);
            if (!hasFormat || !hasProducer || !hasConsumer || !hasCompatibility)
            {
                failures.Add($"{serviceName}: JSON column '{rule.EntityType.Name}.{rule.PropertyName}' comment must mention JSON format, producer, consumer and compatibility. Current comment: '{comment ?? "<missing>"}'.");
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> StringStronglyTypedKeysAreExplicit(
        DbContext dbContext,
        string serviceName,
        IEnumerable<StringKeyRule> rules)
    {
        var failures = new List<string>();
        foreach (var rule in rules)
        {
            var entityType = ResolveEntityType(dbContext, serviceName, rule.EntityType);
            var property = entityType.FindProperty(rule.PropertyName);
            if (property is null)
            {
                failures.Add($"{serviceName}: string key rule references missing property '{rule.EntityType.Name}.{rule.PropertyName}'.");
                continue;
            }

            if (property.ValueGenerated != ValueGenerated.Never)
            {
                failures.Add($"{serviceName}: string key '{rule.EntityType.Name}.{rule.PropertyName}' must use ValueGeneratedNever().");
            }

            if (property.GetMaxLength() is null or <= 0)
            {
                failures.Add($"{serviceName}: string key '{rule.EntityType.Name}.{rule.PropertyName}' must set HasMaxLength(...).");
            }
        }

        return failures;
    }

    public static IReadOnlyList<string> MigrationsHistoryTableIsInSchema(
        DbContext dbContext,
        string serviceName,
        string expectedSchema)
    {
        var options = dbContext.GetService<IDbContextOptions>();
        var relationalOptions = options.Extensions.OfType<RelationalOptionsExtension>().LastOrDefault();
        var failures = new List<string>();

        if (relationalOptions is null)
        {
            failures.Add($"{serviceName}: DbContext is missing relational options.");
            return failures;
        }

        if (!string.Equals(relationalOptions.MigrationsHistoryTableName, "__EFMigrationsHistory", StringComparison.Ordinal))
        {
            failures.Add($"{serviceName}: migrations history table must be '__EFMigrationsHistory' but was '{relationalOptions.MigrationsHistoryTableName ?? "<default>"}'.");
        }

        if (!string.Equals(relationalOptions.MigrationsHistoryTableSchema, expectedSchema, StringComparison.Ordinal))
        {
            failures.Add($"{serviceName}: migrations history schema must be '{expectedSchema}' but was '{relationalOptions.MigrationsHistoryTableSchema ?? "<default>"}'.");
        }

        return failures;
    }

    private static IEnumerable<IEntityType> ResolveEntityTypes(
        DbContext dbContext,
        string serviceName,
        IEnumerable<Type> entityTypes)
    {
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        foreach (var entityType in entityTypes)
        {
            yield return ResolveEntityType(model, serviceName, entityType);
        }
    }

    private static IEntityType ResolveEntityType(DbContext dbContext, string serviceName, Type entityType)
    {
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        return ResolveEntityType(model, serviceName, entityType);
    }

    private static IEntityType ResolveEntityType(IModel model, string serviceName, Type entityType)
    {
        return model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"{serviceName}: entity type '{entityType.FullName}' is not part of the EF model.");
    }

    private static string FormatTable(IEntityType entityType)
    {
        var schema = entityType.GetSchema();
        var table = entityType.GetTableName();
        return string.IsNullOrWhiteSpace(schema) ? table ?? entityType.ClrType.Name : $"{schema}.{table}";
    }
}
