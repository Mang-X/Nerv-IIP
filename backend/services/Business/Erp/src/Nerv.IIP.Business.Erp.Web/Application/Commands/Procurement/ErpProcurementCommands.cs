using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Approval;
using Nerv.IIP.Business.Erp.Web.Application.MasterData;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Wms;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;

public sealed record CreatePurchaseRequisitionFromSuggestionCommand(
    string OrganizationId,
    string EnvironmentId,
    string? RequisitionNo,
    string SuggestionId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    string? IdempotencyKey = null) : ICommand<CreatePurchaseRequisitionFromSuggestionResult>;

public sealed record CreatePurchaseRequisitionFromSuggestionResult(
    PurchaseRequisitionId PurchaseRequisitionId,
    string RequisitionNo);

public sealed class CreatePurchaseRequisitionFromSuggestionCommandValidator : AbstractValidator<CreatePurchaseRequisitionFromSuggestionCommand>
{
    public CreatePurchaseRequisitionFromSuggestionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RequisitionNo).MaximumLength(100);
        RuleFor(x => x.SuggestionId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class CreatePurchaseRequisitionFromSuggestionCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<CreatePurchaseRequisitionFromSuggestionCommand, CreatePurchaseRequisitionFromSuggestionResult>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<CreatePurchaseRequisitionFromSuggestionResult> Handle(CreatePurchaseRequisitionFromSuggestionCommand request, CancellationToken cancellationToken)
    {
        var existingForSuggestion = await dbContext.PurchaseRequisitions.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.SuggestionId == request.SuggestionId,
            cancellationToken);
        if (existingForSuggestion is not null)
        {
            return new CreatePurchaseRequisitionFromSuggestionResult(existingForSuggestion.Id, existingForSuggestion.RequisitionNo);
        }

        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "purchase-requisition",
            request.RequisitionNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.SuggestionId, request.SkuCode, request.UomCode, request.SiteCode, request.Quantity, request.RequiredDate),
            cancellationToken);
        var existing = await dbContext.PurchaseRequisitions.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.RequisitionNo == allocation.Code,
            cancellationToken);
        if (existing is not null)
        {
            return new CreatePurchaseRequisitionFromSuggestionResult(existing.Id, existing.RequisitionNo);
        }

        var requisition = PurchaseRequisition.CreateFromSuggestion(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.SuggestionId,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.Quantity,
            request.RequiredDate);
        dbContext.PurchaseRequisitions.Add(requisition);
        return new CreatePurchaseRequisitionFromSuggestionResult(requisition.Id, requisition.RequisitionNo);
    }
}

public enum PurchaseRequisitionConversionStatus
{
    PurchaseOrderCreated = 0,
    AlreadyConverted = 1,
    RfqRequired = 2,
    RfqCreated = 3,
}

public sealed record ConvertPurchaseRequisitionsToPurchaseOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> PurchaseRequisitionNos,
    string? PurchaseOrderNo = null,
    string? SupplierCode = null,
    IReadOnlyCollection<string>? RfqSupplierCodes = null,
    string? RfqNo = null,
    string? IdempotencyKey = null,
    string CurrencyCode = "CNY") : ICommand<ConvertPurchaseRequisitionsToPurchaseOrderResult>;

public sealed record ConvertPurchaseRequisitionsToPurchaseOrderResult(
    PurchaseRequisitionConversionStatus Status,
    PurchaseOrderId? PurchaseOrderId = null,
    string? PurchaseOrderNo = null,
    string? RfqNo = null,
    string? SupplierCode = null,
    IReadOnlyCollection<ConvertedPurchaseOrderLineResult>? Lines = null);

public sealed record ConvertedPurchaseOrderLineResult(
    string LineNo,
    string SkuCode,
    string UomCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly PromisedDate,
    IReadOnlyCollection<ConvertedPurchaseOrderLineSourceResult> Sources);

public sealed record ConvertedPurchaseOrderLineSourceResult(
    string PurchaseRequisitionNo,
    string PurchaseRequisitionLineNo,
    decimal Quantity);

public sealed class ConvertPurchaseRequisitionsToPurchaseOrderCommandValidator : AbstractValidator<ConvertPurchaseRequisitionsToPurchaseOrderCommand>
{
    public ConvertPurchaseRequisitionsToPurchaseOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseRequisitionNos).NotEmpty();
        RuleForEach(x => x.PurchaseRequisitionNos).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PurchaseOrderNo).MaximumLength(100);
        RuleFor(x => x.SupplierCode).MaximumLength(100);
        RuleForEach(x => x.RfqSupplierCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RfqNo).MaximumLength(100);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
    }
}

