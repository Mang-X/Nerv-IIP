using System.Globalization;
using Microsoft.AspNetCore.Builder;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.FileStorage.Web.Application.Files.Tus;
using Nerv.IIP.Testing;
using static Nerv.IIP.FileStorage.Web.Tests.TemplateAssetRetirementProofTests;
using Npgsql;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.ServiceAuth;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.FileStorage.Web.Application.Files.UploadProviders;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed partial class FileStorageRestartPersistenceTests
{
    private static async Task Verify_Tus_bytes_offset_and_canonical_checksum_survive_host_restart()
    {
        var root = Directory.CreateTempSubdirectory("nerv-tus-999-");
        var payload = Encoding.UTF8.GetBytes(new string('x', 8 * 1024 * 1024));
        CreateUploadSessionResponse created;
        try
        {
            await using (var factory = TusFactory(root.FullName, autoMigrate: true))
            {
                using var client = CreateClient(factory);
                created = await CreateTusSession(client, payload.Length);
                using var partial = TusPatch(created.Upload.Url, 0, payload[..1024]);
                Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(partial)).StatusCode);
            }
            var observation = new StreamingObservation(root.FullName);
            await using (var factory = TusFactory(root.FullName, autoMigrate: false).WithWebHostBuilder(builder =>
                builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(observation))))
            {
                using var client = CreateClient(factory);
                using var head = new HttpRequestMessage(HttpMethod.Head, created.Upload.Url);
                SetTusHeaders(head, 0);
                var offset = await client.SendAsync(head);
                Assert.Equal("1024", Assert.Single(offset.Headers.GetValues("Upload-Offset")));
                using var remaining = TusPatch(created.Upload.Url, 1024, payload[1024..]);
                Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(remaining)).StatusCode);
                Assert.True(observation.SawIncrementalWrite, "Bytes must reach disk before the whole chunk is read.");
                Assert.InRange(observation.MaximumRead, 1, 64 * 1024);
                var metadata = await client.GetFromJsonAsync<FileMetadataResponse>($"/api/files/v1/files/{created.FileId}");
                Assert.Equal($"sha256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant()}", metadata!.Checksum);
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                Assert.Equal(UploadSessionState.Completed, (await db.UploadSessions.SingleAsync(x => x.UploadSessionId == created.UploadSessionId)).State);
                var grantResponse = await client.PostAsJsonAsync($"/api/files/v1/files/{created.FileId}/download-grants", new CreateDownloadGrantRequest("org-tus", "production"));
                var grant = (await grantResponse.Content.ReadFromJsonAsync<DownloadGrantResponse>())!;
                using var download = new HttpRequestMessage(HttpMethod.Get, grant.Download.Url);
                foreach (var header in grant.Download.Headers) download.Headers.TryAddWithoutValidation(header.Key, header.Value);
                Assert.Equal(payload, await (await client.SendAsync(download)).Content.ReadAsByteArrayAsync());
            }
        }
        finally { root.Delete(recursive: true); }
    }

    private static async Task Verify_Tus_admitted_patch_is_rejected_after_complete_commits_intent()
    {
        var root = Directory.CreateTempSubdirectory("nerv-tus-999-race-");
        var barrier = new TransportMutationBarrier(beforeGate: true);
        var storage = new TransportCommitBarrier();
        try
        {
            await using var factory = TusFactory(root.FullName, true, barrier, storage);
            using var client = CreateClient(factory);
            var created = await CreateTusSession(client, 10);
            using (var scope = factory.Services.CreateScope())
            {
                Assert.True(scope.ServiceProvider.GetRequiredService<ILocalTusFileStoreAccessor>().TryGet(out var bytes));
                await bytes.AppendAsync(created.UploadSessionId, 0, new MemoryStream(Encoding.UTF8.GetBytes("hello")), default);
            }
            using var patch = TusPatch(created.Upload.Url, 5, Encoding.UTF8.GetBytes("world"));
            var writing = client.SendAsync(patch);
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using (var scope = factory.Services.CreateScope())
            {
                Assert.True(scope.ServiceProvider.GetRequiredService<ILocalTusFileStoreAccessor>().TryGet(out var bytes));
                await bytes.AppendAsync(created.UploadSessionId, 5, new MemoryStream(Encoding.UTF8.GetBytes("world")), default);
            }
            var completion = client.PostAsJsonAsync($"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-tus", "production", "attachment", null, 10));
            await storage.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using (var scope = factory.Services.CreateScope())
            {
                Assert.Equal(UploadSessionState.Committing, (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .UploadSessions.SingleAsync(x => x.UploadSessionId == created.UploadSessionId)).State);
            }
            // Align physical offset with the admitted request while commit storage is paused.
            // This controlled fixture excludes offset conflict as a substitute for rejecting durable committing.
            using (var offsetScope = factory.Services.CreateScope())
            {
                Assert.True(offsetScope.ServiceProvider.GetRequiredService<ILocalTusFileStoreAccessor>().TryGet(out var bytes));
                await using var file = bytes.OpenWrite(created.UploadSessionId);
                file.SetLength(5);
                file.Flush(flushToDisk: true);
            }
            barrier.Release.TrySetResult();
            await Task.WhenAny(writing, barrier.MutationEntered.Task).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(barrier.MutationEntered.Task.IsCompleted, "A durable committing session must reject PATCH before its mutation callback.");
            Assert.Equal(HttpStatusCode.Conflict, (await writing).StatusCode);
            Assert.Equal(Encoding.UTF8.GetBytes("hello"), await ReadTusBytes(factory, created.UploadSessionId));
            // Restore the complete payload before allowing the real commit storage to read it.
            using (var restoreScope = factory.Services.CreateScope())
            {
                Assert.True(restoreScope.ServiceProvider.GetRequiredService<ILocalTusFileStoreAccessor>().TryGet(out var bytes));
                await bytes.AppendAsync(created.UploadSessionId, 5, new MemoryStream(Encoding.UTF8.GetBytes("world")), default);
            }
            storage.Release.TrySetResult();
            Assert.Equal(HttpStatusCode.OK, (await completion).StatusCode);
            Assert.Equal(Encoding.UTF8.GetBytes("helloworld"), await ReadTusBytes(factory, created.UploadSessionId));
        }
        finally { barrier.Release.TrySetResult(); storage.Release.TrySetResult(); root.Delete(recursive: true); }
    }

    private static async Task Verify_Tus_concurrent_patches_preserve_offset_and_complete_drains_mutation()
    {
        var root = Directory.CreateTempSubdirectory("nerv-tus-999-drain-");
        var barrier = new TransportMutationBarrier(beforeGate: false);
        try
        {
            await using var factory = TusFactory(root.FullName, true, barrier);
            using var client = CreateClient(factory);
            var created = await CreateTusSession(client, 10);
            using var patch = TusPatch(created.Upload.Url, 0, Encoding.UTF8.GetBytes("hello"));
            var first = client.SendAsync(patch);
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var duplicate = TusPatch(created.Upload.Url, 0, Encoding.UTF8.GetBytes("other"));
            Assert.Equal(HttpStatusCode.Locked, (await client.SendAsync(duplicate)).StatusCode);
            barrier.Release.TrySetResult();
            Assert.Equal(HttpStatusCode.NoContent, (await first).StatusCode);
            Assert.Equal(Encoding.UTF8.GetBytes("hello"), await ReadTusBytes(factory, created.UploadSessionId));

            barrier.Reset();
            using var last = TusPatch(created.Upload.Url, 5, Encoding.UTF8.GetBytes("world"));
            var writing = client.SendAsync(last);
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using (var probeScope = factory.Services.CreateScope())
            using (var cancellation = new CancellationTokenSource())
            {
                var registry = probeScope.ServiceProvider.GetRequiredService<UploadSessionGateRegistry>();
                var probe = registry.EnterPatchCommitAsync(created.UploadSessionId, cancellation.Token).AsTask();
                if (probe.IsCompletedSuccessfully)
                {
                    await using var unexpectedLease = await probe;
                    Assert.Fail("Active PATCH must retain the application's PATCH/complete gate.");
                }
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
            }
            var completion = client.PostAsJsonAsync($"/api/files/v1/upload-sessions/{created.UploadSessionId}/complete",
                new CompleteUploadSessionRequest("org-tus", "production", "attachment", null, 10));
            var completionRegistry = factory.Services.GetRequiredService<UploadSessionGateRegistry>();
            await Eventually.AssertAsync("complete reaches shared gate while HTTP PATCH is active", _ =>
            {
                Assert.True(completion.IsCompleted || ReadPatchGateUsers(completionRegistry, created.UploadSessionId) == 2);
                return Task.CompletedTask;
            }, new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20), []));
            Assert.False(completion.IsCompleted, "Complete must wait for the active PATCH, rather than validate partial bytes.");
            using (var stateScope = factory.Services.CreateScope())
                Assert.Equal(UploadSessionState.Open, (await stateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .UploadSessions.SingleAsync(x => x.UploadSessionId == created.UploadSessionId)).State);
            barrier.Release.TrySetResult();
            Assert.Equal(HttpStatusCode.NoContent, (await writing).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await completion).StatusCode);
            Assert.Equal(Encoding.UTF8.GetBytes("helloworld"), await ReadTusBytes(factory, created.UploadSessionId));
            using var scope = factory.Services.CreateScope();
            Assert.Single(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().StoredFiles
                .Where(x => x.FileId == created.FileId).ToArrayAsync());
        }
        finally { barrier.Release.TrySetResult(); root.Delete(recursive: true); }
    }

    // Synchronize on the actual semaphore's registered users, not an HTTP dispatch or elapsed-time guess.
    // Reflection is confined to this local test observation; the production registry has no test hook.
    private static int ReadPatchGateUsers(UploadSessionGateRegistry registry, string session)
    {
        var gates = (System.Collections.IDictionary)typeof(UploadSessionGateRegistry)
            .GetField("gates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(registry)!;
        var entry = gates[$"patch:{session}"]!;
        lock (entry) return (int)entry.GetType().GetProperty("Users")!.GetValue(entry)!;
    }

    private static WebApplicationFactory<Program> TusFactory(string root, bool autoMigrate,
        TransportMutationBarrier? mutation = null, TransportCommitBarrier? commit = null) =>
        new FileStorageUnconfiguredWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Persistence:Provider", "PostgreSQL");
            builder.UseSetting("Persistence:AutoMigrate", autoMigrate.ToString());
            builder.UseSetting("ConnectionStrings:FileStorageDb", LaneConnectionString);
            builder.UseSetting("FileStorage:UploadProvider", "tus");
            builder.UseSetting("FileStorage:Tus:RootPath", root);
            builder.ConfigureServices(services =>
            {
                if (mutation is not null) services.AddSingleton<IUploadSessionMutationGate>(provider =>
                    mutation.Initialize(new UploadSessionMutationGate(provider.GetRequiredService<IServiceScopeFactory>(),
                        provider.GetRequiredService<UploadSessionGateRegistry>())));
                if (commit is not null) services.AddSingleton<IUploadCommitStorage>(provider =>
                    commit.Initialize(new LocalTusUploadCommitStorage(provider.GetRequiredService<ILocalTusFileStoreAccessor>())));
            });
        });

    private static async Task<CreateUploadSessionResponse> CreateTusSession(HttpClient client, long size)
    {
        var response = await client.PostAsJsonAsync("/api/files/v1/upload-sessions", new CreateUploadSessionRequest(
            "org-tus", "production", new OwnerReference("test", "attachment", "999"), "attachment", "payload.txt", "text/plain", size, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateUploadSessionResponse>())!;
    }

    private static HttpRequestMessage TusPatch(string url, long offset, byte[] bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new("application/offset+octet-stream");
        SetTusHeaders(request, offset);
        return request;
    }

    private static void SetTusHeaders(HttpRequestMessage request, long offset)
    {
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Offset", offset.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(FileStorageTransferHeaders.OrganizationId, "org-tus");
        request.Headers.Add(FileStorageTransferHeaders.EnvironmentId, "production");
    }

    private static async Task<byte[]> ReadTusBytes(WebApplicationFactory<Program> factory, string session)
    {
        using var scope = factory.Services.CreateScope();
        Assert.True(scope.ServiceProvider.GetRequiredService<ILocalTusFileStoreAccessor>().TryGet(out var bytes));
        await using var stream = bytes.OpenRead(session);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private sealed class TransportMutationBarrier(bool beforeGate) : IUploadSessionMutationGate
    {
        private IUploadSessionMutationGate inner = null!;
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MutationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Reset() { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public TransportMutationBarrier Initialize(IUploadSessionMutationGate value) { inner = value; return this; }
        public async Task<UploadSessionMutationResult> ExecutePatchMutationAsync(string id, Func<CancellationToken, Task> mutation, CancellationToken token)
        {
            async Task Pause() { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            if (beforeGate) await Pause();
            return await inner.ExecutePatchMutationAsync(id, async ct =>
            {
                if (!beforeGate) await Pause();
                MutationEntered.TrySetResult();
                await mutation(ct);
            }, token);
        }
    }

    private sealed class TransportCommitBarrier : IUploadCommitStorage
    {
        private IUploadCommitStorage inner = null!;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TransportCommitBarrier Initialize(IUploadCommitStorage value) { inner = value; return this; }
        public async Task<UploadCommitStorageResult> CommitAsync(UploadCommitIntent intent, CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            return await inner.CommitAsync(intent, token);
        }
    }

    private sealed class StreamingObservation(string root) : IStartupFilter
    {
        public bool SawIncrementalWrite { get; set; }
        public int MaximumRead { get; set; }
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (context.Request.Method == "PATCH")
                {
                    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:Tus:RootPath"] = root }).Build();
                    var store = new LocalUploadByteStore(config);
                    var id = context.Request.Path.Value!.Split('/')[^1];
                    var offset = store.GetOffset(id);
                    context.Request.Body = new ObservedReadStream(context.Request.Body, count =>
                    {
                        MaximumRead = Math.Max(MaximumRead, count);
                        SawIncrementalWrite |= store.GetOffset(id) > offset;
                    });
                }
                await following(context);
            });
            next(app);
        };
    }

    private sealed class ObservedReadStream(Stream inner, Action<int> observe) : Stream
    {
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { observe(count); return await inner.ReadAsync(buffer, offset, count, token); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

}
