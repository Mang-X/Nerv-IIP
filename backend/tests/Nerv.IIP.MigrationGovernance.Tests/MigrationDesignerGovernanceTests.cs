using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Nerv.IIP.MigrationGovernance.Tests;

public sealed class MigrationDesignerGovernanceTests
{
    [Fact]
    public void Every_compiled_migration_has_EF_discovery_metadata_and_target_model()
    {
        var infrastructureAssemblies = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Nerv.IIP.*.Infrastructure.dll")
            .Select(LoadAssembly)
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(infrastructureAssemblies);

        var failures = new List<string>();

        foreach (var assembly in infrastructureAssemblies)
        {
            var assemblyTypes = assembly.GetTypes();
            var migrationTypes = assemblyTypes
                .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            var designTimeFactories = assemblyTypes
                .Select(type => new
                {
                    FactoryType = type,
                    InterfaceType = type.GetInterfaces().SingleOrDefault(
                        candidate => candidate.IsGenericType
                            && candidate.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>)),
                })
                .Where(candidate => candidate.InterfaceType is not null)
                .ToArray();

            // Intentional fail-closed policy: every referenced Infrastructure assembly is migration-bearing
            // and owns one design-time DbContext. Multi-context services must split governance per context.
            if (migrationTypes.Length == 0)
            {
                failures.Add($"{assembly.GetName().Name}: no compiled Migration subclasses found.");
                continue;
            }

            if (designTimeFactories.Length != 1)
            {
                failures.Add(
                    $"{assembly.GetName().Name}: expected exactly one IDesignTimeDbContextFactory, found {designTimeFactories.Length}.");
                continue;
            }

            var factory = designTimeFactories[0];
            var dbContextType = factory.InterfaceType!.GetGenericArguments()[0];
            var declaredMigrations = new Dictionary<string, Type>(StringComparer.Ordinal);

            foreach (var migrationType in migrationTypes)
            {
                var migration = migrationType.GetCustomAttribute<MigrationAttribute>();
                if (migration is null)
                {
                    failures.Add($"{migrationType.FullName}: missing MigrationAttribute (Designer metadata).");
                }
                else if (!Regex.IsMatch(migration.Id, "^[0-9]{14}_.+$", RegexOptions.CultureInvariant))
                {
                    failures.Add($"{migrationType.FullName}: invalid EF migration id '{migration.Id}'.");
                }
                else if (!migration.Id.EndsWith($"_{migrationType.Name}", StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{migrationType.FullName}: migration id '{migration.Id}' does not match type name '{migrationType.Name}'.");
                }
                else if (!declaredMigrations.TryAdd(migration.Id, migrationType))
                {
                    failures.Add(
                        $"{assembly.GetName().Name}: duplicate migration id '{migration.Id}' on "
                        + $"{declaredMigrations[migration.Id].FullName} and {migrationType.FullName}.");
                }

                var dbContext = migrationType.GetCustomAttribute<DbContextAttribute>();
                if (dbContext is null)
                {
                    failures.Add($"{migrationType.FullName}: missing DbContextAttribute (Designer metadata).");
                }
                else if (dbContext.ContextType != dbContextType)
                {
                    failures.Add(
                        $"{migrationType.FullName}: DbContextAttribute targets {dbContext.ContextType.FullName}, "
                        + $"but the assembly design-time context is {dbContextType.FullName}.");
                }

                var targetModel = migrationType.GetMethod(
                    "BuildTargetModel",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (targetModel is null)
                {
                    failures.Add($"{migrationType.FullName}: missing declared BuildTargetModel override (Designer pairing).");
                }
            }

            try
            {
                var factoryInstance = Activator.CreateInstance(factory.FactoryType);
                var createDbContext = factory.InterfaceType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext));
                using var dbContext = Assert.IsAssignableFrom<DbContext>(
                    createDbContext!.Invoke(factoryInstance, [Array.Empty<string>()]));
                var efDiscoveredMigrationIds = dbContext.Database.GetMigrations().ToArray();

                if (!efDiscoveredMigrationIds.SequenceEqual(declaredMigrations.Keys.Order(StringComparer.Ordinal)))
                {
                    failures.Add(
                        $"{assembly.GetName().Name}: EF discovered [{string.Join(", ", efDiscoveredMigrationIds)}], "
                        + $"but compiled migration metadata declares [{string.Join(", ", declaredMigrations.Keys.Order(StringComparer.Ordinal))}].");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{assembly.GetName().Name}: EF migration discovery failed: {exception.GetBaseException().Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static Assembly LoadAssembly(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
                   assembly => string.Equals(assembly.Location, fullPath, StringComparison.OrdinalIgnoreCase))
               ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }
}
