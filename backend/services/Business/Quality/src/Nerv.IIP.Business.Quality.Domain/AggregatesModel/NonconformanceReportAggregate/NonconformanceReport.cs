using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;

namespace Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;

public partial record NonconformanceReportId : IGuidStronglyTypedId;

public partial record MrbReviewId : IGuidStronglyTypedId;

public sealed class NonconformanceReport : Entity<NonconformanceReportId>, IAggregateRoot
{
    private const string ScrapDisposition = "scrap";
    private const string ConditionalReleaseDisposition = "conditional-release";
    private const string ReturnToSupplierDisposition = "return-to-supplier";
    private const string SortAndScreenDisposition = "sort-and-screen";

    private static readonly HashSet<string> SourceTypes =
    [
        "receiving",
        "in-process",
        "final",
        "customer-return",
    ];

    private static readonly HashSet<string> DispositionTypes =
    [
        "rework",
        ScrapDisposition,
        ReturnToSupplierDisposition,
        ConditionalReleaseDisposition,
        SortAndScreenDisposition,
    ];

    private NonconformanceReport()
    {
    }

    private NonconformanceReport(
        string organizationId,
        string environmentId,
        string ncrCode,
        string sourceType,
        string sourceDocumentId,
        string skuCode,
        decimal defectQuantity,
        string defectReason,
        string? batchNo,
        string? serialNo,
        IReadOnlyCollection<string> attachmentFileIds,
        string? uomCode = null,
        string? siteCode = null,
        string? locationCode = null,
        string? ownerType = null,
        string? ownerId = null)
    {
        Id = new NonconformanceReportId(Guid.CreateVersion7());
        OrganizationId = Required(organizationId);
        EnvironmentId = Required(environmentId);
        NcrCode = Required(ncrCode);
        SourceType = Supported(sourceType, SourceTypes, nameof(sourceType));
        SourceDocumentId = Required(sourceDocumentId);
        SkuCode = Required(skuCode);
        DefectQuantity = Positive(defectQuantity, nameof(defectQuantity));
        DefectReason = Required(defectReason);
        BatchNo = Optional(batchNo);
        SerialNo = Optional(serialNo);
        UomCode = Optional(uomCode);
        SiteCode = Optional(siteCode);
        LocationCode = Optional(locationCode);
        OwnerType = Optional(ownerType)?.ToLowerInvariant();
        OwnerId = Optional(ownerId);
        Status = "open";
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        AttachmentFileIds.AddRange(attachmentFileIds.Select(Required).Distinct(StringComparer.OrdinalIgnoreCase));
        this.AddDomainEvent(new NonconformanceReportOpenedDomainEvent(this));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string NcrCode { get; private set; } = string.Empty;
    public string SourceType { get; private set; } = string.Empty;
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public decimal DefectQuantity { get; private set; }
    public string DefectReason { get; private set; } = string.Empty;
    public string? BatchNo { get; private set; }
    public string? SerialNo { get; private set; }
    public string? UomCode { get; private set; }
    public string? SiteCode { get; private set; }
    public string? LocationCode { get; private set; }
    public string? OwnerType { get; private set; }
    public string? OwnerId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? DispositionType { get; private set; }
    public string? DispositionApprovalChainId { get; private set; }
    public string? ReworkWorkOrderId { get; private set; }
    public string? ScrapMovementId { get; private set; }
    public string? ReturnDocumentId { get; private set; }
    public InspectionRecordId? SourceInspectionRecordId { get; private set; }
    public List<MrbReview> MrbReviews { get; private set; } = [];
    public List<string> AttachmentFileIds { get; private set; } = [];
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static NonconformanceReport Open(
        string organizationId,
        string environmentId,
        string ncrCode,
        string sourceType,
        string sourceDocumentId,
        string skuCode,
        decimal defectQuantity,
        string defectReason,
        string? batchNo,
        string? serialNo,
        IReadOnlyCollection<string> attachmentFileIds)
    {
        return new NonconformanceReport(
            organizationId,
            environmentId,
            ncrCode,
            sourceType,
            sourceDocumentId,
            skuCode,
            defectQuantity,
            defectReason,
            batchNo,
            serialNo,
            attachmentFileIds);
    }

    public static NonconformanceReport OpenFromInspection(
        string ncrCode,
        InspectionRecord inspectionRecord,
        string defectReason,
        IReadOnlyCollection<string> attachmentFileIds)
    {
        ArgumentNullException.ThrowIfNull(inspectionRecord);
        if (inspectionRecord.Result == InspectionRecordResults.Passed)
        {
            throw new InvalidOperationException("Passed inspections cannot open an NCR.");
        }

        var sourceType = ToNcrSourceType(inspectionRecord.SourceType);
        var ncr = new NonconformanceReport(
            inspectionRecord.OrganizationId,
            inspectionRecord.EnvironmentId,
            ncrCode,
            sourceType,
            inspectionRecord.SourceDocumentId,
            inspectionRecord.SkuCode,
            inspectionRecord.FailedQuantity() > 0 ? inspectionRecord.FailedQuantity() : inspectionRecord.InspectedQuantity,
            defectReason,
            inspectionRecord.BatchNo,
            inspectionRecord.SerialNo,
            attachmentFileIds,
            inspectionRecord.UomCode,
            inspectionRecord.SiteCode,
            inspectionRecord.LocationCode,
            inspectionRecord.OwnerType,
            inspectionRecord.OwnerId);
        ncr.SourceInspectionRecordId = inspectionRecord.Id;
        return ncr;
    }

    public static bool RequiresCentralApproval(string dispositionType)
    {
        var normalized = string.IsNullOrWhiteSpace(dispositionType)
            ? string.Empty
            : dispositionType.Trim().ToLowerInvariant();
        return normalized is "rework" or ScrapDisposition or ReturnToSupplierDisposition or ConditionalReleaseDisposition;
    }

    public static bool RequiresEffectiveCapa(string sourceType, string? dispositionType)
    {
        var normalizedSourceType = string.IsNullOrWhiteSpace(sourceType)
            ? string.Empty
            : sourceType.Trim().ToLowerInvariant();
        var normalizedDisposition = string.IsNullOrWhiteSpace(dispositionType)
            ? string.Empty
            : dispositionType.Trim().ToLowerInvariant();
        return normalizedSourceType == "customer-return"
            || normalizedDisposition is ScrapDisposition or ReturnToSupplierDisposition;
    }

    private static string ToNcrSourceType(string inspectionSourceType)
    {
        return inspectionSourceType switch
        {
            "receiving" => "receiving",
            "operation" => "in-process",
            "final" => "final",
            "customer-return" => "customer-return",
            _ => throw new InvalidOperationException($"Inspection source type '{inspectionSourceType}' cannot open an NCR."),
        };
    }

    public void SubmitDisposition(
        string dispositionType,
        string? dispositionApprovalChainId,
        IReadOnlyCollection<string> attachmentFileIds)
    {
        SubmitDisposition(dispositionType, dispositionApprovalChainId, attachmentFileIds, []);
    }

    public void SubmitDisposition(
        string dispositionType,
        string? dispositionApprovalChainId,
        IReadOnlyCollection<string> attachmentFileIds,
        IReadOnlyCollection<MrbReviewInput> mrbReviews)
    {
        EnsureNotClosed();
        if (Status == "disposition-in-progress")
        {
            throw new InvalidOperationException("NCR disposition has already been submitted.");
        }

        var normalizedDisposition = Supported(dispositionType, DispositionTypes, nameof(dispositionType));
        if (RequiresMrbReview(normalizedDisposition) && mrbReviews.Count == 0)
        {
            throw new InvalidOperationException("MRB review is required before this NCR disposition can be submitted.");
        }

        if (RequiresMrbReview(normalizedDisposition)
            && mrbReviews.Any(x => !string.Equals(x.Decision, "approved", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("All MRB review decisions must be approved before this NCR disposition can be submitted.");
        }

        if (RequiresDispositionEvidence(normalizedDisposition)
            && AttachmentFileIds.Count == 0
            && attachmentFileIds.Count == 0)
        {
            throw new InvalidOperationException($"{normalizedDisposition} disposition requires evidence before it can be submitted.");
        }

        DispositionType = normalizedDisposition;
        DispositionApprovalChainId = Optional(dispositionApprovalChainId);
        AddAttachments(attachmentFileIds);
        MrbReviews.AddRange(mrbReviews.Select(MrbReview.FromInput));
        Status = "disposition-in-progress";
        Touch();
        this.AddDomainEvent(new NonconformanceReportDispositionDecidedDomainEvent(this));
        if (RequiresInventoryDispositionRequest(normalizedDisposition) && HasInventoryStockLocator())
        {
            this.AddDomainEvent(new NonconformanceReportInventoryDispositionRequestedDomainEvent(this));
        }
    }

    public void Close(string? reworkWorkOrderId, string? scrapMovementId, string? returnDocumentId) =>
        Close(reworkWorkOrderId, scrapMovementId, returnDocumentId, "system closure");

    public void Close(string? reworkWorkOrderId, string? scrapMovementId, string? returnDocumentId, string reason)
    {
        var validReason = Required(reason);
        EnsureNotClosed();
        if (string.IsNullOrWhiteSpace(DispositionType))
        {
            throw new InvalidOperationException("NCR cannot be closed before disposition is decided.");
        }

        ReworkWorkOrderId = Optional(reworkWorkOrderId) ?? ReworkWorkOrderId;
        ScrapMovementId = Optional(scrapMovementId) ?? ScrapMovementId;
        ReturnDocumentId = Optional(returnDocumentId) ?? ReturnDocumentId;
        EnsureClosureReferences();
        Status = "closed";
        Touch();
        this.AddDomainEvent(new NonconformanceReportClosedDomainEvent(this, validReason));
    }

    public void CompleteScrapDisposition(string scrapMovementId)
    {
        CompleteScrapDisposition(scrapMovementId, -DefectQuantity);
    }

    public void CompleteScrapDisposition(string scrapMovementId, decimal quantity)
    {
        var movementId = Required(scrapMovementId);
        if (DispositionType != ScrapDisposition)
        {
            throw new InvalidOperationException("Only scrap NCR dispositions can be completed by an Inventory scrap movement.");
        }

        EnsureDispositionQuantityBalanced(quantity);

        if (Status == "closed")
        {
            if (ScrapMovementId == movementId)
            {
                return;
            }

            throw new InvalidOperationException("Closed NCR cannot change scrap movement id.");
        }

        Close(null, movementId, null);
    }

    public void RecordScrapDispositionMovement(string scrapMovementId, decimal quantity)
    {
        var movementId = Required(scrapMovementId);
        if (DispositionType != ScrapDisposition)
        {
            throw new InvalidOperationException("Only scrap NCR dispositions can record an Inventory scrap movement.");
        }

        EnsureDispositionQuantityBalanced(quantity);

        if (Status == "closed")
        {
            if (ScrapMovementId == movementId)
            {
                return;
            }

            throw new InvalidOperationException("Closed NCR cannot change scrap movement id.");
        }

        if (!string.IsNullOrWhiteSpace(ScrapMovementId) && ScrapMovementId != movementId)
        {
            throw new InvalidOperationException("NCR scrap disposition already recorded a different scrap movement id.");
        }

        ScrapMovementId = movementId;
        Touch();
    }

    public void CompleteConditionalReleaseDisposition()
    {
        CompleteConditionalReleaseDisposition(DefectQuantity);
    }

    public void CompleteConditionalReleaseDisposition(decimal quantity)
    {
        if (DispositionType != ConditionalReleaseDisposition)
        {
            throw new InvalidOperationException("Only conditional-release NCR dispositions can be completed by an Inventory release movement.");
        }

        EnsureDispositionQuantityBalanced(quantity);

        if (Status == "closed")
        {
            return;
        }

        Close(null, null, null);
    }

    private void EnsureClosureReferences()
    {
        if (DispositionType == "rework" && string.IsNullOrWhiteSpace(ReworkWorkOrderId))
        {
            throw new InvalidOperationException("Rework disposition requires a rework work order id before closing.");
        }

        if (DispositionType == "scrap" && string.IsNullOrWhiteSpace(ScrapMovementId))
        {
            throw new InvalidOperationException("Scrap disposition requires a scrap stock movement id before closing.");
        }

        if (DispositionType == ReturnToSupplierDisposition && string.IsNullOrWhiteSpace(ReturnDocumentId))
        {
            throw new InvalidOperationException("Return-to-supplier disposition requires a return document id before closing.");
        }
    }

    private void EnsureDispositionQuantityBalanced(decimal quantity)
    {
        if (Math.Abs(quantity) != DefectQuantity)
        {
            throw new InvalidOperationException("NCR disposition quantity must balance the full defect quantity before closing.");
        }
    }

    private static bool RequiresMrbReview(string dispositionType)
    {
        return RequiresCentralApproval(dispositionType);
    }

    private static bool RequiresInventoryDispositionRequest(string dispositionType)
    {
        return dispositionType is "rework" or ScrapDisposition or ConditionalReleaseDisposition;
    }

    private static bool RequiresDispositionEvidence(string dispositionType)
    {
        return dispositionType is ConditionalReleaseDisposition or SortAndScreenDisposition;
    }

    private bool HasInventoryStockLocator()
    {
        return !string.IsNullOrWhiteSpace(UomCode)
            && !string.IsNullOrWhiteSpace(SiteCode)
            && !string.IsNullOrWhiteSpace(LocationCode)
            && !string.IsNullOrWhiteSpace(OwnerType);
    }

    private void EnsureNotClosed()
    {
        if (Status == "closed")
        {
            throw new InvalidOperationException("Closed NCR cannot be changed.");
        }
    }

    private void AddAttachments(IReadOnlyCollection<string> attachmentFileIds)
    {
        foreach (var attachmentFileId in attachmentFileIds.Select(Required))
        {
            if (!AttachmentFileIds.Contains(attachmentFileId, StringComparer.OrdinalIgnoreCase))
            {
                AttachmentFileIds.Add(attachmentFileId);
            }
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static decimal Positive(decimal value, string parameterName)
    {
        return value <= 0 ? throw new ArgumentOutOfRangeException(parameterName, "Value must be positive.") : value;
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Supported(string value, HashSet<string> supportedValues, string parameterName)
    {
        var normalized = Required(value).ToLowerInvariant();
        return supportedValues.Contains(normalized)
            ? normalized
            : throw new ArgumentException($"Unsupported value '{value}'.", parameterName);
    }
}

public sealed class MrbReview : Entity<MrbReviewId>
{
    private MrbReview()
    {
    }

    private MrbReview(string reviewerId, string decision, string? comment, DateTimeOffset reviewedAtUtc)
    {
        Id = new MrbReviewId(Guid.CreateVersion7());
        ReviewerId = Required(reviewerId);
        Decision = Required(decision).ToLowerInvariant();
        Comment = Optional(comment);
        ReviewedAtUtc = reviewedAtUtc == default
            ? throw new ArgumentException("MRB review time is required.", nameof(reviewedAtUtc))
            : reviewedAtUtc;
    }

    public NonconformanceReportId NonconformanceReportId { get; private set; } = null!;
    public string ReviewerId { get; private set; } = string.Empty;
    public string Decision { get; private set; } = string.Empty;
    public string? Comment { get; private set; }
    public DateTimeOffset ReviewedAtUtc { get; private set; }

    public static MrbReview FromInput(MrbReviewInput input)
    {
        return new MrbReview(input.ReviewerId, input.Decision, input.Comment, input.ReviewedAtUtc);
    }

    private static string Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value cannot be blank.", nameof(value)) : value.Trim();
    }

    private static string? Optional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record MrbReviewInput(
    string ReviewerId,
    string Decision,
    string? Comment,
    DateTimeOffset ReviewedAtUtc)
{
    public static MrbReviewInput Approve(string reviewerId, string? comment, DateTimeOffset reviewedAtUtc)
    {
        return new MrbReviewInput(reviewerId, "approved", comment, reviewedAtUtc);
    }
}