public sealed class ConvertPurchaseRequisitionsToPurchaseOrderCommandHandler(
    ApplicationDbContext dbContext,
    ErpCodingService? codingService = null,
    IPurchaseOrderApprovalClient? approvalClient = null)
    : ICommandHandler<ConvertPurchaseRequisitionsToPurchaseOrderCommand, ConvertPurchaseRequisitionsToPurchaseOrderResult>
{
    private const string RequisitionLineNo = "10";
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();
    private readonly IPurchaseOrderApprovalClient _approvalClient = approvalClient ?? new GeneratedPurchaseOrderApprovalClient();

    public async Task<ConvertPurchaseRequisitionsToPurchaseOrderResult> Handle(ConvertPurchaseRequisitionsToPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var requisitionNos = request.PurchaseRequisitionNos
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (requisitionNos.Length == 0)
        {
            throw new KnownException("At least one purchase requisition is required.");
        }

        var requisitions = await dbContext.PurchaseRequisitions
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && requisitionNos.Contains(x.RequisitionNo))
            .ToListAsync(cancellationToken);
        if (requisitions.Count != requisitionNos.Length)
        {
            var found = requisitions.Select(x => x.RequisitionNo).ToHashSet(StringComparer.Ordinal);
            var missing = requisitionNos.Where(x => !found.Contains(x));
            throw new KnownException($"Purchase requisitions were not found: {string.Join(", ", missing)}.");
        }

        var convertedPurchaseOrderNos = requisitions
            .Where(x => x.Status == PurchaseRequisitionStatus.Converted)
            .Select(x => x.ConvertedPurchaseOrderNo)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (convertedPurchaseOrderNos.Length > 0)
        {
            if (convertedPurchaseOrderNos.Length == 1 && requisitions.All(x => x.Status == PurchaseRequisitionStatus.Converted))
            {
                var convertedOrderNo = convertedPurchaseOrderNos[0]!;
                var convertedOrder = await dbContext.PurchaseOrders.SingleOrDefaultAsync(x =>
                    x.OrganizationId == request.OrganizationId
                    && x.EnvironmentId == request.EnvironmentId
                    && x.PurchaseOrderNo == convertedOrderNo,
                    cancellationToken);
                return new ConvertPurchaseRequisitionsToPurchaseOrderResult(
                    PurchaseRequisitionConversionStatus.AlreadyConverted,
                    convertedOrder?.Id,
                    convertedOrderNo,
                    SupplierCode: convertedOrder?.SupplierCode);
            }

            throw new KnownException("Purchase requisitions have already been converted and cannot be mixed into another conversion.");
        }

        var priceSources = await ResolvePriceSourcesAsync(request, requisitions, cancellationToken);
        var missingPriceSources = requisitions
            .Where(x => !priceSources.ContainsKey(x.RequisitionNo))
            .OrderBy(x => x.RequisitionNo, StringComparer.Ordinal)
            .ToArray();
        if (missingPriceSources.Length > 0)
        {
            if (request.RfqSupplierCodes is { Count: > 0 })
            {
                var rfqNo = await CreateRfqForMissingPricesAsync(request, missingPriceSources, cancellationToken);
                return new ConvertPurchaseRequisitionsToPurchaseOrderResult(PurchaseRequisitionConversionStatus.RfqCreated, RfqNo: rfqNo);
            }

            return new ConvertPurchaseRequisitionsToPurchaseOrderResult(PurchaseRequisitionConversionStatus.RfqRequired);
        }

        var supplierCodes = priceSources.Values
            .Select(x => x.SupplierCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (supplierCodes.Length != 1)
        {
            throw new KnownException("Purchase requisitions resolve to multiple suppliers and must be converted separately.");
        }

        var supplierCode = supplierCodes[0];
        var siteCodes = requisitions
            .Select(x => x.SiteCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (siteCodes.Length != 1)
        {
            throw new KnownException("Purchase requisitions must belong to the same site before converting to one purchase order.");
        }

        var lineDrafts = BuildPurchaseOrderLines(requisitions, priceSources);
        var conversionIdempotencyKey = request.PurchaseOrderNo is null
            ? StableIdempotencyKey("pr-to-po", supplierCode, request.CurrencyCode, requisitionNos)
            : request.IdempotencyKey;
        var fingerprint = ErpCodingService.Fingerprint(supplierCode, request.CurrencyCode, requisitionNos);
        var replay = await _codingService.TryPeekReplayAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "purchase-order",
            conversionIdempotencyKey,
            fingerprint,
            cancellationToken);
        if (replay is not null)
        {
            var replayedOrder = await dbContext.PurchaseOrders.SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == replay.Code,
                cancellationToken);
            if (replayedOrder is not null)
            {
                return new ConvertPurchaseRequisitionsToPurchaseOrderResult(
                    PurchaseRequisitionConversionStatus.AlreadyConverted,
                    replayedOrder.Id,
                    replayedOrder.PurchaseOrderNo,
                    SupplierCode: replayedOrder.SupplierCode);
            }
        }

        await BusinessPartnerAvailabilityGate.EnsureActiveAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            supplierCode,
            cancellationToken);

        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "purchase-order",
            request.PurchaseOrderNo,
            conversionIdempotencyKey,
            fingerprint,
            cancellationToken);
        var existingOrder = await dbContext.PurchaseOrders.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.PurchaseOrderNo == allocation.Code,
            cancellationToken);
        if (existingOrder is not null)
        {
            return new ConvertPurchaseRequisitionsToPurchaseOrderResult(
                PurchaseRequisitionConversionStatus.AlreadyConverted,
                existingOrder.Id,
                existingOrder.PurchaseOrderNo,
                SupplierCode: existingOrder.SupplierCode);
        }

        var order = PurchaseOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            supplierCode,
            siteCodes[0],
            request.CurrencyCode,
            lineDrafts);
        var approvalResult = await _approvalClient.StartApprovalAsync(
            new PurchaseOrderApprovalRequest(
                request.OrganizationId,
                request.EnvironmentId,
                "erp-purchase-order-release",
                "business-erp",
                "purchase-order",
                allocation.Code,
                null,
                "system:erp",
                GeneratedPurchaseOrderApprovalClient.BuildChainId(request.OrganizationId, request.EnvironmentId, allocation.Code),
                lineDrafts.Sum(x => x.Quantity * x.UnitPrice)),
            cancellationToken);
        order.MarkApprovalRequested(approvalResult.ChainId);
        foreach (var requisition in requisitions)
        {
            requisition.MarkConverted(allocation.Code);
        }

        dbContext.PurchaseOrders.Add(order);
        return new ConvertPurchaseRequisitionsToPurchaseOrderResult(
            PurchaseRequisitionConversionStatus.PurchaseOrderCreated,
            order.Id,
            order.PurchaseOrderNo,
            SupplierCode: order.SupplierCode,
            Lines: order.Lines.Select(ToResultLine).ToArray());
    }

    private async Task<Dictionary<string, ProcurementPriceSource>> ResolvePriceSourcesAsync(
        ConvertPurchaseRequisitionsToPurchaseOrderCommand request,
        IReadOnlyCollection<PurchaseRequisition> requisitions,
        CancellationToken cancellationToken)
    {
        var skuCodes = requisitions.Select(x => x.SkuCode).Distinct(StringComparer.Ordinal).ToArray();
        var uomCodes = requisitions.Select(x => x.UomCode).Distinct(StringComparer.Ordinal).ToArray();
        var siteCodes = requisitions.Select(x => x.SiteCode).Distinct(StringComparer.Ordinal).ToArray();
        var quotations = await dbContext.SupplierQuotations
            .Include(x => x.Lines.Where(line => skuCodes.Contains(line.SkuCode) && uomCodes.Contains(line.UomCode)))
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && (request.SupplierCode == null || x.SupplierCode == request.SupplierCode)
                && x.Lines.Any(line => skuCodes.Contains(line.SkuCode) && uomCodes.Contains(line.UomCode)))
            .OrderByDescending(x => x.ReceivedAtUtc)
            .ToListAsync(cancellationToken);
        var purchaseOrders = await dbContext.PurchaseOrders
            .Include(x => x.Lines.Where(line => skuCodes.Contains(line.SkuCode) && uomCodes.Contains(line.UomCode)))
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && (request.SupplierCode == null || x.SupplierCode == request.SupplierCode)
                && siteCodes.Contains(x.SiteCode)
                && x.Lines.Any(line => skuCodes.Contains(line.SkuCode) && uomCodes.Contains(line.UomCode)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, ProcurementPriceSource>(StringComparer.Ordinal);
        foreach (var requisition in requisitions)
        {
            var quotationSource = quotations
                .SelectMany(quotation => quotation.Lines
                    .Where(line => Matches(line.SkuCode, line.UomCode, requisition))
                    .Select(line => new ProcurementPriceSource(quotation.SupplierCode, line.UnitPrice, line.PromisedDate)))
                .FirstOrDefault();
            if (quotationSource is not null)
            {
                result[requisition.RequisitionNo] = quotationSource;
                continue;
            }

            var purchaseOrderSource = purchaseOrders
                .Where(order => order.SiteCode == requisition.SiteCode)
                .SelectMany(order => order.Lines
                    .Where(line => Matches(line.SkuCode, line.UomCode, requisition))
                    .Select(line => new ProcurementPriceSource(order.SupplierCode, line.UnitPrice, line.PromisedDate)))
                .FirstOrDefault();
            if (purchaseOrderSource is not null)
            {
                result[requisition.RequisitionNo] = purchaseOrderSource;
            }
        }

        return result;
    }

    private async Task<string> CreateRfqForMissingPricesAsync(
        ConvertPurchaseRequisitionsToPurchaseOrderCommand request,
        IReadOnlyCollection<PurchaseRequisition> requisitions,
        CancellationToken cancellationToken)
    {
        var lineNo = 10;
        var lines = requisitions
            .GroupBy(x => new { x.SkuCode, x.UomCode, x.SiteCode, x.RequiredDate })
            .OrderBy(x => x.Key.SkuCode, StringComparer.Ordinal)
            .ThenBy(x => x.Key.RequiredDate)
            .Select(group =>
            {
                var currentLineNo = (lineNo++).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new RfqCommandLine(
                    currentLineNo,
                    group.Key.SkuCode,
                    group.Key.UomCode,
                    group.Sum(x => x.Quantity),
                    group.Key.SiteCode,
                    group.Key.RequiredDate);
            })
            .ToArray();
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "request-for-quotation",
            request.RfqNo,
            request.RfqNo is null
                ? StableIdempotencyKey("pr-to-rfq", request.RfqSupplierCodes!, requisitions.Select(x => x.RequisitionNo))
                : request.IdempotencyKey is null ? null : $"{request.IdempotencyKey}:rfq",
            ErpCodingService.Fingerprint(request.RfqSupplierCodes!, requisitions.Select(x => x.RequisitionNo)),
            cancellationToken);
        if (await dbContext.RequestForQuotations.AnyAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.RfqNo == allocation.Code,
                cancellationToken))
        {
            return allocation.Code;
        }

        dbContext.RequestForQuotations.Add(RequestForQuotation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.RfqSupplierCodes!,
            lines.Select(x => new RfqLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.SiteCode, x.RequiredDate))));
        return allocation.Code;
    }

    private static IReadOnlyCollection<PurchaseOrderLineDraft> BuildPurchaseOrderLines(
        IReadOnlyCollection<PurchaseRequisition> requisitions,
        IReadOnlyDictionary<string, ProcurementPriceSource> priceSources)
    {
        var lineNo = 10;
        return requisitions
            .GroupBy(x =>
            {
                var priceSource = priceSources[x.RequisitionNo];
                return new
                {
                    x.SkuCode,
                    x.UomCode,
                    x.SiteCode,
                    x.RequiredDate,
                    priceSource.UnitPrice,
                    PromisedDate = priceSource.PromisedDate < x.RequiredDate ? x.RequiredDate : priceSource.PromisedDate,
                };
            })
            .OrderBy(x => x.Key.SkuCode, StringComparer.Ordinal)
            .ThenBy(x => x.Key.RequiredDate)
            .Select(group =>
            {
                var currentLineNo = (lineNo++).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new PurchaseOrderLineDraft(
                    currentLineNo,
                    group.Key.SkuCode,
                    group.Key.UomCode,
                    group.Sum(x => x.Quantity),
                    group.Key.UnitPrice,
                    group.Key.PromisedDate,
                    Sources: group
                        .OrderBy(x => x.RequisitionNo, StringComparer.Ordinal)
                        .Select(x => new PurchaseOrderLineSourceDraft(x.RequisitionNo, RequisitionLineNo, x.Quantity))
                        .ToArray());
            })
            .ToArray();
    }

    private static bool Matches(string skuCode, string uomCode, PurchaseRequisition requisition)
    {
        return string.Equals(skuCode, requisition.SkuCode, StringComparison.Ordinal)
            && string.Equals(uomCode, requisition.UomCode, StringComparison.Ordinal);
    }

    private static ConvertedPurchaseOrderLineResult ToResultLine(PurchaseOrderLine line)
    {
        return new ConvertedPurchaseOrderLineResult(
            line.LineNo,
            line.SkuCode,
            line.UomCode,
            line.OrderedQuantity,
            line.UnitPrice,
            line.PromisedDate,
            line.SourceLinks
                .OrderBy(x => x.PurchaseRequisitionNo, StringComparer.Ordinal)
                .Select(x => new ConvertedPurchaseOrderLineSourceResult(x.PurchaseRequisitionNo, x.PurchaseRequisitionLineNo, x.Quantity))
                .ToArray());
    }

    private sealed record ProcurementPriceSource(string SupplierCode, decimal UnitPrice, DateOnly PromisedDate);

    private static string StableIdempotencyKey(string prefix, params object?[] parts)
    {
        var raw = ErpCodingService.Fingerprint(parts);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
        return $"{prefix}:{hash}";
    }
}

