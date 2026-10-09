using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.FileStorage.Web.Application.Files;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed partial class FileStorageTusProviderTests
{
    [Fact]
    public async Task TusUploadEndpoint_WrongPurpose_CannotReadOrMutateTheOpenSession()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var factory = CreateFactoryWithTusProvider(root);
            using var client = CreateInternalServiceClient(factory);
            var created = await CreateTusUploadSessionAsync(client, expectedSizeBytes: 10, request: CreateTextAttachmentRequest());
            var original = Encoding.UTF8.GetBytes("hello");
            await PatchTusBytesAsync(client, created.Upload.Url, 0, original);
            using var head = new HttpRequestMessage(HttpMethod.Head, created.Upload.Url);
            head.Headers.Add("Tus-Resumable", "1.0.0");
            AddDefaultTransferHeaders(head);
            head.Headers.Add(FileStorageTransferHeaders.FilePurpose, "engineering-document");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.SendAsync(head)).StatusCode);
            using var patch = CreateTusPatchRequest(created.Upload.Url, 5, Encoding.UTF8.GetBytes("world"));
            patch.Headers.Add(FileStorageTransferHeaders.FilePurpose, "engineering-document");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.SendAsync(patch)).StatusCode);
            Assert.Equal(5, GetUploadOffset(await SendTusHeadAsync(client, created.Upload.Url)));
            await using var bytes = CreateTusStore(root).OpenRead(created.UploadSessionId);
            using var copy = new MemoryStream();
            await bytes.CopyToAsync(copy);
            Assert.Equal(original, copy.ToArray());
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = await db.UploadSessions.SingleAsync(x => x.UploadSessionId == created.UploadSessionId);
            Assert.Equal(UploadSessionState.Open, session.State);
            Assert.False(await db.StoredFiles.AnyAsync(x => x.FileId == created.FileId));
        }
        finally { DeleteTempDirectory(root); }
    }

}
