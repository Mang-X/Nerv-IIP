namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;

public partial record MeasuringDeviceId : IGuidStronglyTypedId;

public partial record CalibrationRecordId : IGuidStronglyTypedId;

public sealed class MeasuringDevice : Entity<MeasuringDeviceId>, IAggregateRoot
{
    private MeasuringDevice() { }

    private MeasuringDevice(string organizationId, string environmentId, string deviceCode, string deviceType, string accuracy, int calibrationIntervalDays, DateTimeOffset calibratedAtUtc)
    {
        Id = new MeasuringDeviceId(Guid.CreateVersion7());
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        DeviceCode = Required(deviceCode);
        DeviceType = Required(deviceType);
        Accuracy = Required(accuracy);
        CalibrationIntervalDays = Positive(calibrationIntervalDays);
        Status = MeasuringDeviceStatuses.InUse;
        LastCalibratedAtUtc = calibratedAtUtc;
        CalibrationDueAtUtc = calibratedAtUtc.AddDays(CalibrationIntervalDays);
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string DeviceCode { get; private set; } = string.Empty;
    public string DeviceType { get; private set; } = string.Empty;
    public string Accuracy { get; private set; } = string.Empty;
    public int CalibrationIntervalDays { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset? LastCalibratedAtUtc { get; private set; }
    public DateTimeOffset CalibrationDueAtUtc { get; private set; }
    public List<CalibrationRecord> CalibrationRecords { get; private set; } = [];

    public static MeasuringDevice Create(string organizationId, string environmentId, string deviceCode, string deviceType, string accuracy, int calibrationIntervalDays, DateTimeOffset calibratedAtUtc)
        => new(organizationId, environmentId, deviceCode, deviceType, accuracy, calibrationIntervalDays, calibratedAtUtc);

    public string EvaluateCalibration(DateTimeOffset nowUtc)
    {
        if (Status is MeasuringDeviceStatuses.Retired or MeasuringDeviceStatuses.Disabled)
        {
            return MeasuringDeviceCalibrationStates.Unavailable;
        }

        if (CalibrationDueAtUtc < nowUtc)
        {
            Status = MeasuringDeviceStatuses.Calibration;
            return MeasuringDeviceCalibrationStates.Overdue;
        }

        return CalibrationDueAtUtc <= nowUtc.AddDays(7)
            ? MeasuringDeviceCalibrationStates.Warning
            : MeasuringDeviceCalibrationStates.Current;
    }

    public void RecordCalibration(string calibrationNo, DateTimeOffset calibratedAtUtc, string calibratedBy, string? certificateFileId)
    {
        if (Status is MeasuringDeviceStatuses.Retired or MeasuringDeviceStatuses.Disabled)
        {
            throw new InvalidOperationException("Retired or disabled devices cannot be calibrated.");
        }

        CalibrationRecords.Add(new CalibrationRecord(calibrationNo, calibratedAtUtc, calibratedBy, certificateFileId));
        LastCalibratedAtUtc = calibratedAtUtc;
        CalibrationDueAtUtc = calibratedAtUtc.AddDays(CalibrationIntervalDays);
        Status = MeasuringDeviceStatuses.InUse;
    }

    private static string Required(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    private static int Positive(int value) => value <= 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
}

public sealed class CalibrationRecord : Entity<CalibrationRecordId>
{
    private CalibrationRecord() { }
    internal CalibrationRecord(string calibrationNo, DateTimeOffset calibratedAtUtc, string calibratedBy, string? certificateFileId)
    {
        Id = new CalibrationRecordId(Guid.CreateVersion7());
        CalibrationNo = string.IsNullOrWhiteSpace(calibrationNo) ? throw new ArgumentException("Value cannot be blank.", nameof(calibrationNo)) : calibrationNo.Trim();
        CalibratedAtUtc = calibratedAtUtc;
        CalibratedBy = string.IsNullOrWhiteSpace(calibratedBy) ? throw new ArgumentException("Value cannot be blank.", nameof(calibratedBy)) : calibratedBy.Trim();
        CertificateFileId = string.IsNullOrWhiteSpace(certificateFileId) ? null : certificateFileId.Trim();
    }

    public MeasuringDeviceId MeasuringDeviceId { get; private set; } = null!;
    public string CalibrationNo { get; private set; } = string.Empty;
    public DateTimeOffset CalibratedAtUtc { get; private set; }
    public string CalibratedBy { get; private set; } = string.Empty;
    public string? CertificateFileId { get; private set; }
}

public static class MeasuringDeviceStatuses
{
    public const string InUse = "in-use";
    public const string Calibration = "calibration";
    public const string Disabled = "disabled";
    public const string Retired = "retired";
}

public static class MeasuringDeviceCalibrationStates
{
    public const string Current = "current";
    public const string Warning = "warning";
    public const string Overdue = "overdue";
    public const string Unavailable = "unavailable";
}

public static class MeasuringDeviceInspectionPolicy
{
    public static bool Blocks(string policy, string calibrationState) =>
        string.Equals(policy, "block", StringComparison.OrdinalIgnoreCase)
        && calibrationState is MeasuringDeviceCalibrationStates.Overdue or MeasuringDeviceCalibrationStates.Unavailable;
}
