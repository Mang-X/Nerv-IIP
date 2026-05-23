using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;

public sealed record InspectionResultLineCommandInput(
    string CharacteristicCode,
    string ObservedValue,
    string? UnitCode,
    string Result,
    string? DefectReason,
    decimal? DefectQuantity,
    IReadOnlyCollection<string> AttachmentFileIds);

public sealed record CreateInspectionRecordCommand(
    string OrganizationId,
    string EnvironmentId,
    InspectionPlanId? InspectionPlanId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string SkuCode,
    decimal InspectedQuantity,
    string? BatchNo,
    string? SerialNo,
    IReadOnlyCollection<InspectionResultLineCommandInput> ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds) : ICommand<InspectionRecordId>;

public sealed class CreateInspectionRecordCommandValidator : AbstractValidator<CreateInspectionRecordCommand>
{
    public CreateInspectionRecordCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SourceService).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceDocumentId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InspectedQuantity).GreaterThan(0);
        RuleFor(x => x.ResultLines).NotEmpty();
        RuleFor(x => x.DispositionReason)
            .NotEmpty()
            .When(HasNonPassedResultLine)
            .WithMessage("Disposition reason is required when any inspection result line is rejected or conditionally released.");
    }

    private static bool HasNonPassedResultLine(CreateInspectionRecordCommand command)
    {
        return command.ResultLines?.Any(line =>
            !string.Equals(line.Result, InspectionLineResults.Passed, StringComparison.OrdinalIgnoreCase)) == true;
    }
}

public sealed class CreateInspectionRecordCommandHandler(IInspectionRecordRepository repository)
    : ICommandHandler<CreateInspectionRecordCommand, InspectionRecordId>
{
    public async Task<InspectionRecordId> Handle(CreateInspectionRecordCommand request, CancellationToken cancellationToken)
    {
        var record = InspectionRecord.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.InspectionPlanId,
            request.SourceType,
            request.SourceService,
            request.SourceDocumentId,
            request.SkuCode,
            request.InspectedQuantity,
            request.BatchNo,
            request.SerialNo,
            request.ResultLines.Select(x => new InspectionResultLineInput(
                x.CharacteristicCode,
                x.ObservedValue,
                x.UnitCode,
                x.Result,
                x.DefectReason,
                x.DefectQuantity,
                x.AttachmentFileIds)).ToArray(),
            request.DispositionReason,
            request.DispositionAttachmentFileIds);
        await repository.AddAsync(record, cancellationToken);
        return record.Id;
    }
}
