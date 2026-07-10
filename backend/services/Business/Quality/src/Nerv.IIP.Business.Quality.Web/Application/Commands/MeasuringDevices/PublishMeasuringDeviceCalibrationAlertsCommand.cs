using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.MeasuringDeviceAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Contracts.Quality;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.MeasuringDevices;

public sealed record PublishMeasuringDeviceCalibrationAlertsCommand(string OrganizationId, string EnvironmentId, DateTimeOffset NowUtc) : ICommand<int>;

public sealed class PublishMeasuringDeviceCalibrationAlertsCommandHandler(ApplicationDbContext dbContext, IIntegrationEventPublisher publisher)
    : ICommandHandler<PublishMeasuringDeviceCalibrationAlertsCommand, int>
{
    public async Task<int> Handle(PublishMeasuringDeviceCalibrationAlertsCommand request, CancellationToken cancellationToken)
    {
        var devices = await dbContext.MeasuringDevices.Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Status != MeasuringDeviceStatuses.Retired && x.Status != MeasuringDeviceStatuses.Disabled && x.CalibrationDueAtUtc <= request.NowUtc.AddDays(7)).ToArrayAsync(cancellationToken);
        foreach (var device in devices)
        {
            var state = device.EvaluateCalibration(request.NowUtc);
            await publisher.PublishAsync(new MeasuringDeviceCalibrationDueIntegrationEvent(
                $"evt-{Guid.CreateVersion7():N}", QualityIntegrationEventTypes.MeasuringDeviceCalibrationDue, QualityIntegrationEventVersions.V1, request.NowUtc,
                QualityIntegrationEventSources.BusinessQuality, $"quality:calibration:{device.Id}", device.Id.ToString(), device.OrganizationId, device.EnvironmentId,
                "system:quality", $"quality:calibration:{device.Id}:{state}:{device.CalibrationDueAtUtc:O}",
                new MeasuringDeviceCalibrationDuePayload(device.Id.ToString(), device.DeviceCode, device.DeviceType, state, device.CalibrationDueAtUtc, request.NowUtc)), cancellationToken);
        }
        return devices.Length;
    }
}
