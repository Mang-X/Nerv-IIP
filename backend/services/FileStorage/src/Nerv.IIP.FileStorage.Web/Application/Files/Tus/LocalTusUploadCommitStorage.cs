namespace Nerv.IIP.FileStorage.Web.Application.Files.Tus;

/// <summary>
/// 本地 tus 部署下的提交存储：字节在 PATCH 时已由 <see cref="LocalTusFileStore"/> 落盘，下载授权内容端点
/// 也从同一处读取，因此提交阶段只负责从实际字节读回 size 与 canonical SHA-256 作为提交证据，不搬运字节。
/// 证据是否与冻结的提交意图一致由 <see cref="PostgreSqlFileStorageService"/> 判定，这里不重复比对。
/// </summary>
/// <remarks>
/// 这不是 ADR 0024 的 <c>IStorageProvider</c>：按 <c>ObjectKey</c> 定位 final、staging/final 分区与 atomic
/// promote 仍属 #994 / #1012。本类型只是把 #1628 留下的 storage seam 接到当前已在运行的本地 tus 字节面上。
/// 由于它从不改动 final，所有失败都据实报告为 ProvenNoFinalActionStarted，使会话可以重新打开并续传。
/// </remarks>
public sealed class LocalTusUploadCommitStorage(ILocalTusFileStoreAccessor accessor) : IUploadCommitStorage
{
    public async Task<UploadCommitStorageResult> CommitAsync(
        UploadCommitIntent intent,
        CancellationToken cancellationToken)
    {
        // TryGet 在 FileStorage:UploadProvider 不是 tus 时返回 false：server-proxy 部署没有本地字节面，
        // 提交因此不可能开始任何最终存储动作。
        if (!accessor.TryGet(out var store) || !store.Exists(intent.UploadSessionId))
        {
            return UploadCommitStorageResult.ProvenNoFinalActionStarted();
        }

        var sizeBytes = store.GetOffset(intent.UploadSessionId);
        var checksum = await store.ComputeSha256HexAsync(intent.UploadSessionId, cancellationToken);
        return UploadCommitStorageResult.Verified(sizeBytes, $"sha256:{checksum}");
    }
}