public sealed record RfqCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, string SiteCode, DateOnly RequiredDate);

public sealed record CreateRequestForQuotationCommand(
    string OrganizationId,
    string EnvironmentId,
    string? RfqNo,
    IReadOnlyCollection<string> SupplierCodes,
    IReadOnlyCollection<RfqCommandLine> Lines,
    string? IdempotencyKey = null) : ICommand<RequestForQuotationId>;

public sealed class CreateRequestForQuotationCommandValidator : AbstractValidator<CreateRequestForQuotationCommand>
{
    public CreateRequestForQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RfqNo).MaximumLength(100);
        RuleFor(x => x.SupplierCodes).NotEmpty();
        RuleForEach(x => x.SupplierCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.RequiredDate).NotEqual(default(DateOnly));
        });
    }
}

public sealed class CreateRequestForQuotationCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<CreateRequestForQuotationCommand, RequestForQuotationId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<RequestForQuotationId> Handle(CreateRequestForQuotationCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "request-for-quotation",
            request.RfqNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.SupplierCodes, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.RequiredDate}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.RequestForQuotations.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.RfqNo == allocation.Code,
                cancellationToken)).Id;
        }

        var rfq = RequestForQuotation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.SupplierCodes,
            request.Lines.Select(x => new RfqLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.SiteCode, x.RequiredDate)));
        dbContext.RequestForQuotations.Add(rfq);
        return rfq.Id;
    }
}

