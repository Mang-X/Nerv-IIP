using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.MeasuringDevices;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.MeasuringDevices;

public sealed record CreateMeasuringDeviceRequest(string OrganizationId, string EnvironmentId, string? DeviceCode, string DeviceType, string Accuracy, int CalibrationIntervalDays, DateTimeOffset CalibratedAtUtc, string? IdempotencyKey = null);
public sealed record RecordCalibrationRequest(MeasuringDeviceId MeasuringDeviceId, string CalibrationNo, DateTimeOffset CalibratedAtUtc, string CalibrationProvider, string? CertificateFileId);
public sealed record ChangeMeasuringDeviceStatusRequest(MeasuringDeviceId MeasuringDeviceId, string Status);
public sealed record CalibrationDashboardRequest(string OrganizationId, string EnvironmentId, int WarningDays = 7);
public sealed record CalibrationDashboardResponse(int Current, int Warning, int Overdue, IReadOnlyCollection<CalibrationDashboardItem> Items);
public sealed record CalibrationDashboardItem(MeasuringDeviceId MeasuringDeviceId, string DeviceCode, string DeviceType, string Status, DateTimeOffset CalibrationDueAtUtc, string CalibrationState);

public sealed class CreateMeasuringDeviceEndpoint(ISender sender) : QualityEndpoint<CreateMeasuringDeviceRequest, ResponseData<MeasuringDeviceId>>
{
    public override void Configure() => ConfigureQualityContract(QualityInspectionEndpointContracts.Get<CreateMeasuringDeviceEndpoint>());
    public override async Task HandleAsync(CreateMeasuringDeviceRequest req, CancellationToken ct) => await Send.OkAsync((await sender.Send(new CreateMeasuringDeviceCommand(req.OrganizationId, req.EnvironmentId, req.DeviceCode, req.DeviceType, req.Accuracy, req.CalibrationIntervalDays, req.CalibratedAtUtc, req.IdempotencyKey), ct)).AsResponseData(), cancellation: ct);
}
public sealed class RecordCalibrationEndpoint(ISender sender) : QualityEndpoint<RecordCalibrationRequest, ResponseData<AcceptedResponse>>
{
    public override void Configure() => ConfigureQualityContract(QualityInspectionEndpointContracts.Get<RecordCalibrationEndpoint>());
    public override async Task HandleAsync(RecordCalibrationRequest req, CancellationToken ct) { await sender.Send(new RecordMeasuringDeviceCalibrationCommand(req.MeasuringDeviceId, req.CalibrationNo, req.CalibratedAtUtc, req.CalibrationProvider, req.CertificateFileId), ct); await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct); }
}
public sealed class ChangeMeasuringDeviceStatusEndpoint(ISender sender) : QualityEndpoint<ChangeMeasuringDeviceStatusRequest, ResponseData<AcceptedResponse>>
{
    public override void Configure() => ConfigureQualityContract(QualityInspectionEndpointContracts.Get<ChangeMeasuringDeviceStatusEndpoint>());
    public override async Task HandleAsync(ChangeMeasuringDeviceStatusRequest req, CancellationToken ct) { await sender.Send(new ChangeMeasuringDeviceStatusCommand(req.MeasuringDeviceId, req.Status), ct); await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct); }
}
public sealed class GetCalibrationDashboardEndpoint(ApplicationDbContext dbContext, TimeProvider timeProvider) : QualityEndpoint<CalibrationDashboardRequest, ResponseData<CalibrationDashboardResponse>>
{
    public override void Configure() => ConfigureQualityContract(QualityInspectionEndpointContracts.Get<GetCalibrationDashboardEndpoint>());
    public override async Task HandleAsync(CalibrationDashboardRequest req, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow(); var warningDays = Math.Clamp(req.WarningDays, 0, 365); var devices = await dbContext.MeasuringDevices.AsNoTracking().Where(x => x.OrganizationId == req.OrganizationId && x.EnvironmentId == req.EnvironmentId).OrderBy(x => x.CalibrationDueAtUtc).ToArrayAsync(ct);
        var items = devices.Select(x => new CalibrationDashboardItem(x.Id, x.DeviceCode, x.DeviceType, x.Status, x.CalibrationDueAtUtc, x.ComputeCalibrationState(now, warningDays))).ToArray();
        await Send.OkAsync(new CalibrationDashboardResponse(items.Count(x => x.CalibrationState == MeasuringDeviceCalibrationStates.Current), items.Count(x => x.CalibrationState == MeasuringDeviceCalibrationStates.Warning), items.Count(x => x.CalibrationState == MeasuringDeviceCalibrationStates.Overdue), items).AsResponseData(), cancellation: ct);
    }
}
