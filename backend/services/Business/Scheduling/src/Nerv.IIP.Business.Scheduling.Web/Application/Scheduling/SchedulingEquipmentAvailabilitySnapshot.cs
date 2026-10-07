using System.Text.Json;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

// Internal replay input. Maintenance owns ETR/MTTR/default selection; actual windows stay authoritative.
internal sealed record SchedulingEquipmentAvailabilitySnapshot(
    DateTimeOffset AsOfUtc,
    int ContractVersion,
    IReadOnlyCollection<SchedulingEquipmentAvailabilityInput> Windows)
{
    public static SchedulingEquipmentAvailabilitySnapshot Create(
        EquipmentRuntimeAvailabilityResponse availability, DateTimeOffset asOfUtc)
    {
        var windows = availability.Items.Select(window =>
        {
            var actual = window with
            {
                StartUtc = window.StartUtc.ToUniversalTime(),
                EndUtc = window.EndUtc.ToUniversalTime(),
                ExpectedRestoreAtUtc = window.AvailabilityStatus == EquipmentRuntimeAvailabilityStatus.Unavailable
                    ? window.ExpectedRestoreAtUtc?.ToUniversalTime() : null,
                RestorePredictionSource = window.AvailabilityStatus == EquipmentRuntimeAvailabilityStatus.Unavailable
                    ? window.RestorePredictionSource : null,
                RestorePredictionSourceVersion = window.AvailabilityStatus == EquipmentRuntimeAvailabilityStatus.Unavailable
                    ? window.RestorePredictionSourceVersion : null,
                SubstituteDeviceAssetIds = window.SubstituteDeviceAssetIds.Order(StringComparer.Ordinal).ToArray()
            };
            return new SchedulingEquipmentAvailabilityInput(actual,
                actual.ExpectedRestoreAtUtc is { } prediction && prediction <= asOfUtc);
        }).OrderBy(x => JsonSerializer.Serialize(x, SchedulingJson.Options), StringComparer.Ordinal).ToArray();
        return new(asOfUtc.ToUniversalTime(), availability.ContractVersion, windows);
    }
}

internal sealed record SchedulingEquipmentAvailabilityInput(
    EquipmentRuntimeAvailabilityWindowContract Window,
    bool RestorePredictionExpired);
