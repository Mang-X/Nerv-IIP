using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;

public sealed record OpenNcrFromInspectionCommand(
    InspectionRecordId InspectionRecordId,
    string DefectReason,
    IReadOnlyCollection<string> AttachmentFileIds) : ICommand<NonconformanceReportId>;

public sealed class OpenNcrFromInspectionCommandValidator : AbstractValidator<OpenNcrFromInspectionCommand>
{
    public OpenNcrFromInspectionCommandValidator()
    {
        RuleFor(x => x.InspectionRecordId).NotEmpty();
        RuleFor(x => x.DefectReason).NotEmpty().MaximumLength(200);
    }
}

public sealed class OpenNcrFromInspectionCommandHandler(
    IInspectionRecordRepository inspectionRecordRepository,
    INonconformanceReportRepository ncrRepository,
    INonconformanceReportCodeGenerator codeGenerator)
    : ICommandHandler<OpenNcrFromInspectionCommand, NonconformanceReportId>
{
    public async Task<NonconformanceReportId> Handle(OpenNcrFromInspectionCommand request, CancellationToken cancellationToken)
    {
        var record = await inspectionRecordRepository.GetAsync(request.InspectionRecordId, cancellationToken)
            ?? throw new KnownException($"Inspection record '{request.InspectionRecordId}' was not found.");
        var ncrCode = await codeGenerator.NextAsync(record.OrganizationId, record.EnvironmentId, cancellationToken);
        var ncr = NonconformanceReport.OpenFromInspection(
            ncrCode,
            record,
            request.DefectReason,
            request.AttachmentFileIds);
        record.LinkNonconformanceReport(ncr.Id.ToString());
        await ncrRepository.AddAsync(ncr, cancellationToken);
        return ncr.Id;
    }
}
