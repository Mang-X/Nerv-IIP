namespace Nerv.IIP.FileStorage.Domain;

public sealed record RetirementCapability(
    string DecisionId, string OrganizationId, string EnvironmentId, string FileId,
    string Checksum, string OwnerService, string OwnerType, string OwnerId, string Purpose,
    long ReplayPolicyVersion, long ClientWindowSeconds, long BarcodeLeaseSeconds, long BarcodeMaxBackoffSeconds);

public sealed record RetirementStorageInputs(long PhysicalGraceSeconds, long GcIntervalSeconds,
    long LeaseSeconds, long MaxBackoffSeconds);

public static class RetirementReplayPolicy
{
    public const long Version = 1;
    public const long MinimumSeconds = 8 * 86400;
    public const long MaximumSeconds = 90 * 86400;
    public const long DefaultClientWindowSeconds = 30 * 86400;

    public static long Resolve(long clientWindowSeconds, long barcodeLeaseSeconds,
        long barcodeMaxBackoffSeconds, RetirementStorageInputs storage)
    {
        if (clientWindowSeconds <= 0 || barcodeLeaseSeconds <= 0 || barcodeMaxBackoffSeconds <= 0
            || storage.PhysicalGraceSeconds < 0 || storage.GcIntervalSeconds <= 0
            || storage.LeaseSeconds <= 0 || storage.MaxBackoffSeconds <= 0)
            throw new ArgumentException("Retirement replay inputs are invalid.");

        // Decimal avoids overflow on untrusted integer inputs before enforcing the safety ceiling.
        var physical = (decimal)storage.PhysicalGraceSeconds + 2m * storage.GcIntervalSeconds;
        var recovery = (decimal)Math.Max(barcodeLeaseSeconds, storage.LeaseSeconds)
            + 2m * Math.Max(barcodeMaxBackoffSeconds, storage.MaxBackoffSeconds);
        if (physical > MaximumSeconds || recovery > MaximumSeconds)
            throw new ArgumentException("Retirement safety inputs exceed 90 days.");
        return (long)Math.Clamp(Math.Max(clientWindowSeconds, Math.Max(physical, recovery)), MinimumSeconds, MaximumSeconds);
    }
}
