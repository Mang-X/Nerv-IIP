using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

/// <summary>
/// 设备运行时可用性 → 排程问题的适配。
///
/// 口径(与「齐套是开工门槛不是排产门槛」同族):**「不知道」不等于「不可用」**。
/// <list type="bullet">
///   <item><description>
///     <see cref="EquipmentRuntimeAvailabilityStatus.Unavailable"/>(真实停机、活动报警、维护窗口)
///     → 硬不可用窗口,照旧阻断排程。
///   </description></item>
///   <item><description>
///     <see cref="EquipmentRuntimeAvailabilityStatus.Unknown"/>(无快照 / 快照过期 / 采集源不可达)
///     → 设备数据风险,可排 + 标记。软约束以前会把这类窗口当硬不可用,叠加上游全窗口
///     [horizonStart, horizonEnd] 的兜底窗口,结果是整台设备被排除、方案 0 已排、发布守卫拒绝
///     (#1320)。
///   </description></item>
/// </list>
/// </summary>
public static class EquipmentAvailabilitySchedulingAdapter
{
    public static IReadOnlyCollection<SchedulingUnavailabilityWindowContract> ToUnavailabilityWindows(
        EquipmentRuntimeAvailabilityResponse availability,
        SchedulingEquipmentUnknownModeContract unknownMode = SchedulingEquipmentUnknownModeContract.Soft)
    {
        ArgumentNullException.ThrowIfNull(availability);

        return availability.Items
            .Where(x => IsHardBlocking(x.AvailabilityStatus, unknownMode))
            .Where(x => x.EndUtc > x.StartUtc)
            .Select(x => new SchedulingUnavailabilityWindowContract(
                ResourceId: x.DeviceAssetId,
                WorkCenterId: x.WorkCenterId,
                StartUtc: x.StartUtc,
                EndUtc: x.EndUtc,
                ReasonCode: x.ReasonCode))
            .ToArray();
    }

    /// <summary>
    /// 「状态未知」的窗口转成设备数据风险。硬口径下这些窗口已经进了不可用窗口,不再重复登记风险。
    /// </summary>
    public static IReadOnlyCollection<SchedulingEquipmentDataRiskContract> ToEquipmentDataRisks(
        EquipmentRuntimeAvailabilityResponse availability,
        SchedulingEquipmentUnknownModeContract unknownMode = SchedulingEquipmentUnknownModeContract.Soft)
    {
        ArgumentNullException.ThrowIfNull(availability);

        if (unknownMode == SchedulingEquipmentUnknownModeContract.Hard)
        {
            return [];
        }

        return availability.Items
            .Where(x => x.AvailabilityStatus == EquipmentRuntimeAvailabilityStatus.Unknown)
            .Where(x => x.EndUtc > x.StartUtc)
            .Where(x => !string.IsNullOrWhiteSpace(x.DeviceAssetId))
            .Select(x => new SchedulingEquipmentDataRiskContract(
                ResourceId: x.DeviceAssetId,
                WorkCenterId: x.WorkCenterId,
                ReasonCode: x.ReasonCode,
                StartUtc: x.StartUtc,
                EndUtc: x.EndUtc,
                SourceReferenceLabel: x.SourceReferenceLabel))
            .ToArray();
    }

    public static SchedulingProblemContract Apply(
        SchedulingProblemContract problem,
        EquipmentRuntimeAvailabilityResponse availability,
        SchedulingEquipmentUnknownModeContract unknownMode = SchedulingEquipmentUnknownModeContract.Soft)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(availability);

        if (!string.Equals(problem.OrganizationId, availability.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(problem.EnvironmentId, availability.EnvironmentId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Equipment runtime availability context does not match scheduling problem context.",
                nameof(availability));
        }

        var windows = problem.UnavailabilityWindows
            .Concat(ToUnavailabilityWindows(availability, unknownMode))
            .OrderBy(x => x.ResourceId, StringComparer.Ordinal)
            .ThenBy(x => x.WorkCenterId, StringComparer.Ordinal)
            .ThenBy(x => x.StartUtc)
            .ThenBy(x => x.EndUtc)
            .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
            .ToArray();
        var risks = (problem.EquipmentDataRisks ?? [])
            .Concat(ToEquipmentDataRisks(availability, unknownMode))
            .OrderBy(x => x.ResourceId, StringComparer.Ordinal)
            .ThenBy(x => x.WorkCenterId, StringComparer.Ordinal)
            .ThenBy(x => x.StartUtc)
            .ThenBy(x => x.EndUtc)
            .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
            .ToArray();

        return problem with { UnavailabilityWindows = windows, EquipmentDataRisks = risks };
    }

    private static bool IsHardBlocking(
        EquipmentRuntimeAvailabilityStatus status,
        SchedulingEquipmentUnknownModeContract unknownMode)
    {
        return status switch
        {
            EquipmentRuntimeAvailabilityStatus.Available => false,
            EquipmentRuntimeAvailabilityStatus.Unavailable => true,
            EquipmentRuntimeAvailabilityStatus.Unknown =>
                unknownMode == SchedulingEquipmentUnknownModeContract.Hard,
            _ => true
        };
    }
}
