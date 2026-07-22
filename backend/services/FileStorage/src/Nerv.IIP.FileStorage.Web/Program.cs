using FastEndpoints;
using Nerv.IIP.Caching;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Web;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;
using Nerv.IIP.Localization;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;

var builder = WebApplication.CreateBuilder(args);
var persistence = FileStoragePersistenceStartup.Validate(builder.Configuration, builder.Environment);
var usePostgreSql = persistence.UsePostgreSql;
builder.Services.AddFastEndpoints();
builder.Services.AddNervIipInternalServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<ILocalTusFileStoreAccessor, LocalTusFileStoreAccessor>();
builder.Services.AddSingleton<IFileStorageUploadProvider>(services =>
    string.Equals(services.GetRequiredService<IConfiguration>()["FileStorage:UploadProvider"], "tus", StringComparison.OrdinalIgnoreCase)
        ? new TusUploadProvider()
        : new ServerProxyUploadProvider());

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

builder.Services.AddFileStoragePersistence(builder.Configuration, persistence.UsePostgreSql);
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
