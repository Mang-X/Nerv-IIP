using Nerv.IIP.Caching;
using Nerv.IIP.FileStorage.Domain;
using Nerv.IIP.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNervIipCaching(builder.Configuration, "file-storage");
builder.Services.AddNervIipObservability(builder.Configuration, "file-storage");

var app = builder.Build();
app.UseNervIipCorrelation();

app.MapGet("/health", () => "Healthy");
app.MapGet("/internal/file-storage/v1/build-info", () => new { service = "FileStorage", slice = "first-iteration-skeleton" });
app.MapGet("/internal/file-storage/v1/boundaries", () => new FileStorageBoundaryResponse(
    ["FileMetadata", "UploadSession", "UploadInstruction", "DownloadGrant", "FilePurposePolicy", "scanStatus"],
    ["UploadProvider", "tus", "s3-multipart", "server-proxy", "ObjectStorageAdapter", "MinIO"]));
app.MapGet("/internal/file-storage/v1/purposes/{purpose}", (string purpose) => new { purpose, allowed = FilePurposePolicy.IsAllowed(purpose) });

app.Run();

public sealed record FileStorageBoundaryResponse(IReadOnlyList<string> DomainFacts, IReadOnlyList<string> ProviderBoundaries);

public partial class Program;
