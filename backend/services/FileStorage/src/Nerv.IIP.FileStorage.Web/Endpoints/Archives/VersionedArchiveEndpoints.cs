using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Web.Application.Archives;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.FileStorage.Web.Endpoints.Archives;

[Tags("Internal versioned archives")]
[HttpPost("/api/files/internal/v1/versioned-archives/put")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class PutVersionedArchiveEndpoint(VersionedArchiveService service)
    : Endpoint<PutVersionedArchiveRequest, VersionedArchiveEvidence>
{
    public override async Task HandleAsync(PutVersionedArchiveRequest req, CancellationToken ct) =>
        await Send.OkAsync(await service.PutAsync(req, ct), ct);
}

[Tags("Internal versioned archives")]
[HttpPost("/api/files/internal/v1/versioned-archives/get")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class GetVersionedArchiveEndpoint(VersionedArchiveService service)
    : Endpoint<GetVersionedArchiveRequest, GetVersionedArchiveResponse>
{
    public override async Task HandleAsync(GetVersionedArchiveRequest req, CancellationToken ct) =>
        await Send.OkAsync(await service.GetAsync(req, ct), ct);
}

[Tags("Internal versioned archives")]
[HttpPost("/api/files/internal/v1/versioned-archives/delete")]
[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]
public sealed class DeleteVersionedArchiveEndpoint(VersionedArchiveService service)
    : Endpoint<DeleteVersionedArchiveRequest>
{
    public override async Task HandleAsync(DeleteVersionedArchiveRequest req, CancellationToken ct)
    {
        await service.DeleteAsync(req, ct);
        await Send.NoContentAsync(ct);
    }
}
