using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Contracts.Wms;

namespace Nerv.IIP.Business.Wms.Web.Application.Commands;

/// <summary>
/// Warehouse leg of the MES 领料 chain: turns a MES material issue request into an outbound document
/// plus its first picking task, and announces the resulting 出库单 back to MES.
/// </summary>
public sealed record PrepareMesMaterialIssueOutboundCommand(
    string OrganizationId,
    string EnvironmentId,
    string MaterialIssueRequestNo,
    string WorkOrderId,
    string? OperationTaskId,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    string SiteCode,
    string SourceLocationCode,
    string LineSideLocationCode,
    DateTimeOffset RequestedAtUtc) : ICommand<PrepareMesMaterialIssueOutboundResult>;

public sealed record PrepareMesMaterialIssueOutboundResult(
    string OutboundOrderNo,
    string? PickingTaskNo,
    bool AlreadyPrepared);

public sealed class PrepareMesMaterialIssueOutboundCommandValidator : AbstractValidator<PrepareMesMaterialIssueOutboundCommand>
{
    public PrepareMesMaterialIssueOutboundCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaterialIssueRequestNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SourceLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LineSideLocationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class PrepareMesMaterialIssueOutboundCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<PrepareMesMaterialIssueOutboundCommand, PrepareMesMaterialIssueOutboundResult>
{
    public const string LineNo = "1";
    private const string MaterialIssueQualityStatus = "unrestricted";
    private const string MaterialIssueOwnerType = "production";

    public async Task<PrepareMesMaterialIssueOutboundResult> Handle(
        PrepareMesMaterialIssueOutboundCommand request,
        CancellationToken cancellationToken)
    {
        var outboundOrderNo = OutboundOrderNoFor(request.MaterialIssueRequestNo);
        var existing = await dbContext.OutboundOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(
                x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.OutboundOrderNo == outboundOrderNo,
                cancellationToken);
        if (existing is not null)
        {
            // Replay: the warehouse work already exists. Do not re-announce — MES either already linked
            // the 出库单 or will get it from the earlier announcement's own retry.
            var existingTaskNo = await dbContext.WarehouseTasks
                .AsNoTracking()
                .Where(x => x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.SourceOrderNo == existing.OutboundOrderNo)
                .Select(x => x.TaskNo)
                .FirstOrDefaultAsync(cancellationToken);
            return new PrepareMesMaterialIssueOutboundResult(existing.OutboundOrderNo, existingTaskNo, true);
        }

        var order = OutboundOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            outboundOrderNo,
            WmsSourceDocumentTypes.MesMaterialIssueRequest,
            request.MaterialIssueRequestNo,
            request.SiteCode,
            [
                new OutboundOrderLineDraft(
                    LineNo,
                    request.SkuCode,
                    request.UomCode,
                    request.Quantity,
                    request.SourceLocationCode,
                    null,
                    null,
                    MaterialIssueQualityStatus,
                    MaterialIssueOwnerType,
                    request.WorkOrderId)
            ]);
        dbContext.OutboundOrders.Add(order);

        // Material issue picking stays a local warehouse task: the inventory legs of this chain are
        // driven by the MES receipt/return events, so no remote reservation is taken here.
        var pickingTask = order.CreatePickingTask(
            PickingTaskNoFor(outboundOrderNo),
            LineNo,
            request.SourceLocationCode,
            request.LineSideLocationCode,
            request.Quantity);
        dbContext.WarehouseTasks.Add(pickingTask);
        order.AnnounceMaterialIssuePrepared(request.MaterialIssueRequestNo, pickingTask.TaskNo, request.RequestedAtUtc);
        return new PrepareMesMaterialIssueOutboundResult(order.OutboundOrderNo, pickingTask.TaskNo, false);
    }

    public static string OutboundOrderNoFor(string materialIssueRequestNo) => $"MI-{materialIssueRequestNo.Trim()}";

    public static string PickingTaskNoFor(string outboundOrderNo) => $"{outboundOrderNo}-P1";
}
