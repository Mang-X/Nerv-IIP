using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.StandardOperations;

public sealed record StandardOperationCommandResult(string OperationCode);

public sealed record CreateStandardOperationCommand(
    string OrganizationId,
    string EnvironmentId,
    string OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int StandardSetupMinutes,
    int StandardRunMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced,
    string? Description) : ICommand<StandardOperationCommandResult>, IStandardOperationDetails;

public sealed class CreateStandardOperationCommandValidator : AbstractValidator<CreateStandardOperationCommand>
{
    public CreateStandardOperationCommandValidator()
    {
        Include(new StandardOperationDetailsValidator());
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateStandardOperationCommandHandler(IStandardOperationRepository repository)
    : ICommandHandler<CreateStandardOperationCommand, StandardOperationCommandResult>
{
    public async Task<StandardOperationCommandResult> Handle(CreateStandardOperationCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.OperationCode, cancellationToken))
        {
            throw new KnownException($"Standard operation '{request.OperationCode}' already exists.");
        }

        var operation = ProductEngineeringReleaseValidation.AsKnownException(() => StandardOperation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.OperationCode,
            request.OperationName,
            request.DefaultWorkCenterCode,
            request.StandardSetupMinutes,
            request.StandardRunMinutes,
            request.ControlKey,
            request.RequiresReporting,
            request.RequiresQualityInspection,
            request.IsOutsourced,
            request.Description));
        await repository.AddAsync(operation, cancellationToken);
        return new StandardOperationCommandResult(operation.OperationCode);
    }
}

public sealed record UpdateStandardOperationCommand(
    string OrganizationId,
    string EnvironmentId,
    string OperationCode,
    string OperationName,
    string DefaultWorkCenterCode,
    int StandardSetupMinutes,
    int StandardRunMinutes,
    string ControlKey,
    bool RequiresReporting,
    bool RequiresQualityInspection,
    bool IsOutsourced,
    string? Description) : ICommand<StandardOperationCommandResult>, IStandardOperationDetails;

public sealed class UpdateStandardOperationCommandValidator : AbstractValidator<UpdateStandardOperationCommand>
{
    public UpdateStandardOperationCommandValidator()
    {
        Include(new StandardOperationDetailsValidator());
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateStandardOperationCommandHandler(IStandardOperationRepository repository)
    : ICommandHandler<UpdateStandardOperationCommand, StandardOperationCommandResult>
{
    public async Task<StandardOperationCommandResult> Handle(UpdateStandardOperationCommand request, CancellationToken cancellationToken)
    {
        var operation = await repository.GetByCodeAsync(request.OrganizationId, request.EnvironmentId, request.OperationCode, cancellationToken)
            ?? throw new KnownException($"Standard operation '{request.OperationCode}' was not found.");

        ProductEngineeringReleaseValidation.AsKnownException(() =>
        {
            operation.Update(
                request.OperationName,
                request.DefaultWorkCenterCode,
                request.StandardSetupMinutes,
                request.StandardRunMinutes,
                request.ControlKey,
                request.RequiresReporting,
                request.RequiresQualityInspection,
                request.IsOutsourced,
                request.Description);
            return operation;
        });
        return new StandardOperationCommandResult(operation.OperationCode);
    }
}

public sealed record ArchiveStandardOperationCommand(string OrganizationId, string EnvironmentId, string OperationCode, string Reason) : ICommand;

public sealed class ArchiveStandardOperationCommandValidator : AbstractValidator<ArchiveStandardOperationCommand>
{
    public ArchiveStandardOperationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OperationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ArchiveStandardOperationCommandHandler(IStandardOperationRepository repository)
    : ICommandHandler<ArchiveStandardOperationCommand>
{
    public async Task Handle(ArchiveStandardOperationCommand request, CancellationToken cancellationToken)
    {
        var operation = await repository.GetByCodeAsync(request.OrganizationId, request.EnvironmentId, request.OperationCode, cancellationToken)
            ?? throw new KnownException($"Standard operation '{request.OperationCode}' was not found.");
        ProductEngineeringReleaseValidation.AsKnownException(() =>
        {
            operation.Archive(request.Reason);
            return operation;
        });
    }
}

internal sealed class StandardOperationDetailsValidator : AbstractValidator<IStandardOperationDetails>
{
    public StandardOperationDetailsValidator()
    {
        RuleFor(x => x.OperationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DefaultWorkCenterCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StandardSetupMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.StandardRunMinutes).GreaterThan(0);
        RuleFor(x => x.ControlKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public interface IStandardOperationDetails
{
    string OperationName { get; }

    string DefaultWorkCenterCode { get; }

    int StandardSetupMinutes { get; }

    int StandardRunMinutes { get; }

    string ControlKey { get; }

    string? Description { get; }
}