public sealed record SupplierQuotationCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, decimal UnitPrice, DateOnly PromisedDate);

public sealed record ReceiveSupplierQuotationCommand(
    string OrganizationId,
    string EnvironmentId,
    string? QuotationNo,
    string RfqNo,
    string SupplierCode,
    IReadOnlyCollection<SupplierQuotationCommandLine> Lines,
    string? IdempotencyKey = null) : ICommand<SupplierQuotationId>;

public sealed class ReceiveSupplierQuotationCommandValidator : AbstractValidator<ReceiveSupplierQuotationCommand>
{
    public ReceiveSupplierQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.QuotationNo).MaximumLength(100);
        RuleFor(x => x.RfqNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SupplierCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitPrice).GreaterThan(0);
            line.RuleFor(x => x.PromisedDate).NotEqual(default(DateOnly));
        });
    }
}

public sealed class ReceiveSupplierQuotationCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<ReceiveSupplierQuotationCommand, SupplierQuotationId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<SupplierQuotationId> Handle(ReceiveSupplierQuotationCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "supplier-quotation",
            request.QuotationNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.RfqNo, request.SupplierCode, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.UnitPrice}:{x.PromisedDate}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.SupplierQuotations.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.QuotationNo == allocation.Code,
                cancellationToken)).Id;
        }

        var quotation = SupplierQuotation.Receive(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.RfqNo,
            request.SupplierCode,
            request.Lines.Select(x => new SupplierQuotationLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.UnitPrice, x.PromisedDate)));
        dbContext.SupplierQuotations.Add(quotation);
        return quotation.Id;
    }
}

