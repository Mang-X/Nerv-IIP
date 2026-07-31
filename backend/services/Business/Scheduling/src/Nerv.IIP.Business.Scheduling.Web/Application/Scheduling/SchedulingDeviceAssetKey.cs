namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

/// <summary>
/// 排程世界的设备标识口径(#1320)。
///
/// 两侧的标识体系:
/// <list type="bullet">
///   <item><description>
///     MasterData 设备台账:聚合主键是 <c>DeviceAssetId</c>(GUID),业务标识是 <c>Code</c>
///     (形如 <c>DEV-CNC-01</c>)。读面两个字段都给。
///   </description></item>
///   <item><description>
///     IIoT / 维护世界:<c>DeviceStateSnapshot.DeviceAssetId</c>、<c>AlarmEvent.DeviceAssetId</c>、
///     采集点位、维护计划一律用**业务编码字符串**,不持有 MasterData 的 GUID 主键。
///   </description></item>
/// </list>
///
/// 因此两侧唯一共同持有的 join 键是业务编码。排程问题的 resourceId / eligibleResourceIds
/// 必须落在这个键上,可用性查询才能命中快照;此前取了 GUID,导致所有设备查无快照、
/// 全部回落成「状态未知」并被当成硬不可用。
/// </summary>
public static class SchedulingDeviceAssetKey
{
    /// <param name="code">MasterData 设备业务编码(权威 join 键)。</param>
    /// <param name="deviceAssetId">MasterData 聚合主键 GUID;仅在编码缺失时兜底,保证不产出空标识。</param>
    public static string Resolve(string? code, string? deviceAssetId)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code.Trim();
        }

        return string.IsNullOrWhiteSpace(deviceAssetId) ? string.Empty : deviceAssetId.Trim();
    }
}
