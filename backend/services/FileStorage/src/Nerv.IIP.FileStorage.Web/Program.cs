using FastEndpoints;
using Minio;
using Nerv.IIP.Caching;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;
using Nerv.IIP.FileStorage.Web.Application.Archives;
using Nerv.IIP.Localization;
using Nerv.IIP.Observability;
using Nerv.IIP.Persistence;
using Nerv.IIP.ServiceAuth;

var builder = WebApplication.CreateBuilder(args);
var persistence = PersistenceStartupGovernance.Resolve(
    builder.Configuration,
    builder.Environment,
    new PersistenceStartupRequirements("FileStorage", ["FileStorageDb", "PostgreSQL"])
    {
        NonDevelopmentMigrationRemedy =
            "Use scripts/install/migrate-file-storage.ps1 or a migration bundle outside Development."
    });
var usePostgreSql = persistence.UsePostgreSql;
builder.Services.AddFastEndpoints();
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<ILocalTusFileStoreAccessor, LocalTusFileStoreAccessor>();
builder.Services.AddSingleton<IFileStorageUploadProvider>(services =>
    string.Equals(services.GetRequiredService<IConfiguration>()["FileStorage:UploadProvider"], "tus", StringComparison.OrdinalIgnoreCase)
        ? new TusUploadProvider()
        : new ServerProxyUploadProvider());
builder.Services.AddSingleton<IVersionedObjectStore>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var endpoint = configuration["Storage:MinIO:Endpoint"];
    var accessKey = configuration["Storage:MinIO:AccessKey"];
    var secretKey = configuration["Storage:MinIO:SecretKey"];
    var bucket = configuration["Storage:MinIO:ComplianceArchiveBucket"];
    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
        string.IsNullOrWhiteSpace(accessKey) ||
        string.IsNullOrWhiteSpace(secretKey) ||
        string.IsNullOrWhiteSpace(bucket))
    {
        return new UnavailableVersionedObjectStore();
    }

    var client = new MinioClient()
        .WithEndpoint(uri.Host, uri.Port)
        .WithCredentials(accessKey, secretKey)
        .WithSSL(string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        .Build();
    return new MinioVersionedObjectStore(client, bucket);
});
builder.Services.AddSingleton<VersionedArchiveService>();

if (usePostgreSql)
{
    builder.Services.AddScoped<IFileStorageService, PostgreSqlFileStorageService>();
    builder.Services.AddScoped<PostgreSqlFileStorageGarbageCollector>();
    builder.Services.AddScoped<PostgreSqlFileStorageScanner>();
    builder.Services.AddScoped<IFileStorageSecurityAlertSink, LoggingFileStorageSecurityAlertSink>();
    builder.Services.AddHostedService<FileStorageGarbageCollectionHostedService>();
    builder.Services.AddHostedService<FileStorageScanHostedService>();
}
else
{
    builder.Services.AddSingleton<IFileStorageService, InMemoryFileStorageService>();
}

builder.Services.AddFileStoragePersistence(builder.Configuration, persistence.PostgreSqlConnectionStringName);
builder.Services.AddNervIipCaching(builder.Configuration, "file-storage");
builder.Services.AddNervIipObservability(builder.Configuration, "file-storage");
builder.Services.AddNervIipLocalization();

var app = builder.Build();
if (usePostgreSql && persistence.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<FileStorageDatabaseMigrationRunner>().MigrateAsync();
}

app.UseNervIipCorrelation();
app.UseNervIipRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();
app.UseFastEndpoints();
app.Run();

public partial class Program;
