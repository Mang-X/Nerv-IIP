using FastEndpoints;
using tusdotnet;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
builder.Services.AddHealthChecks().AddCheck<TusVolumeHealthCheck>("tus-volume");
builder.Services.AddSingleton<TusFinalReconciliationGate>();
builder.Services.AddScoped<TusFinalReconciliation>();
builder.Services.AddSingleton(TemplateAssetRetirementOptions.Load(builder.Configuration));
builder.Services.AddSingleton<TemplateAssetRetirementProof>();
builder.Services.AddScoped<TemplateAssetRetirementStore>();
// Upload-session / download-grant expiry and file retention are scheduling semantics, so the clock behind
// them is injected rather than read from DateTimeOffset.UtcNow: tests replace this registration to advance
// past a TTL without waiting. Every path that writes or reads those columns resolves this one registration
// (the storage services, the tus endpoints and PostgreSqlFileStorageGarbageCollector), so the columns are
// never driven by two clocks.
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.AddFileDownloadAuthorization();
builder.Services.AddSingleton<ILocalTusFileStoreAccessor, LocalTusFileStoreAccessor>();
builder.Services.AddSingleton<IFileStorageUploadProvider, TusUploadProvider>();
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
builder.Services.AddSingleton<UploadSessionGateRegistry>();
builder.Services.AddSingleton<IUploadSessionMutationGate, UploadSessionMutationGate>();
builder.Services.AddSingleton<S3FinalFileStore>();
builder.Services.AddHttpClient(S3FinalFileStore.ClientName)
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IUploadCommitStorage>(services =>
    services.GetRequiredService<S3FinalFileStore>().IsEnabled
        ? ActivatorUtilities.CreateInstance<S3TusUploadCommitStorage>(services)
        : ActivatorUtilities.CreateInstance<LocalTusUploadCommitStorage>(services));
builder.Services.AddScoped<UploadCommitExecutionLeaseManager>();

builder.Services.AddScoped<IFileStorageService, PostgreSqlFileStorageService>();
builder.Services.AddScoped<UploadCommitRecoveryProcessor>();
builder.Services.AddScoped<PostgreSqlFileStorageGarbageCollector>();
builder.Services.AddHostedService<UploadCommitRecoveryHostedService>();
builder.Services.AddHostedService<FileStorageGarbageCollectionHostedService>();

builder.Services.AddFileStoragePersistence(builder.Configuration, persistence.PostgreSqlConnectionStringName);
builder.Services.AddNervIipCaching(builder.Configuration, "file-storage");
builder.Services.AddNervIipObservability(builder.Configuration, "file-storage");
builder.Services.AddNervIipLocalization();

var app = builder.Build();
// 测试宿主的 ConfigureAppConfiguration 到 Build() 才生效。
_ = app.Services.GetRequiredService<S3FinalFileStore>();
var uploadProvider = app.Configuration["FileStorage:UploadProvider"] ?? TusUploadProvider.Name;
if (!string.Equals(uploadProvider, TusUploadProvider.Name, StringComparison.Ordinal))
{
    throw new InvalidOperationException("FileStorage:UploadProvider only supports tus.");
}

var tusRootPath = app.Configuration["FileStorage:Tus:RootPath"];
if (string.IsNullOrWhiteSpace(tusRootPath) || !Path.IsPathFullyQualified(tusRootPath))
{
    // tus 盘承载已 complete 文件的字节，ADR 0024 §5 要求显式、绝对、持久的 root。这里只判前两项：
    // 「持久」需要 storage identity / mount identity 探测，归 #1012，所以不在文案里宣称已经拒绝 temp。
    throw new InvalidOperationException(
        "FileStorage:UploadProvider=tus requires FileStorage:Tus:RootPath to be an explicit absolute path " +
        $"(configured={(string.IsNullOrWhiteSpace(tusRootPath) ? "<missing>" : "<relative>")}). " +
        "Point it at persistent storage.");
}

if (app.Services.GetRequiredService<ILocalTusFileStoreAccessor>().TryGet(out var localStore))
{
    localStore.VerifyVolume(required: !app.Environment.IsDevelopment());
}

if (persistence.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<FileStorageDatabaseMigrationRunner>().MigrateAsync();
}

app.UseNervIipCorrelation();
app.UseNervIipRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.GetEndpoint()?.DisplayName?.StartsWith("tus: ", StringComparison.Ordinal) == true
        && context.Request.Method == "PATCH"
        && !context.Request.Headers.ContainsKey("Tus-Resumable"))
    {
        context.Response.StatusCode = StatusCodes.Status412PreconditionFailed;
        context.Response.Headers["Tus-Resumable"] = "1.0.0";
        return;
    }
    try { await next(context); }
    catch (TusMutationRejectedException error)
    {
        context.Response.StatusCode = error.StatusCode;
        context.Response.Headers["Tus-Resumable"] = "1.0.0";
        await context.Response.WriteAsync(error.Message, context.RequestAborted);
    }
});
app.UseFastEndpoints();
app.MapHealthChecks("/health/tus-volume", new HealthCheckOptions
{
    Predicate = registration => registration.Name == "tus-volume"
});
app.MapTus("/api/files/v1/tus", TusTransport.CreateConfigurationAsync)
    .RequireAuthorization(InternalServiceAuthorizationPolicy.Name);
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
