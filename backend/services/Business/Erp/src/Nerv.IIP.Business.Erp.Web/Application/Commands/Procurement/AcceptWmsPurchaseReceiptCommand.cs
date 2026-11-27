using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Contracts.Erp;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;

public sealed record WmsPurchaseReceiptActualLine(
    string PurchaseOrderLineNo,
    decimal ReceivedQuantity,
    string? LocationCode = null,
    string? LotNo = null);

public sealed record AcceptWmsPurchaseReceiptCommand(
    string OrganizationId,
    string EnvironmentId,
    Guid WmsReceiptBatchId,
    string PurchaseOrderNo,
    string SiteCode,
    decimal ExchangeRate,
    IReadOnlyCollection<WmsPurchaseReceiptActualLine> Lines,
    bool Unrestricted = false) : ICommand<RecordPurchaseReceiptResult>;

public sealed class AcceptWmsPurchaseReceiptCommandValidator : AbstractValidator<AcceptWmsPurchaseReceiptCommand>
{
    public AcceptWmsPurchaseReceiptCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.WmsReceiptBatchId).NotEmpty();
        RuleFor(x => x.PurchaseOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExchangeRate).GreaterThan(0).PrecisionScale(18, 8, ignoreTrailingZeros: true);
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop).NotEmpty()
            .Must(lines => lines.All(line => line is not null)
                && lines.Select(line => line.PurchaseOrderLineNo).Distinct(StringComparer.Ordinal).Count() == lines.Count)
            .WithMessage("每个采购订单行在同一收货批次中只能出现一次，且收货行不能为空。");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.PurchaseOrderLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.ReceivedQuantity).GreaterThan(0).PrecisionScale(18, 6, ignoreTrailingZeros: true);
            line.RuleFor(x => x.LocationCode).MaximumLength(100);
            line.RuleFor(x => x.LotNo).MaximumLength(100);
        });
    }
}

public sealed class AcceptWmsPurchaseReceiptCommandHandler(ApplicationDbContext dbContext, ErpCodingService codingService)
    : ICommandHandler<AcceptWmsPurchaseReceiptCommand, RecordPurchaseReceiptResult>
{
    public Task<RecordPurchaseReceiptResult> Handle(AcceptWmsPurchaseReceiptCommand request, CancellationToken cancellationToken)
    {
        var lines = request.Lines.OrderBy(line => line.PurchaseOrderLineNo, StringComparer.Ordinal)
            .Select(line => new PurchaseReceiptCommandLine(
                line.PurchaseOrderLineNo,
                line.ReceivedQuantity,
                request.Unrestricted ? ErpReceiptQualityStatuses.Unrestricted : ErpReceiptQualityStatuses.Quality,
                line.LocationCode?.Trim(),
                string.IsNullOrWhiteSpace(line.LotNo) ? null : line.LotNo.Trim()))
            .ToArray();
        var fingerprint = WmsPurchaseReceiptAcceptanceIdentity.FingerprintPrefix
            + (request.Unrestricted ? "unrestricted:" : string.Empty) + Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                request.PurchaseOrderNo,
                request.SiteCode,
                ExchangeRate = CanonicalDecimal(request.ExchangeRate),
                Lines = lines.Select(line => new
                {
                    line.PurchaseOrderLineNo,
                    ReceivedQuantity = CanonicalDecimal(line.ReceivedQuantity),
                    LocationCode = line.LocationCode ?? string.Empty,
                    line.LotNo,
                }),
            })));
        var receipt = new RecordPurchaseReceiptCommand(
            request.OrganizationId, request.EnvironmentId, null, request.PurchaseOrderNo, lines,
            WmsPurchaseReceiptAcceptanceIdentity.Key(request.WmsReceiptBatchId),
            request.ExchangeRate, PurchaseReceiptInventoryPostingRoute.Wms);
        return new RecordPurchaseReceiptCommandHandler(dbContext, codingService)
            .HandleWmsAcceptanceAsync(receipt, request.SiteCode, fingerprint, request.WmsReceiptBatchId, cancellationToken);
    }

    private static string CanonicalDecimal(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}

internal static class WmsPurchaseReceiptAcceptanceIdentity
{
    internal const string ReceiptRuleKey = "purchase-receipt";
    internal const string KeyPrefix = "wms-receipt-acceptance:";
    internal const string FingerprintPrefix = "wms-receipt-acceptance-v1:";

    internal static string Key(Guid batchId) => $"{KeyPrefix}{batchId:N}";
}
