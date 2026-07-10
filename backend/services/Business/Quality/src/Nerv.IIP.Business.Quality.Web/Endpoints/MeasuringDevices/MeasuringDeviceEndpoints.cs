using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.MeasuringDevices;
using Nerv.IIP.Business.Quality.Web.Endpoints.InspectionPlans;
using Nerv.IIP.Business.Quality.Web.Endpoints.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Endpoints.MeasuringDevices;

public sealed record CreateMeasuringDeviceRequest(string OrganizationId, string EnvironmentId, string? DeviceCode, string DeviceType, string Accuracy, int CalibrationIntervalDays, DateTimeOffset CalibratedAtUtc, string? IdempotencyKey = null);
public sealed record RecordCalibrationRequest(MeasuringDeviceId MeasuringDeviceId, string CalibrationNo, DateTimeOffset CalibratedAtUtc, string CalibratedBy, string? CertificateFileId);
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
    public override async Task HandleAsync(RecordCalibrationRequest req, CancellationToken ct) { await sender.Send(new RecordMeasuringDeviceCalibrationCommand(req.MeasuringDeviceId, req.CalibrationNo, req.CalibratedAtUtc, req.CalibratedBy, req.CertificateFileId), ct); await Send.OkAsync(new AcceptedResponse(true).AsResponseData(), cancellation: ct); }
}
public sealed class GetCalibrationDashboardEndpoint(ApplicationDbContext dbContext, TimeProvider timeProvider) : QualityEndpoint<CalibrationDashboardRequest, ResponseData<CalibrationDashboardResponse>>
{
    public override void Configure() => ConfigureQualityContract(QualityInspectionEndpointContracts.Get<GetCalibrationDashboardEndpoint>());
    public override async Task HandleAsync(CalibrationDashboardRequest req, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow(); var devices = await dbContext.MeasuringDevices.Where(x => x.OrganizationId == req.OrganizationId && x.EnvironmentId == req.EnvironmentId).OrderBy(x => x.CalibrationDueAtUtc).ToArrayAsync(ct);
        var items = devices.Select(x => new CalibrationDashboardItem(x.Id, x.DeviceCode, x.DeviceType, x.Status, x.CalibrationDueAtUtc, x.EvaluateCalibration(now))).ToArray();
        await Send.OkAsync(new CalibrationDashboardResponse(items.Count(x => x.CalibrationState == MeasuringDeviceCalibrationStates.Current), items.Count(x => x.CalibrationState == MeasuringDeviceCalibrationStates.Warning), items.Count(x => x.CalibrationState == MeasuringDeviceCalibrationStates.Overdue), items).AsResponseData(), cancellation: ct);
    }
}
