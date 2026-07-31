using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Errors;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;

public sealed record CloseNonconformanceReportCommand(
    NonconformanceReportId NcrId,
    string? ReworkWorkOrderId,
    string? ScrapMovementId,
    string? ReturnDocumentId,
    string Reason) : ICommand;

public sealed class CloseNonconformanceReportCommandValidator : AbstractValidator<CloseNonconformanceReportCommand>
{
    public CloseNonconformanceReportCommandValidator()
    {
        RuleFor(x => x.NcrId).NotEmpty();
        RuleFor(x => x.ReworkWorkOrderId).MaximumLength(150);
        RuleFor(x => x.ScrapMovementId).MaximumLength(150);
        RuleFor(x => x.ReturnDocumentId).MaximumLength(150);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CloseNonconformanceReportCommandHandler(
    INonconformanceReportRepository repository,
    ICorrectiveActionRepository correctiveActionRepository,
    IQualityIntegrationEventContextAccessor integrationEventContextAccessor)
    : ICommandHandler<CloseNonconformanceReportCommand>
{
    public async Task Handle(CloseNonconformanceReportCommand request, CancellationToken cancellationToken)
    {
        var ncr = await repository.GetAsync(request.NcrId, cancellationToken)
            ?? throw new KnownException($"找不到不合格报告 {request.NcrId}，请在不合格报告页确认单据存在后重试。");
        if (ncr.Status != "disposition-in-progress"
            || string.IsNullOrWhiteSpace(ncr.DispositionType))
        {
            throw new QualityLifecycleConflictException("close-ncr", ncr.Status);
        }

        if (NonconformanceReport.RequiresEffectiveCapa(ncr.SourceType, ncr.DispositionType)
            && !await correctiveActionRepository.HasEffectiveCapaForNcrAsync(
                ncr.OrganizationId,
                ncr.EnvironmentId,
                ncr.Id.ToString(),
                cancellationToken))
        {
            throw new KnownException($"不合格报告 {ncr.NcrCode} 关闭前需要关联已生效的 CAPA，请在 CAPA 页面完成效果验证并关联后再关闭。");
        }

        try
        {
            ncr.Close(
                request.ReworkWorkOrderId,
                request.ScrapMovementId,
                request.ReturnDocumentId,
                request.Reason,
                integrationEventContextAccessor.GetContext().Actor);
        }
        catch (InvalidOperationException)
        {
            throw new KnownException($"不合格报告 {ncr.NcrCode} 尚未满足关闭条件，请补齐处置所需单据和数量后重试。");
        }
        catch (ArgumentException)
        {
            throw new KnownException($"不合格报告 {ncr.NcrCode} 的关闭参数无效，请检查返工工单、报废过账或退供应商单据后重试。");
        }
    }
}
