namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesHistoricalProducedLotListResponse(
    IReadOnlyCollection<BusinessConsoleMesHistoricalProducedLotRow> Items);

public sealed record BusinessConsoleMesHistoricalProducedLotRow(
    string ProducedLotNo,
    string ReportNo,
    string OperationTaskId,
    decimal Quantity,
    DateTimeOffset CreatedAtUtc,
    string? SerialNo,
    IReadOnlyCollection<string> SerialNumbers);
