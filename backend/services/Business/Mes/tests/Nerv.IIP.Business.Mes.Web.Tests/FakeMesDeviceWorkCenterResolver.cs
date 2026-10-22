using Nerv.IIP.Business.Mes.Web.Application.Planning;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 测试替身：代替 MasterData 回答「这台设备现在属于哪个工作中心」（#3878 起 MES 不再保存本地映射表）。
/// 未登记的设备返回 <c>null</c>，等同主数据里查不到；<see cref="Failure"/> 非空时每次调用都抛出，模拟主数据不可用。
/// </summary>
internal sealed class FakeMesDeviceWorkCenterResolver : IMesDeviceWorkCenterResolver
{
    private readonly Dictionary<(string? OrganizationId, string? EnvironmentId, string DeviceAssetId), string> workCenters = [];
    private int calls;

    public Exception? Failure { get; set; }

    public int Calls => Volatile.Read(ref calls);

    /// <summary>登记一条对任何组织/环境都生效的设备归属。</summary>
    public FakeMesDeviceWorkCenterResolver Map(string deviceAssetId, string workCenterId)
    {
        workCenters[(null, null, deviceAssetId)] = workCenterId;
        return this;
    }

    /// <summary>登记一条只在指定组织/环境下生效的设备归属。</summary>
    public FakeMesDeviceWorkCenterResolver Map(string organizationId, string environmentId, string deviceAssetId, string workCenterId)
    {
        workCenters[(organizationId, environmentId, deviceAssetId)] = workCenterId;
        return this;
    }

    public Task<string?> ResolveAsync(
        string organizationId,
        string environmentId,
        string deviceAssetId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref calls);
        if (Failure is not null)
        {
            throw Failure;
        }

        if (workCenters.TryGetValue((organizationId, environmentId, deviceAssetId), out var scoped))
        {
            return Task.FromResult<string?>(scoped);
        }

        return Task.FromResult(workCenters.TryGetValue((null, null, deviceAssetId), out var global) ? global : null);
    }
}
