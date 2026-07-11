using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.IndustrialTelemetry.Domain.AggregatesModel.DeviceControlChannelBindingAggregate;
using Nerv.IIP.Business.IndustrialTelemetry.Infrastructure;

namespace Nerv.IIP.Business.IndustrialTelemetry.Web.Application.Commands;

public sealed record CreateOrUpdateDeviceControlBindingCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string ConnectorHostId,
    string InstanceKey) : ICommand<DeviceControlChannelBindingId>;

public sealed class CreateOrUpdateDeviceControlBindingCommandValidator : AbstractValidator<CreateOrUpdateDeviceControlBindingCommand>
{
    public CreateOrUpdateDeviceControlBindingCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.ConnectorHostId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.InstanceKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class CreateOrUpdateDeviceControlBindingCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateOrUpdateDeviceControlBindingCommand, DeviceControlChannelBindingId>
{
    public async Task<DeviceControlChannelBindingId> Handle(CreateOrUpdateDeviceControlBindingCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.DeviceControlChannelBindings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId,
            cancellationToken);
        if (existing is not null)
        {
            existing.UpdateDefinition(request.ConnectorHostId, request.InstanceKey);
            return existing.Id;
        }

        var binding = DeviceControlChannelBinding.Configure(
            request.OrganizationId,
            request.EnvironmentId,
            request.DeviceAssetId,
            request.ConnectorHostId,
            request.InstanceKey);
        dbContext.DeviceControlChannelBindings.Add(binding);
        return binding.Id;
    }
}

public sealed record DisableDeviceControlBindingCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string? Reason) : ICommand<DeviceControlChannelBindingId>;

public sealed class DisableDeviceControlBindingCommandValidator : AbstractValidator<DisableDeviceControlBindingCommand>
{
    public DisableDeviceControlBindingCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeviceAssetId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}

public sealed class DisableDeviceControlBindingCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<DisableDeviceControlBindingCommand, DeviceControlChannelBindingId>
{
    public async Task<DeviceControlChannelBindingId> Handle(DisableDeviceControlBindingCommand request, CancellationToken cancellationToken)
    {
        var binding = await dbContext.DeviceControlChannelBindings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.DeviceAssetId == request.DeviceAssetId,
            cancellationToken)
            ?? throw new KnownException($"Device control channel binding was not found for device: {request.DeviceAssetId}");
        binding.Disable(request.Reason);
        return binding.Id;
    }
}
