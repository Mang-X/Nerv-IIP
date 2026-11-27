using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.Testing;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed partial class FileStorageTusProviderTests
{
    [Fact]
    public async Task S3_stalled_commit_readback_keeps_staging_and_allows_retry()
    {
        var root = CreateTempDirectory();
        try
        {
            using var stalled = new StalledS3Stream();
            using var s3 = new TestS3Handler { StalledGet = stalled };
            var clock = new FakeTimeProvider();
            await using var factory = new FileStorageWebApplicationFactory();
            await using var host = ConfigureStorage(factory, "s3", s3, root, clock);
            using var client = CreateInternalServiceClient(host);
            var bytes = "hello"u8.ToArray();
            var upload = await CreateTusUploadSessionAsync(client, bytes.Length, CreateTextAttachmentRequest());
            using var request = CreateTusPatchRequest(upload.Upload.Url, 0, bytes);
            using var response = await ExpireStalledReadAsync(stalled, clock, ct => client.SendAsync(request, ct));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Null(await ReadFileAsync(host, upload.FileId));
            Assert.True(CreateTusStore(root).Exists(upload.UploadSessionId));

            s3.StalledGet = null;
            using var retry = await client.PostAsJsonAsync($"/api/files/v1/upload-sessions/{upload.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-001", "prod", "attachment", null, bytes.Length));
            retry.EnsureSuccessStatusCode();
            Assert.NotNull(await ReadFileAsync(host, upload.FileId));
            Assert.False(CreateTusStore(root).Exists(upload.UploadSessionId));
            Assert.Equal(1, s3.PutCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task S3_stalled_download_before_first_byte_returns_503()
    {
        using var stalled = new StalledS3Stream();
        using var s3 = new TestS3Handler();
        var clock = new FakeTimeProvider();
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = ConfigureStorage(factory, "s3", s3, clock: clock,
            change: values => values["FileStorage:S3:ReadIdleTimeoutSeconds"] = "2");
        using var client = CreateInternalServiceClient(host);
        var bytes = "hello"u8.ToArray();
        var upload = await CreateTusUploadSessionAsync(client, bytes.Length, CreateTextAttachmentRequest());
        await PatchTusBytesAsync(client, upload.Upload.Url, 0, bytes);

        var grant = await IssueDownloadGrantAsync(client, upload.FileId);
        s3.StalledGet = stalled;
        using var response = await ExpireStalledReadAsync(stalled, clock, ct => DownloadAsync(client, grant, ct), idleSeconds: 2);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task S3_stalled_download_after_partial_bytes_aborts_response_and_disposes_stream()
    {
        var bytes = "hello-world"u8.ToArray();
        using var stalled = new StalledS3Stream("hello"u8.ToArray());
        using var s3 = new TestS3Handler();
        var clock = new FakeTimeProvider();
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = ConfigureStorage(factory, "s3", s3, clock: clock,
            change: values => values["FileStorage:S3:ReadIdleTimeoutSeconds"] = "2");
        using var client = CreateInternalServiceClient(host);
        var upload = await CreateTusUploadSessionAsync(client, bytes.Length, CreateTextAttachmentRequest());
        await PatchTusBytesAsync(client, upload.Upload.Url, 0, bytes);

        var grant = await IssueDownloadGrantAsync(client, upload.FileId);
        s3.StalledGet = stalled;

        await TestTimeout.RunAsync("expire mid-stream stalled S3 download", async ct =>
        {
            using var response = await DownloadAsync(client, grant, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var readBody = response.Content.ReadAsByteArrayAsync(ct);
            var first = await Task.WhenAny(stalled.Entered.Task, readBody).WaitAsync(ct);
            Assert.Same(stalled.Entered.Task, first);
            clock.Advance(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<Exception>(() => readBody.WaitAsync(ct));
        }, TimeSpan.FromSeconds(10));

        Assert.True(stalled.Cancelled);
        Assert.True(stalled.Disposed);
    }

    [Fact]
    public async Task S3_stalled_body_preserves_caller_cancellation()
    {
        using var stalled = new StalledS3Stream();
        using var s3 = new TestS3Handler { StalledGet = stalled };
        s3.Objects["/files-bucket/production/files/org/file"] = [1];
        await using var factory = new FileStorageWebApplicationFactory();
        await using var host = ConfigureStorage(factory, "s3", s3, clock: new FakeTimeProvider());
        var store = host.Services.GetRequiredService<S3FinalFileStore>();

        await TestTimeout.RunAsync("caller cancels stalled S3 read", async ct =>
        {
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pending = store.ReadEvidenceAsync("org/file", caller.Token);
            var first = await Task.WhenAny(stalled.Entered.Task, pending).WaitAsync(ct);
            Assert.Same(stalled.Entered.Task, first);
            await caller.CancelAsync();
            var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(ct));
            Assert.Equal(caller.Token, cancelled.CancellationToken);
            Assert.True(stalled.Cancelled);
            Assert.True(stalled.Disposed);
        }, TimeSpan.FromSeconds(10));
    }

    private static HttpResponseMessage StalledResponse(StalledS3Stream stream, int length) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(stream) { Headers = { ContentLength = length } } };

    private static async Task<T> ExpireStalledReadAsync<T>(
        StalledS3Stream stream, FakeTimeProvider clock, Func<CancellationToken, Task<T>> request, int idleSeconds = 30)
    {
        try
        {
            return await TestTimeout.RunAsync("expire stalled S3 read", async ct =>
            {
                var pending = request(ct);
                var first = await Task.WhenAny(stream.Entered.Task, pending).WaitAsync(ct);
                Assert.Same(stream.Entered.Task, first);
                clock.Advance(TimeSpan.FromSeconds(idleSeconds));
                return await pending.WaitAsync(ct);
            }, TimeSpan.FromSeconds(10));
        }
        finally
        {
            Assert.True(stream.Cancelled);
            Assert.True(stream.Disposed);
        }
    }

    private sealed class StalledS3Stream(byte[]? prefix = null) : MemoryStream
    {
        private int prefixOffset;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public bool Disposed { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (prefix is not null && prefixOffset < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - prefixOffset);
                prefix.AsMemory(prefixOffset, count).CopyTo(buffer);
                prefixOffset += count;
                return count;
            }

            Entered.TrySetResult();
            try
            {
                return await new TaskCompletionSource<int>().Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Cancelled = cancellationToken.IsCancellationRequested;
            }
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