public sealed record PurchaseOrderCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, decimal UnitPrice, DateOnly PromisedDate);

public sealed record CreatePurchaseOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string? PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    IReadOnlyCollection<PurchaseOrderCommandLine> Lines,
    string? IdempotencyKey = null,
    string CurrencyCode = "CNY") : ICommand<PurchaseOrderId>;

public sealed class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseOrderNo).MaximumLength(100);
        RuleFor(x => x.SupplierCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitPrice).GreaterThan(0);
            line.RuleFor(x => x.PromisedDate).NotEqual(default(DateOnly));
        });
    }
}

public sealed class CreatePurchaseOrderCommandHandler(
    ApplicationDbContext dbContext,
    ErpCodingService? codingService = null,
    IPurchaseOrderApprovalClient? approvalClient = null)
    : ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();
    private readonly IPurchaseOrderApprovalClient _approvalClient = approvalClient ?? new GeneratedPurchaseOrderApprovalClient();

    public async Task<PurchaseOrderId> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var fingerprint = ErpCodingService.Fingerprint(request.SupplierCode, request.SiteCode, request.CurrencyCode, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.UnitPrice}:{x.PromisedDate}"));
        var replay = await _codingService.TryPeekReplayAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "purchase-order",
            request.IdempotencyKey,
            fingerprint,
            cancellationToken);
        if (replay is not null)
        {
            return (await dbContext.PurchaseOrders.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == replay.Code,
                cancellationToken)).Id;
        }

        await BusinessPartnerAvailabilityGate.EnsureActiveAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            request.SupplierCode,
            cancellationToken);

        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "purchase-order",
            request.PurchaseOrderNo,
            request.IdempotencyKey,
            fingerprint,
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.PurchaseOrders.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == allocation.Code,
                cancellationToken)).Id;
        }

        var order = PurchaseOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.SupplierCode,
            request.SiteCode,
            request.CurrencyCode,
            request.Lines.Select(x => new PurchaseOrderLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.UnitPrice, x.PromisedDate)));
        var approvalResult = await _approvalClient.StartApprovalAsync(
            new PurchaseOrderApprovalRequest(
                request.OrganizationId,
                request.EnvironmentId,
                "erp-purchase-order-release",
                "business-erp",
                "purchase-order",
                allocation.Code,
                null,
                "system:erp",
                GeneratedPurchaseOrderApprovalClient.BuildChainId(request.OrganizationId, request.EnvironmentId, allocation.Code),
                request.Lines.Sum(x => x.Quantity * x.UnitPrice)),
            cancellationToken);
        order.MarkApprovalRequested(approvalResult.ChainId);
        dbContext.PurchaseOrders.Add(order);
        return order.Id;
    }

}

file sealed class GeneratedPurchaseOrderApprovalClient : IPurchaseOrderApprovalClient
{
    public Task<PurchaseOrderApprovalResult> StartApprovalAsync(PurchaseOrderApprovalRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new PurchaseOrderApprovalResult(request.ChainId));
    }

    public static string BuildChainId(string organizationId, string environmentId, string purchaseOrderNo)
    {
        var raw = $"{organizationId}:{environmentId}:{purchaseOrderNo}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
        return $"erp-po-approval-{hash}";
    }
}

public sealed record PurchaseReceiptCommandLine(
    string PurchaseOrderLineNo,
    decimal ReceivedQuantity,
    string QualityStatus,
    string? LocationCode = null,
    string? LotNo = null,
    bool FinalDelivery = false);

public sealed record RecordPurchaseReceiptCommand(
    string OrganizationId,
    string EnvironmentId,
    string? PurchaseReceiptNo,
    string PurchaseOrderNo,
    IReadOnlyCollection<PurchaseReceiptCommandLine> Lines,
    string? IdempotencyKey = null,
    decimal ExchangeRate = 1m) : ICommand<PurchaseReceiptId>;

public sealed class RecordPurchaseReceiptCommandValidator : AbstractValidator<RecordPurchaseReceiptCommand>
{
    public RecordPurchaseReceiptCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseReceiptNo).MaximumLength(100);
        RuleFor(x => x.PurchaseOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.PurchaseOrderLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.ReceivedQuantity).GreaterThan(0);
            line.RuleFor(x => x.QualityStatus).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class RecordPurchaseReceiptCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<RecordPurchaseReceiptCommand, PurchaseReceiptId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<PurchaseReceiptId> Handle(RecordPurchaseReceiptCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "purchase-receipt",
            request.PurchaseReceiptNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.PurchaseOrderNo, request.ExchangeRate, request.Lines.Select(x => $"{x.PurchaseOrderLineNo}:{x.ReceivedQuantity}:{x.QualityStatus}:{x.FinalDelivery}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.PurchaseReceipts.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseReceiptNo == allocation.Code,
                cancellationToken)).Id;
        }

        var order = await dbContext.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == request.PurchaseOrderNo,
                cancellationToken)
            ?? throw new KnownException($"Purchase order '{request.PurchaseOrderNo}' was not found.");

        PurchaseReceipt receipt;
        try
        {
            receipt = PurchaseReceipt.Record(
                order,
                allocation.Code,
                request.Lines.Select(x => new PurchaseReceiptLineDraft(x.PurchaseOrderLineNo, x.ReceivedQuantity, x.QualityStatus, x.LocationCode, x.LotNo, x.FinalDelivery)),
                request.ExchangeRate);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new KnownException(exception.Message, exception);
        }

        dbContext.PurchaseReceipts.Add(receipt);
        return receipt.Id;
    }
}

