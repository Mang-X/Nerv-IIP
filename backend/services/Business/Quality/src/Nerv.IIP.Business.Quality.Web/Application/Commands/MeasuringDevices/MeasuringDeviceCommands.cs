using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.MeasuringDevices;

public sealed record CreateMeasuringDeviceCommand(string OrganizationId, string EnvironmentId, string? DeviceCode, string DeviceType, string Accuracy, int CalibrationIntervalDays, DateTimeOffset CalibratedAtUtc, string? IdempotencyKey = null) : ICommand<MeasuringDeviceId>;
public sealed class CreateMeasuringDeviceCommandHandler(ApplicationDbContext dbContext, QualityCodingService codingService) : ICommandHandler<CreateMeasuringDeviceCommand, MeasuringDeviceId>
{
    public async Task<MeasuringDeviceId> Handle(CreateMeasuringDeviceCommand request, CancellationToken cancellationToken)
    {
        var code = (await codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "measuring-device", request.DeviceCode, request.IdempotencyKey, QualityCodingService.Fingerprint(request.DeviceType, request.Accuracy, request.CalibrationIntervalDays, request.CalibratedAtUtc), cancellationToken)).Code;
        var existing = await dbContext.MeasuringDevices.SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.DeviceCode == code, cancellationToken);
        if (existing is not null) return existing.Id;
        var device = MeasuringDevice.Create(request.OrganizationId, request.EnvironmentId, code, request.DeviceType, request.Accuracy, request.CalibrationIntervalDays, request.CalibratedAtUtc);
        await dbContext.MeasuringDevices.AddAsync(device, cancellationToken);
        return device.Id;
    }
}

public sealed record RecordMeasuringDeviceCalibrationCommand(MeasuringDeviceId MeasuringDeviceId, string CalibrationNo, DateTimeOffset CalibratedAtUtc, string CalibrationProvider, string? CertificateFileId) : ICommand;
public sealed class RecordMeasuringDeviceCalibrationCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<RecordMeasuringDeviceCalibrationCommand>
{
    public async Task Handle(RecordMeasuringDeviceCalibrationCommand request, CancellationToken cancellationToken)
    {
        var device = await dbContext.MeasuringDevices.Include(x => x.CalibrationRecords).SingleOrDefaultAsync(x => x.Id == request.MeasuringDeviceId, cancellationToken) ?? throw new KnownException("Measuring device was not found.");
        device.RecordCalibration(request.CalibrationNo, request.CalibratedAtUtc, request.CalibrationProvider, request.CertificateFileId);
    }
}

public sealed record ChangeMeasuringDeviceStatusCommand(MeasuringDeviceId MeasuringDeviceId, string Status) : ICommand;
public sealed class ChangeMeasuringDeviceStatusCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<ChangeMeasuringDeviceStatusCommand>
{
    public async Task Handle(ChangeMeasuringDeviceStatusCommand request, CancellationToken cancellationToken)
    {
        var device = await dbContext.MeasuringDevices.SingleOrDefaultAsync(x => x.Id == request.MeasuringDeviceId, cancellationToken) ?? throw new KnownException("Measuring device was not found.");
        switch (request.Status.Trim().ToLowerInvariant())
        {
            case MeasuringDeviceStatuses.InUse: device.Enable(); break;
            case MeasuringDeviceStatuses.Disabled: device.Disable(); break;
            case MeasuringDeviceStatuses.Retired: device.Retire(); break;
            default: throw new KnownException("Measuring device status must be in-use, disabled, or retired.");
        }
    }
}
