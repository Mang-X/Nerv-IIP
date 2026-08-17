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
var persistence = PersistenceStartupGovernance.Resolve(
    builder.Configuration,
    builder.Environment,
    new PersistenceStartupRequirements("FileStorage", ["FileStorageDb", "PostgreSQL"])
    {
        NonDevelopmentMigrationRemedy =
            "Use scripts/install/migrate-file-storage.ps1 or a migration bundle outside Development."
    });
var usePostgreSql = persistence.UsePostgreSql;
var storageProvider = builder.Configuration["Storage:Provider"]?.Trim();
var minioEndpoint = builder.Configuration["Storage:MinIO:Endpoint"];
var minioAccessKey = builder.Configuration["Storage:MinIO:AccessKey"];
var minioSecretKey = builder.Configuration["Storage:MinIO:SecretKey"];
var minioComplianceArchiveBucket = builder.Configuration["Storage:MinIO:ComplianceArchiveBucket"];
var hasValidMinioEndpoint = TryParseMinioEndpoint(minioEndpoint, out var minioUri);
var hasValidMinioComplianceArchiveBucket = IsValidBucketName(minioComplianceArchiveBucket);
var hasStorageConfiguration = !string.IsNullOrWhiteSpace(storageProvider) ||
    !string.IsNullOrWhiteSpace(minioEndpoint) ||
    !string.IsNullOrWhiteSpace(minioAccessKey) ||
    !string.IsNullOrWhiteSpace(minioSecretKey) ||
    !string.IsNullOrWhiteSpace(minioComplianceArchiveBucket);
var useMinio = hasStorageConfiguration &&
    string.Equals(storageProvider, "MinIO", StringComparison.OrdinalIgnoreCase) &&
    hasValidMinioEndpoint &&
    !string.IsNullOrWhiteSpace(minioAccessKey) &&
    !string.IsNullOrWhiteSpace(minioSecretKey) &&
    hasValidMinioComplianceArchiveBucket;

if (hasStorageConfiguration && !useMinio)
{
    throw new InvalidOperationException(
        "FileStorage versioned object storage configuration is invalid: " +
        $"provider={(string.IsNullOrWhiteSpace(storageProvider) ? "<missing>" : storageProvider)}, " +
        $"endpointConfigured={!string.IsNullOrWhiteSpace(minioEndpoint)}, " +
        $"endpointValid={hasValidMinioEndpoint}, " +
        $"accessKeyConfigured={!string.IsNullOrWhiteSpace(minioAccessKey)}, " +
        $"secretKeyConfigured={!string.IsNullOrWhiteSpace(minioSecretKey)}, " +
        $"complianceArchiveBucketConfigured={!string.IsNullOrWhiteSpace(minioComplianceArchiveBucket)}, " +
        $"complianceArchiveBucketValid={hasValidMinioComplianceArchiveBucket}. " +
        "Set Storage:Provider=MinIO and configure Storage:MinIO:Endpoint, AccessKey, SecretKey, and " +
        "ComplianceArchiveBucket together, or remove the entire Storage section when versioned archive storage is not used.");
}

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
builder.Services.AddSingleton<IVersionedObjectStore>(_ =>
{
    if (!useMinio)
    {
        return new UnavailableVersionedObjectStore();
    }

    var client = new MinioClient()
        .WithEndpoint(minioUri!.Host, minioUri.Port)
        .WithCredentials(minioAccessKey, minioSecretKey)
        .WithSSL(string.Equals(minioUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        .Build();
    return new MinioVersionedObjectStore(client, minioComplianceArchiveBucket!);
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

static bool TryParseMinioEndpoint(string? value, out Uri? endpoint)
{
    endpoint = null;
    if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
        (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps) ||
        string.IsNullOrWhiteSpace(candidate.Host) ||
        !string.IsNullOrEmpty(candidate.UserInfo) ||
        !string.IsNullOrEmpty(candidate.Query) ||
        !string.IsNullOrEmpty(candidate.Fragment) ||
        (candidate.AbsolutePath != "/" && candidate.AbsolutePath.Length != 0))
    {
        return false;
    }

    endpoint = candidate;
    return true;
}

static bool IsValidBucketName(string? value)
{
    if (value is null || value.Length is < 3 or > 63 ||
        !IsAsciiLetterOrDigit(value[0]) ||
        !IsAsciiLetterOrDigit(value[^1]) ||
        value.Contains("..", StringComparison.Ordinal) ||
        value.Contains(".-", StringComparison.Ordinal) ||
        value.Contains("-.", StringComparison.Ordinal) ||
        System.Net.IPAddress.TryParse(value, out _))
    {
        return false;
    }

    return value.All(character =>
        IsAsciiLetterOrDigit(character) || character is '-' or '.');

    static bool IsAsciiLetterOrDigit(char character)
    {
        return character is >= 'a' and <= 'z' or >= '0' and <= '9';
    }
}

public partial class Program;
