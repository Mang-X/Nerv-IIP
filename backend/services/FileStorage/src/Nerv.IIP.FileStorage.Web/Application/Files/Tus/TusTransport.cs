using System.Net;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;

namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

internal static class TusTransport
{
    public static Task<DefaultTusConfiguration> CreateConfigurationAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var accessor = services.GetRequiredService<ILocalTusFileStoreAccessor>();
        var gate = services.GetRequiredService<IUploadSessionMutationGate>();
        var clock = services.GetRequiredService<TimeProvider>();
        UploadSessionRecord? authorized = null;
        var store = new ApplicationTusStore(db, accessor, gate, clock, context);
        context.Response.OnStarting(async () =>
        {
            if (authorized is not null && context.Response.StatusCode is 200 or 204)
                context.Response.Headers["Upload-Expires"] = authorized.ExpiresAtUtc.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (authorized is not null && context.Response.StatusCode is 409 or 413 or 460)
                context.Response.Headers["Upload-Offset"] = (await store.GetUploadOffsetAsync(
                    authorized.UploadSessionId, context.RequestAborted)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
        return Task.FromResult(new DefaultTusConfiguration
        {
            Store = store,
            AllowedExtensions = TusExtensions.All.Except(TusExtensions.ChecksumTrailer),
            Events = new Events
            {
                OnAuthorizeAsync = async authorization =>
                {
                    if (authorization.Intent == IntentType.GetOptions && accessor.TryGet(out _)) return;
                    if (!accessor.TryGet(out _) || authorization.FileId is null)
                    {
                        authorization.FailRequest(HttpStatusCode.NotFound);
                        return;
                    }

                    var organization = context.Request.Headers[FileStorageTransferHeaders.OrganizationId].ToString();
                    var environment = context.Request.Headers[FileStorageTransferHeaders.EnvironmentId].ToString();
                    authorized = await db.UploadSessions.AsNoTracking().SingleOrDefaultAsync(x =>
                        x.UploadSessionId == authorization.FileId && x.OrganizationId == organization
                        && x.EnvironmentId == environment && x.Provider == "tus"
                        && x.State == UploadSessionState.Open && !x.LegacyCompleted,
                        authorization.CancellationToken);
                    if (authorized is null)
                    {
                        authorization.FailRequest(HttpStatusCode.NotFound);
                    }
                    else if (authorization.Intent == IntentType.WriteFile && context.Request.Headers
                        .GetCommaSeparatedValues("Trailer").Contains("Upload-Checksum", StringComparer.OrdinalIgnoreCase))
                    {
                        authorization.FailRequest(HttpStatusCode.BadRequest, "Checksum trailers are not supported. Use Upload-Checksum.");
                    }
                    else if (authorized.ExpiresAtUtc <= clock.GetUtcNow())
                    {
                        await store.DeleteFileAsync(authorized.UploadSessionId, authorization.CancellationToken);
                        authorization.FailRequest(HttpStatusCode.NotFound);
                    }
                },
                OnFileCompleteAsync = async completion =>
                {
                    var result = await services.GetRequiredService<IFileStorageService>().CompleteUploadSessionAsync(
                        completion.FileId,
                        new CompleteUploadSessionRequest(authorized!.OrganizationId, authorized.EnvironmentId,
                            authorized.FilePurpose, authorized.Checksum, authorized.ExpectedSizeBytes),
                        completion.CancellationToken);
                    if (result.Error is not null)
                    {
                        throw new TusMutationRejectedException(result.StatusCode, result.Error.Message);
                    }
                }
            }
        });
    }
}

internal sealed class TusMutationRejectedException(int statusCode, string? message = null) : Exception(message ?? "上传会话无法接受当前传输操作。")
{
    public int StatusCode { get; } = statusCode;
}
