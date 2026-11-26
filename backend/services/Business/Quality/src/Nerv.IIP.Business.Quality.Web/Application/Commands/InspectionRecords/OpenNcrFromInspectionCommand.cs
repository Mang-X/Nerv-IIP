using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionRecords;

public sealed record OpenNcrFromInspectionCommand(
    InspectionRecordId InspectionRecordId,
    string DefectReason,
    IReadOnlyCollection<string> AttachmentFileIds,
    string? OrganizationId = null,
    string? EnvironmentId = null) : ICommand<NonconformanceReportId>;

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
        var record = await inspectionRecordRepository.GetWithResultLinesAsync(request.InspectionRecordId, cancellationToken);
        if (record is null
            || (request.OrganizationId is not null && request.OrganizationId != record.OrganizationId)
            || (request.EnvironmentId is not null && request.EnvironmentId != record.EnvironmentId))
        {
            throw new KnownException($"找不到检验记录 {request.InspectionRecordId}，请在检验记录页确认记录编号后重试。");
        }
        if (record.Result == InspectionRecordResults.Passed || !string.IsNullOrWhiteSpace(record.NonconformanceReportId))
        {
            throw new KnownException("仅可从尚未关联不合格品报告的非合格检验记录开立报告。");
        }

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
