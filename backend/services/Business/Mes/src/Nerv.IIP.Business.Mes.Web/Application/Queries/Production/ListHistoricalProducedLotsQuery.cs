using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Queries.Production;

public sealed record ListHistoricalProducedLotsQuery(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId) : IQuery<ListHistoricalProducedLotsResponse>;

public sealed record ListHistoricalProducedLotsResponse(IReadOnlyCollection<HistoricalProducedLotFact> Items);

public sealed record HistoricalProducedLotFact(
    string ProducedLotNo,
    string ReportNo,
    string OperationTaskId,
    decimal Quantity,
    DateTimeOffset CreatedAtUtc,
    string? SerialNo,
    IReadOnlyCollection<string> SerialNumbers);

public sealed class ListHistoricalProducedLotsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListHistoricalProducedLotsQuery, ListHistoricalProducedLotsResponse>
{
    public async Task<ListHistoricalProducedLotsResponse> Handle(
        ListHistoricalProducedLotsQuery request, CancellationToken cancellationToken)
    {
        // genealogy 是产出事实；入库申请及其剩余量不决定历史目录的存在性。
        var items = await dbContext.OutputLotGenealogies
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.WorkOrderId == request.WorkOrderId)
            .OrderBy(x => x.ProducedLotNo)
            .Select(x => new HistoricalProducedLotFact(
                x.ProducedLotNo, x.ReportNo, x.OperationTaskId, x.Quantity, x.CreatedAtUtc, x.SerialNo,
                dbContext.ProductionReportSerialNumbers
                    .Where(serial => serial.OrganizationId == x.OrganizationId
                        && serial.EnvironmentId == x.EnvironmentId
                        && serial.ReportNo == x.ReportNo)
                    .OrderBy(serial => serial.SequenceNo)
                    .Select(serial => serial.SerialNumber)
                    .ToArray()))
            .ToArrayAsync(cancellationToken);
        return new ListHistoricalProducedLotsResponse(items);
    }
}
