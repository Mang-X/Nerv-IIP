using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.QualityReasonAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Queries.QualityReasons;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.QualityReasons;

public sealed record CreateQualityReasonCommand(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode,
    string ReasonName,
    string GroupName,
    string Severity,
    string? DefaultDisposition) : ICommand<QualityReasonItem>;

public sealed record UpdateQualityReasonCommand(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode,
    string ReasonName,
    string GroupName,
    string Severity,
    string? DefaultDisposition) : ICommand<QualityReasonItem>;

public sealed record ArchiveQualityReasonCommand(
    string OrganizationId,
    string EnvironmentId,
    string ReasonCode) : ICommand<QualityReasonItem>;

public sealed class CreateQualityReasonCommandValidator : AbstractValidator<CreateQualityReasonCommand>
{
    public CreateQualityReasonCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GroupName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Severity)
            .NotEmpty()
            .MaximumLength(50)
            .Must(QualityReason.IsSupportedSeverity)
            .WithMessage(QualityReasonValidationMessages.Severity);
        RuleFor(x => x.DefaultDisposition)
            .MaximumLength(100)
            .Must(QualityReason.IsSupportedDefaultDisposition)
            .WithMessage(QualityReasonValidationMessages.DefaultDisposition);
    }
}

public sealed class UpdateQualityReasonCommandValidator : AbstractValidator<UpdateQualityReasonCommand>
{
    public UpdateQualityReasonCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.GroupName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Severity)
            .NotEmpty()
            .MaximumLength(50)
            .Must(QualityReason.IsSupportedSeverity)
            .WithMessage(QualityReasonValidationMessages.Severity);
        RuleFor(x => x.DefaultDisposition)
            .MaximumLength(100)
            .Must(QualityReason.IsSupportedDefaultDisposition)
            .WithMessage(QualityReasonValidationMessages.DefaultDisposition);
    }
}

internal static class QualityReasonValidationMessages
{
    public const string Severity = "Severity must be one of: minor, major, critical.";
    public const string DefaultDisposition = "DefaultDisposition must be one of: rework, scrap, return-to-supplier, conditional-release, or omitted.";
}

public sealed class ArchiveQualityReasonCommandValidator : AbstractValidator<ArchiveQualityReasonCommand>
{
    public ArchiveQualityReasonCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ReasonCode).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateQualityReasonCommandHandler(IQualityReasonRepository repository)
    : ICommandHandler<CreateQualityReasonCommand, QualityReasonItem>
{
    public async Task<QualityReasonItem> Handle(CreateQualityReasonCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.ReasonCode, cancellationToken))
        {
            throw new KnownException($"Quality reason '{request.ReasonCode}' already exists.");
        }

        var reason = QualityReason.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.ReasonCode,
            request.ReasonName,
            request.GroupName,
            request.Severity,
            request.DefaultDisposition,
            enabled: true);
        await repository.AddAsync(reason, cancellationToken);
        return QualityReasonMapper.ToItem(reason);
    }
}

public sealed class UpdateQualityReasonCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<UpdateQualityReasonCommand, QualityReasonItem>
{
    public async Task<QualityReasonItem> Handle(UpdateQualityReasonCommand request, CancellationToken cancellationToken)
    {
        var reason = await FindAsync(dbContext, request.OrganizationId, request.EnvironmentId, request.ReasonCode, cancellationToken);
        reason.Update(request.ReasonName, request.GroupName, request.Severity, request.DefaultDisposition);
        return QualityReasonMapper.ToItem(reason);
    }

    internal static async Task<QualityReason> FindAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        return await dbContext.QualityReasons.SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.ReasonCode == reasonCode,
            cancellationToken)
            ?? throw new KnownException($"Quality reason '{reasonCode}' was not found.");
    }
}

public sealed class ArchiveQualityReasonCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ArchiveQualityReasonCommand, QualityReasonItem>
{
    public async Task<QualityReasonItem> Handle(ArchiveQualityReasonCommand request, CancellationToken cancellationToken)
    {
        var reason = await UpdateQualityReasonCommandHandler.FindAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.ReasonCode,
            cancellationToken);
        reason.SetEnabled(false);
        return QualityReasonMapper.ToItem(reason);
    }
}