public sealed record SupplierInvoiceCommandLine(
    string PurchaseOrderLineNo,
    string PurchaseReceiptLineNo,
    decimal InvoiceQuantity,
    decimal UnitPrice);

public sealed record RecordSupplierInvoiceCommand(
    string OrganizationId,
    string EnvironmentId,
    string? InvoiceNo,
    string PurchaseOrderNo,
    string PurchaseReceiptNo,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string CurrencyCode,
    decimal QuantityTolerance,
    decimal AmountTolerance,
    IReadOnlyCollection<SupplierInvoiceCommandLine> Lines,
    string? PayableNo = null,
    string? IdempotencyKey = null,
    decimal? PriceTolerancePercent = null,
    decimal ExchangeRate = 1m) : ICommand<SupplierInvoiceId>;

public sealed class RecordSupplierInvoiceCommandValidator : AbstractValidator<RecordSupplierInvoiceCommand>
{
    public RecordSupplierInvoiceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.InvoiceNo).MaximumLength(100);
        RuleFor(x => x.PurchaseOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PurchaseReceiptNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InvoiceDate).NotEqual(default(DateOnly));
        RuleFor(x => x.DueDate).NotEqual(default(DateOnly));
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.QuantityTolerance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AmountTolerance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PriceTolerancePercent).GreaterThanOrEqualTo(0).When(x => x.PriceTolerancePercent.HasValue);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.PurchaseOrderLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.PurchaseReceiptLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.InvoiceQuantity).GreaterThan(0);
            line.RuleFor(x => x.UnitPrice).GreaterThan(0);
        });
    }
}

public sealed class RecordSupplierInvoiceCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<RecordSupplierInvoiceCommand, SupplierInvoiceId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<SupplierInvoiceId> Handle(RecordSupplierInvoiceCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "supplier-invoice",
            request.InvoiceNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.PurchaseOrderNo, request.PurchaseReceiptNo, request.InvoiceDate, request.DueDate, request.CurrencyCode, request.ExchangeRate, request.QuantityTolerance, request.AmountTolerance, request.PriceTolerancePercent, request.PayableNo, request.Lines.Select(x => $"{x.PurchaseOrderLineNo}:{x.PurchaseReceiptLineNo}:{x.InvoiceQuantity}:{x.UnitPrice}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.SupplierInvoices.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.InvoiceNo == allocation.Code,
                cancellationToken)).Id;
        }

        var order = await dbContext.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == request.PurchaseOrderNo,
                cancellationToken)
            ?? throw new KnownException($"Purchase order '{request.PurchaseOrderNo}' was not found.");
        var receipt = await dbContext.PurchaseReceipts
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseReceiptNo == request.PurchaseReceiptNo,
                cancellationToken)
            ?? throw new KnownException($"Purchase receipt '{request.PurchaseReceiptNo}' was not found.");
        var alreadyInvoicedQuantitiesByReceiptLineNo = (await dbContext.SupplierInvoices
            .Include(x => x.Lines)
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseReceiptNo == request.PurchaseReceiptNo
                && x.MatchStatus != SupplierInvoiceMatchStatus.Voided)
            .SelectMany(x => x.Lines)
            .ToListAsync(cancellationToken))
            .GroupBy(x => x.PurchaseReceiptLineNo, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.InvoiceQuantity), StringComparer.Ordinal);

        var invoiceLines = request.Lines.Select(x => new SupplierInvoiceLineDraft(x.PurchaseOrderLineNo, x.PurchaseReceiptLineNo, x.InvoiceQuantity, x.UnitPrice));
        var invoice = request.PriceTolerancePercent is { } priceTolerancePercent
            ? SupplierInvoice.Match(
                order,
                receipt,
                allocation.Code,
                request.InvoiceDate,
                request.DueDate,
                request.CurrencyCode,
                request.QuantityTolerance,
                request.AmountTolerance,
                priceTolerancePercent,
                invoiceLines,
                alreadyInvoicedQuantitiesByReceiptLineNo,
                request.ExchangeRate)
            : SupplierInvoice.Match(
                order,
                receipt,
                allocation.Code,
                request.InvoiceDate,
                request.DueDate,
                request.CurrencyCode,
                request.QuantityTolerance,
                request.AmountTolerance,
                invoiceLines,
                alreadyInvoicedQuantitiesByReceiptLineNo,
                request.ExchangeRate);
        dbContext.SupplierInvoices.Add(invoice);

        if (invoice.MatchStatus == SupplierInvoiceMatchStatus.PaymentHeld)
        {
            return invoice.Id;
        }

        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            invoice.InvoiceDate,
            "supplier invoice GR/IR clearing voucher",
            cancellationToken);
        var payableAllocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "account-payable",
            request.PayableNo,
            request.IdempotencyKey is null ? null : $"{request.IdempotencyKey}:account-payable",
            ErpCodingService.Fingerprint(invoice.InvoiceNo, invoice.SupplierCode, invoice.TotalAmount, invoice.CurrencyCode, invoice.InvoiceDate, invoice.DueDate, "MATCHED"),
            cancellationToken);
        var payable = AccountPayable.Create(
            request.OrganizationId,
            request.EnvironmentId,
            payableAllocation.Code,
            invoice.InvoiceNo,
            invoice.SupplierCode,
            invoice.TotalAmount,
            invoice.CurrencyCode,
            invoice.InvoiceDate,
            invoice.DueDate,
            "MATCHED",
            request.ExchangeRate);
        dbContext.AccountPayables.Add(payable);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForSupplierInvoiceGrIrClearing(invoice, payable, receipt.ExchangeRate));
        return invoice.Id;
    }
}

