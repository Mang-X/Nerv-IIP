using FastEndpoints;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
if (string.Equals(
        builder.Configuration["Persistence:Provider"]?.Trim(),
        "InMemory",
        StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "FileStorage does not support Persistence:Provider=InMemory. " +
        "Start FileStorage through the Aspire AppHost with PostgreSQL persistence.");
}

var persistence = PersistenceStartupGovernance.Resolve(
    builder.Configuration,
    builder.Environment,
    new PersistenceStartupRequirements("FileStorage", ["FileStorageDb", "PostgreSQL"])
    {
        NonDevelopmentMigrationRemedy =
            "Use scripts/install/migrate-file-storage.ps1 or a migration bundle outside Development."
    });
builder.Services.AddFastEndpoints();
// Upload-session / download-grant expiry and file retention are scheduling semantics, so the clock behind
// them is injected rather than read from DateTimeOffset.UtcNow: tests replace this registration to advance
// past a TTL without waiting. Every path that writes or reads those columns resolves this one registration
// (the storage services, the tus endpoints and PostgreSqlFileStorageGarbageCollector), so the columns are
// never driven by two clocks. Wall-clock audit stamps that no expiry comparison reads — the scanner's
// ScannedAtUtc — deliberately stay on DateTimeOffset.UtcNow.
builder.Services.TryAddSingleton(TimeProvider.System);
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

builder.Services.AddScoped<IFileStorageService, PostgreSqlFileStorageService>();
builder.Services.AddScoped<PostgreSqlFileStorageGarbageCollector>();
builder.Services.AddScoped<PostgreSqlFileStorageScanner>();
builder.Services.AddScoped<IFileStorageSecurityAlertSink, LoggingFileStorageSecurityAlertSink>();
builder.Services.AddHostedService<FileStorageGarbageCollectionHostedService>();
builder.Services.AddHostedService<FileStorageScanHostedService>();

builder.Services.AddFileStoragePersistence(builder.Configuration, persistence.PostgreSqlConnectionStringName);
builder.Services.AddNervIipCaching(builder.Configuration, "file-storage");
builder.Services.AddNervIipObservability(builder.Configuration, "file-storage");
builder.Services.AddNervIipLocalization();

var app = builder.Build();
if (persistence.AutoMigrate)
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
