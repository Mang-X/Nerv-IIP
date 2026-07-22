using System.Security.Cryptography;
using System.Text;
using Nerv.IIP.Contracts.FileStorage;
using Nerv.IIP.FileStorage.Web.Application.Archives;

namespace Nerv.IIP.FileStorage.Web.Tests;

public sealed class VersionedArchiveStoreTests
{
    [Fact]
    public async Task Put_requires_versioning_and_never_returns_delete_evidence_when_unavailable()
    {
        var backend = new FakeVersionedObjectStore { VersioningEnabled = false };
        var service = new VersionedArchiveService(backend, TimeProvider.System);
        var request = Request("payload");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PutAsync(request, CancellationToken.None));
        Assert.Equal(0, backend.PutCount);
    }

    [Fact]
    public async Task Put_reads_the_exact_version_back_and_returns_complete_evidence()
    {
        var backend = new FakeVersionedObjectStore();
        var service = new VersionedArchiveService(backend, TimeProvider.System);
        var request = Request("payload");

        var evidence = await service.PutAsync(request, CancellationToken.None);

        Assert.Equal("version-1", evidence.VersionId);
        Assert.Equal(request.Sha256, evidence.Sha256);
        Assert.Equal(Encoding.UTF8.GetByteCount("payload"), evidence.SizeBytes);
        Assert.Equal(evidence.ObjectKey, backend.LastReadObjectKey);
        Assert.Equal(evidence.VersionId, backend.LastReadVersionId);
    }

    [Fact]
    public async Task Read_back_checksum_mismatch_fails_without_complete_evidence()
    {
        var backend = new FakeVersionedObjectStore { ReadOverride = Encoding.UTF8.GetBytes("corrupt") };
        var service = new VersionedArchiveService(backend, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PutAsync(Request("payload"), CancellationToken.None));
    }

    [Fact]
    public async Task Multipart_sized_archive_is_rejected_before_object_storage_is_called()
    {
        var backend = new FakeVersionedObjectStore();
        var service = new VersionedArchiveService(backend, TimeProvider.System);
        var content = new string('x', VersionedArchiveLimits.MaximumConditionallyWritableBytes + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => service.PutAsync(Request(content), CancellationToken.None));

        Assert.Equal(0, backend.PutCount);
    }

    [Fact]
    public async Task Legal_hold_is_applied_to_the_exact_archived_version()
    {
        var backend = new FakeVersionedObjectStore();
        var service = new VersionedArchiveService(backend, TimeProvider.System);

        var evidence = await service.PutAsync(Request("payload") with { LegalHold = true }, CancellationToken.None);

        Assert.Equal((evidence.ObjectKey, evidence.VersionId), backend.LastLegalHold);
    }

    private static PutVersionedArchiveRequest Request(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new PutVersionedArchiveRequest(
            "org-001", "prod", "order-urgency", "batch-001", Convert.ToBase64String(bytes),
            "application/json", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), false);
    }

    private sealed class FakeVersionedObjectStore : IVersionedObjectStore
    {
        private byte[] stored = [];
        public bool VersioningEnabled { get; init; } = true;
        public byte[]? ReadOverride { get; init; }
        public int PutCount { get; private set; }
        public string? LastReadObjectKey { get; private set; }
        public string? LastReadVersionId { get; private set; }
        public (string ObjectKey, string VersionId)? LastLegalHold { get; private set; }

        public Task<bool> IsVersioningEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(VersioningEnabled);

        public Task<string> PutAsync(string objectKey, ReadOnlyMemory<byte> content, string contentType, string sha256, CancellationToken cancellationToken)
        {
            PutCount++;
            stored = content.ToArray();
            return Task.FromResult("version-1");
        }

        public Task<byte[]> GetAsync(string objectKey, string versionId, CancellationToken cancellationToken)
        {
            LastReadObjectKey = objectKey;
            LastReadVersionId = versionId;
            return Task.FromResult(ReadOverride ?? stored);
        }

        public Task SetLegalHoldAsync(string objectKey, string versionId, CancellationToken cancellationToken)
        {
            LastLegalHold = (objectKey, versionId);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string objectKey, string versionId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