public sealed record ReleaseSupplierInvoicePaymentHoldCommand(
    string OrganizationId,
    string EnvironmentId,
    string InvoiceNo,
    string? PayableNo,
    string IdempotencyKey) : ICommand<SupplierInvoiceId>;

public sealed class ReleaseSupplierInvoicePaymentHoldCommandValidator : AbstractValidator<ReleaseSupplierInvoicePaymentHoldCommand>
{
    public ReleaseSupplierInvoicePaymentHoldCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.InvoiceNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PayableNo).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class ReleaseSupplierInvoicePaymentHoldCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<ReleaseSupplierInvoicePaymentHoldCommand, SupplierInvoiceId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<SupplierInvoiceId> Handle(ReleaseSupplierInvoicePaymentHoldCommand request, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.SupplierInvoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.InvoiceNo == request.InvoiceNo,
                cancellationToken)
            ?? throw new KnownException($"Supplier invoice '{request.InvoiceNo}' was not found.");

        var existingPayableForInvoice = await dbContext.AccountPayables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.SourceDocumentNo == invoice.InvoiceNo,
            cancellationToken);
        if (existingPayableForInvoice is not null)
        {
            if (invoice.MatchStatus == SupplierInvoiceMatchStatus.PaymentHeld)
            {
                invoice.ReleasePaymentHold();
            }

            return invoice.Id;
        }

        var payableAllocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "account-payable",
            request.PayableNo,
            $"{request.IdempotencyKey}:account-payable",
            ErpCodingService.Fingerprint(invoice.InvoiceNo, invoice.SupplierCode, invoice.TotalAmount, invoice.CurrencyCode, invoice.ExchangeRate, invoice.InvoiceDate, invoice.DueDate, "HELD-RELEASE"),
            cancellationToken);
        var existingPayable = await dbContext.AccountPayables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.PayableNo == payableAllocation.Code,
            cancellationToken);
        if (existingPayable is not null)
        {
            invoice.ReleasePaymentHold();
            return invoice.Id;
        }

        try
        {
            invoice.ReleasePaymentHold();
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }

        var receipt = await dbContext.PurchaseReceipts.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.PurchaseReceiptNo == invoice.PurchaseReceiptNo,
            cancellationToken)
            ?? throw new KnownException($"Purchase receipt '{invoice.PurchaseReceiptNo}' was not found.");
        await AccountingPeriodPostingGuard.EnsureOpenAsync(
            dbContext,
            request.OrganizationId,
            request.EnvironmentId,
            invoice.InvoiceDate,
            "supplier invoice payment hold release voucher",
            cancellationToken);
        var payable = AccountPayable.Create(
            request.OrganizationId,
            request.EnvironmentId,
            payableAllocation.Code,
            invoice.InvoiceNo,
            invoice.SupplierCode,
            invoice.TotalAmount,
            invoice.CurrencyCode,
            invoice.InvoiceDate,
            invoice.DueDate,
            "MATCHED",
            invoice.ExchangeRate);
        dbContext.AccountPayables.Add(payable);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForSupplierInvoiceGrIrClearing(invoice, payable, receipt.ExchangeRate));
        return invoice.Id;
    }
}

public sealed record VoidSupplierInvoicePaymentHoldCommand(
    string OrganizationId,
    string EnvironmentId,
    string InvoiceNo) : ICommand<SupplierInvoiceId>;

public sealed class VoidSupplierInvoicePaymentHoldCommandValidator : AbstractValidator<VoidSupplierInvoicePaymentHoldCommand>
{
    public VoidSupplierInvoicePaymentHoldCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.InvoiceNo).NotEmpty().MaximumLength(100);
    }
}

public sealed class VoidSupplierInvoicePaymentHoldCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<VoidSupplierInvoicePaymentHoldCommand, SupplierInvoiceId>
{
    public async Task<SupplierInvoiceId> Handle(VoidSupplierInvoicePaymentHoldCommand request, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.SupplierInvoices.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.InvoiceNo == request.InvoiceNo,
            cancellationToken)
            ?? throw new KnownException($"Supplier invoice '{request.InvoiceNo}' was not found.");

        try
        {
            invoice.VoidPaymentHold();
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }

        return invoice.Id;
    }
}

public sealed record RequestPurchaseOrderChangeCommand(
    string OrganizationId,
    string EnvironmentId,
    string PurchaseOrderNo,
    IReadOnlyCollection<PurchaseOrderLineChangeDraft> Lines,
    string? Reason = null,
    string StartedBy = "system:erp") : ICommand<string>;

