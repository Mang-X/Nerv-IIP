using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.Testing.EntityFramework;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class FileStorageSchemaConventionTests
{
    [Fact]
    public void FileStorage_schema_metadata_follows_database_conventions()
    {
        using var fixture = CreateFixture();
        var businessEntities = new[]
        {
            typeof(StoredFileRecord),
            typeof(UploadSessionRecord),
            typeof(DownloadGrantRecord),
        };

        var stringKeys = new[]
        {
            new StringKeyRule(typeof(StoredFileRecord), nameof(StoredFileRecord.FileId)),
            new StringKeyRule(typeof(StoredFileRecord), nameof(StoredFileRecord.ObjectKey)),
            new StringKeyRule(typeof(UploadSessionRecord), nameof(UploadSessionRecord.UploadSessionId)),
            new StringKeyRule(typeof(UploadSessionRecord), nameof(UploadSessionRecord.FileId)),
            new StringKeyRule(typeof(UploadSessionRecord), nameof(UploadSessionRecord.ObjectKey)),
            new StringKeyRule(typeof(DownloadGrantRecord), nameof(DownloadGrantRecord.DownloadGrantId)),
            new StringKeyRule(typeof(DownloadGrantRecord), nameof(DownloadGrantRecord.FileId)),
        };

        var failures = new List<string>();
        failures.AddRange(SchemaConventionAssertions.BusinessTablesHaveComments(fixture.DbContext, "FileStorage", businessEntities));
        failures.AddRange(SchemaConventionAssertions.BusinessColumnsHaveComments(fixture.DbContext, "FileStorage", businessEntities));
        failures.AddRange(SchemaConventionAssertions.StringStronglyTypedKeysAreExplicit(fixture.DbContext, "FileStorage", stringKeys));
        failures.AddRange(SchemaConventionAssertions.MigrationsHistoryTableIsInSchema(fixture.DbContext, "FileStorage", "filestorage"));
        failures.AddRange(AssertTablesUseFileStorageSchema(fixture.DbContext, businessEntities));
        failures.AddRange(AssertExpectedBusinessTablesExist(fixture.DbContext));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IReadOnlyList<string> AssertTablesUseFileStorageSchema(
        ApplicationDbContext dbContext,
        IEnumerable<Type> businessEntities)
    {
        return businessEntities
            .Select(type => dbContext.Model.FindEntityType(type))
            .OfType<Microsoft.EntityFrameworkCore.Metadata.IEntityType>()
            .Where(entityType => !string.Equals(entityType.GetSchema(), "filestorage", StringComparison.Ordinal))
            .Select(entityType => $"FileStorage: table '{entityType.GetTableName()}' must use schema 'filestorage' but used '{entityType.GetSchema() ?? "<default>"}'.")
            .ToArray();
    }

    private static IReadOnlyList<string> AssertExpectedBusinessTablesExist(ApplicationDbContext dbContext)
    {
        var expectedTables = new[] { "stored_files", "upload_sessions", "download_grants" };
        var actualTables = dbContext.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .ToHashSet(StringComparer.Ordinal);

        return expectedTables
            .Where(expectedTable => !actualTables.Contains(expectedTable))
            .Select(expectedTable => $"FileStorage: expected business table '{expectedTable}' is missing from the EF model.")
            .ToArray();
    }

    private static SchemaFixture CreateFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "PostgreSQL",
                ["ConnectionStrings:FileStorageDb"] = "Host=localhost;Database=nerv_iip_schema_conventions;Username=nerv;Password=nerv",
            })
            .Build();
        services.AddFileStoragePersistence(configuration);

        return new SchemaFixture(services.BuildServiceProvider());
    }

    private sealed class SchemaFixture : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;

        public SchemaFixture(ServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            scope = serviceProvider.CreateScope();
            DbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ApplicationDbContext DbContext { get; }

        public void Dispose()
        {
            DbContext.Dispose();
            scope.Dispose();
            serviceProvider.Dispose();
        }
    }
}