public sealed class RequestPurchaseOrderChangeCommandValidator : AbstractValidator<RequestPurchaseOrderChangeCommand>
{
    public RequestPurchaseOrderChangeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.OrderedQuantity).GreaterThan(0);
            line.RuleFor(x => x.UnitPrice).GreaterThan(0);
        });
        RuleFor(x => x.Reason).MaximumLength(1000);
        RuleFor(x => x.StartedBy).NotEmpty().MaximumLength(150);
    }
}

public sealed class RequestPurchaseOrderChangeCommandHandler(
    ApplicationDbContext dbContext,
    IPurchaseOrderApprovalClient? approvalClient = null)
    : ICommandHandler<RequestPurchaseOrderChangeCommand, string>
{
    private readonly IPurchaseOrderApprovalClient _approvalClient = approvalClient ?? new GeneratedPurchaseOrderApprovalClient();

    public async Task<string> Handle(RequestPurchaseOrderChangeCommand request, CancellationToken cancellationToken)
    {
        var order = await dbContext.PurchaseOrders
            .Include(x => x.Lines)
            .Include(x => x.ChangeHistory)
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == request.PurchaseOrderNo, cancellationToken)
            ?? throw new KnownException($"Purchase order '{request.PurchaseOrderNo}' was not found.");
        try
        {
            if (order.Status == PurchaseOrderStatus.PendingApproval && order.ApprovalChainId is null)
            {
                order.ReviseBeforeApproval(request.Lines);
                var revisedChainId = GeneratedPurchaseOrderApprovalClient.BuildChainId(
                    request.OrganizationId,
                    request.EnvironmentId,
                    $"{request.PurchaseOrderNo}:revision:{order.Version}");
                var revisedApproval = await _approvalClient.StartApprovalAsync(
                    new PurchaseOrderApprovalRequest(
                        request.OrganizationId,
                        request.EnvironmentId,
                        "erp-purchase-order-release",
                        "business-erp",
                        "purchase-order",
                        request.PurchaseOrderNo,
                        null,
                        request.StartedBy,
                        revisedChainId,
                        order.TotalAmount),
                    cancellationToken);
                order.MarkApprovalRequested(revisedApproval.ChainId);
                return revisedApproval.ChainId;
            }

            var change = order.RequestChange(request.Lines, request.Reason);
            var chainId = GeneratedPurchaseOrderApprovalClient.BuildChainId(
                request.OrganizationId,
                request.EnvironmentId,
                $"{request.PurchaseOrderNo}:change:{Guid.CreateVersion7():N}");
            var approval = await _approvalClient.StartApprovalAsync(
                new PurchaseOrderApprovalRequest(request.OrganizationId, request.EnvironmentId, "erp-purchase-order-change", "business-erp", "purchase-order", request.PurchaseOrderNo, null, request.StartedBy, chainId),
                cancellationToken);
            change.AssignApprovalChain(approval.ChainId);
            return approval.ChainId;
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}

public sealed record ClosePurchaseOrderLineCommand(string OrganizationId, string EnvironmentId, string PurchaseOrderNo, string LineNo, string Reason) : ICommand;

public sealed class ClosePurchaseOrderLineCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<ClosePurchaseOrderLineCommand>
{
    public async Task Handle(ClosePurchaseOrderLineCommand request, CancellationToken cancellationToken)
    {
        var order = await dbContext.PurchaseOrders.Include(x => x.Lines).Include(x => x.ChangeHistory).SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.PurchaseOrderNo == request.PurchaseOrderNo,
            cancellationToken) ?? throw new KnownException($"Purchase order '{request.PurchaseOrderNo}' was not found.");
        try { order.CloseRemainingLine(request.LineNo, request.Reason); }
        catch (InvalidOperationException exception) { throw new KnownException(exception.Message, exception); }
    }
}

public sealed record CancelPurchaseOrderCommand(string OrganizationId, string EnvironmentId, string PurchaseOrderNo, string Reason) : ICommand;

public sealed class CancelPurchaseOrderCommandHandler(
    ApplicationDbContext dbContext,
    IWmsInboundCancellationClient wmsInboundCancellationClient) : ICommandHandler<CancelPurchaseOrderCommand>
{
    public async Task Handle(CancelPurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await dbContext.PurchaseOrders.Include(x => x.Lines).Include(x => x.ChangeHistory).SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.PurchaseOrderNo == request.PurchaseOrderNo,
            cancellationToken) ?? throw new KnownException($"Purchase order '{request.PurchaseOrderNo}' was not found.");
        if (await dbContext.SupplierInvoices.AnyAsync(x => x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId && x.PurchaseOrderNo == request.PurchaseOrderNo, cancellationToken))
        {
            throw new KnownException("Purchase orders with supplier invoices cannot be cancelled.");
        }

        if (order.Status != PurchaseOrderStatus.Released)
        {
            throw new KnownException("Only released purchase orders can be cancelled.");
        }

        if (order.Lines.Any(x => x.ReceivedQuantity > 0m))
        {
            throw new KnownException("Purchase orders with received quantity cannot be cancelled.");
        }

        await wmsInboundCancellationClient.CancelOpenInboundOrdersForPurchaseOrderAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.PurchaseOrderNo,
            request.Reason,
            cancellationToken);
        try { order.Cancel(request.Reason); }
        catch (InvalidOperationException exception) { throw new KnownException(exception.Message, exception); }
    }
}
