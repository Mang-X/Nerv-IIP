using Nerv.IIP.Business.Erp.Domain.AggregatesModel;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;

public partial record WorkOrderCostId : IGuidStronglyTypedId;
public partial record WorkOrderCostDetailId : IGuidStronglyTypedId;

public enum WorkOrderCostDetailType { Labor, Material }
public enum LaborCostBasis
{
    TheoreticalReport,
    TheoreticalReportReplacement,
    ActualOperation,
    ActualOperationVoid,
    ActualOperationSuperseded,
    UncostedReport,
}

public sealed class WorkOrderCost : Entity<WorkOrderCostId>, IAggregateRoot
{
    private readonly List<WorkOrderCostDetail> details = [];
    private WorkOrderCost() { }
    private WorkOrderCost(string organizationId, string environmentId, string workOrderId, string skuCode)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        WorkOrderId = ErpText.Required(workOrderId, nameof(workOrderId));
        SkuCode = ErpText.Required(skuCode, nameof(skuCode));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string SkuCode { get; private set; } = string.Empty;
    public string? LaborCurrencyCode { get; private set; }
    public decimal CompletedQuantity { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int ExpectedReportCount { get; private set; }
    public int ReceivedReportCount { get; private set; }
    public int ExpectedMaterialMovementCount { get; private set; }
    public int ReceivedMaterialMovementCount { get; private set; }
    public bool CapitalizationPublished { get; private set; }
    public decimal CapitalizedQuantity { get; private set; }
    public decimal WipClearedCost { get; private set; }
    public decimal CapitalizedCost { get; private set; }
    public decimal VarianceCost => TotalAccumulatedCost - CapitalizedCost;
    public decimal LaborCost => details.Where(x => x.Type == WorkOrderCostDetailType.Labor).Sum(x => x.Amount);
    public decimal MaterialCost => details.Where(x => x.Type == WorkOrderCostDetailType.Material).Sum(x => x.Amount);
    public decimal TotalAccumulatedCost => LaborCost + MaterialCost;
    public IReadOnlyCollection<WorkOrderCostDetail> Details => details;

    public static WorkOrderCost Open(string organizationId, string environmentId, string workOrderId, string skuCode)
        => new(organizationId, environmentId, workOrderId, skuCode);

    public void AssignSku(string skuCode) => SkuCode = ErpText.Required(skuCode, nameof(skuCode));

    public bool TryFreezeLaborCurrency(string currencyCode)
    {
        var normalized = NormalizeLaborCurrency(currencyCode);
        if (LaborCurrencyCode is not null)
            return string.Equals(LaborCurrencyCode, normalized, StringComparison.Ordinal);
        if (details.Any(x => x.Type == WorkOrderCostDetailType.Labor
                && x.LaborBasis != LaborCostBasis.UncostedReport
                && x.Amount != 0m))
            return false;
        LaborCurrencyCode = normalized;
        return true;
    }

    public void RecordLabor(string sourceDocumentId, string workCenterId, decimal hours, decimal hourlyRate, string currencyCode, bool isReversal, DateTimeOffset occurredAtUtc)
    {
        EnsureLaborCurrency(currencyCode);
        ErpText.Positive(hours, nameof(hours));
        ErpText.Positive(hourlyRate, nameof(hourlyRate));
        details.Add(WorkOrderCostDetail.CreateLabor(
            sourceDocumentId,
            workCenterId,
            hours,
            hourlyRate,
            isReversal ? -(hours * hourlyRate) : hours * hourlyRate,
            occurredAtUtc,
            LaborCostBasis.TheoreticalReport,
            sourceDocumentId));
        if (!isReversal) ReceivedReportCount++;
        TryPublishCapitalization();
    }

    public void RecordUncostedReport(string sourceDocumentId, bool isReversal, DateTimeOffset occurredAtUtc)
    {
        details.Add(WorkOrderCostDetail.CreateLabor(
            sourceDocumentId,
            "UNSPECIFIED",
            0m,
            0m,
            0m,
            occurredAtUtc,
            LaborCostBasis.UncostedReport,
            sourceDocumentId));
        if (!isReversal) ReceivedReportCount++;
        TryPublishCapitalization();
    }

    public void ReplaceTheoreticalLabor(
        string reportNo,
        string replacementSourceDocumentId,
        DateTimeOffset occurredAtUtc)
    {
        if (details.Any(x => x.Type == WorkOrderCostDetailType.Labor
                && x.LaborBasis == LaborCostBasis.TheoreticalReportReplacement
                && x.LaborLineageId == reportNo))
            return;

        var theoreticalDetails = details
            .Where(x => x.Type == WorkOrderCostDetailType.Labor
                && x.LaborBasis == LaborCostBasis.TheoreticalReport
                && x.SourceDocumentId == reportNo)
            .ToArray();
        foreach (var detail in theoreticalDetails)
        {
            details.Add(WorkOrderCostDetail.CreateLabor(
                replacementSourceDocumentId,
                detail.DimensionCode,
                -detail.Quantity,
                detail.Rate,
                -detail.Amount,
                occurredAtUtc,
                LaborCostBasis.TheoreticalReportReplacement,
                reportNo));
        }
    }

    public void ReplaceAllTheoreticalLabor(string replacementScope, DateTimeOffset occurredAtUtc)
    {
        var activeReportNos = details
            .Where(x => x.Type == WorkOrderCostDetailType.Labor
                && x.LaborBasis == LaborCostBasis.TheoreticalReport)
            .Select(x => x.SourceDocumentId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var reportNo in activeReportNos)
            ReplaceTheoreticalLabor(
                reportNo,
                $"{replacementScope}:replace:{reportNo}",
                occurredAtUtc);
    }

    public void RecordActualLabor(OperationLaborSettlement settlement)
    {
        if (settlement.Amount != 0m)
            EnsureLaborCurrency(settlement.CurrencyCode);
        details.Add(WorkOrderCostDetail.CreateLabor(
            ActualLaborSourceId(settlement.OperationTaskId, settlement.SettlementRevision, "settled"),
            settlement.WorkCenterId,
            settlement.ActualLaborHours,
            settlement.HourlyRate,
            settlement.Amount,
            settlement.CompletedAtUtc,
            LaborCostBasis.ActualOperation,
            ActualLaborLineageId(settlement.OperationTaskId, settlement.SettlementRevision)));
        TryPublishCapitalization();
    }

    public void RecordActualLaborVoid(OperationLaborSettlementVoid settlementVoid)
    {
        if (settlementVoid.Amount != 0m)
            EnsureLaborCurrency(settlementVoid.CurrencyCode);
        details.Add(WorkOrderCostDetail.CreateLabor(
            ActualLaborSourceId(settlementVoid.OperationTaskId, settlementVoid.SettlementRevision, "voided"),
            settlementVoid.WorkCenterId,
            -settlementVoid.ActualLaborHours,
            settlementVoid.HourlyRate,
            settlementVoid.Amount,
            settlementVoid.VoidedAtUtc,
            LaborCostBasis.ActualOperationVoid,
            ActualLaborLineageId(settlementVoid.OperationTaskId, settlementVoid.SettlementRevision)));
    }

    public void RecordActualLaborSuperseded(OperationLaborSettlement settlement, long supersedingRevision, DateTimeOffset occurredAtUtc)
    {
        if (settlement.Amount != 0m)
            EnsureLaborCurrency(settlement.CurrencyCode);
        details.Add(WorkOrderCostDetail.CreateLabor(
            ActualLaborSourceId(settlement.OperationTaskId, settlement.SettlementRevision, $"superseded-by-{supersedingRevision}"),
            settlement.WorkCenterId,
            -settlement.ActualLaborHours,
            settlement.HourlyRate,
            -settlement.Amount,
            occurredAtUtc,
            LaborCostBasis.ActualOperationSuperseded,
            ActualLaborLineageId(settlement.OperationTaskId, settlement.SettlementRevision)));
    }

    private static string ActualLaborLineageId(string operationTaskId, long revision)
        => $"{operationTaskId}:r{revision}";

    private static string ActualLaborSourceId(string operationTaskId, long revision, string suffix)
        => $"actual-labor:{ActualLaborLineageId(operationTaskId, revision)}:{suffix}";

    private void EnsureLaborCurrency(string currencyCode)
    {
        if (!TryFreezeLaborCurrency(currencyCode))
            throw new InvalidOperationException($"Work order '{WorkOrderId}' labor currency is incompatible with '{currencyCode}'.");
    }

    private static string NormalizeLaborCurrency(string currencyCode)
    {
        var normalized = ErpText.Required(currencyCode, nameof(currencyCode)).ToUpperInvariant();
        if (normalized.Length != 3)
            throw new ArgumentOutOfRangeException(nameof(currencyCode), currencyCode, "Currency code must contain exactly three characters.");
        return normalized;
    }

    public void RecordMaterial(string sourceDocumentId, string reportNo, string skuCode, decimal signedQuantity, decimal unitCost, DateTimeOffset occurredAtUtc)
    {
        if (signedQuantity == 0m) throw new ArgumentOutOfRangeException(nameof(signedQuantity));
        ErpText.Positive(unitCost, nameof(unitCost));
        details.Add(WorkOrderCostDetail.CreateMaterial(
            sourceDocumentId, skuCode, signedQuantity, unitCost,
            signedQuantity * unitCost, occurredAtUtc, reportNo));
        if (signedQuantity > 0m) ReceivedMaterialMovementCount++;
        TryPublishCapitalization();
    }

    public void Complete(decimal completedQuantity, int expectedReportCount, int expectedMaterialMovementCount, DateTimeOffset completedAtUtc)
    {
        CompletedQuantity = ErpText.Positive(completedQuantity, nameof(completedQuantity));
        if (expectedReportCount <= 0) throw new ArgumentOutOfRangeException(nameof(expectedReportCount));
        if (expectedMaterialMovementCount < 0) throw new ArgumentOutOfRangeException(nameof(expectedMaterialMovementCount));
        ExpectedReportCount = expectedReportCount;
        ExpectedMaterialMovementCount = expectedMaterialMovementCount;
        CompletedAtUtc = completedAtUtc;
        TryPublishCapitalization();
    }

    public void Capitalize(string sourceDocumentId, decimal quantity, decimal unitCost, DateTimeOffset occurredAtUtc)
    {
        _ = ErpText.Required(sourceDocumentId, nameof(sourceDocumentId));
        ErpText.Positive(quantity, nameof(quantity));
        ErpText.Positive(unitCost, nameof(unitCost));
        CapitalizedQuantity += quantity;
        CapitalizedCost += quantity * unitCost;
    }

    public void RecordWipClearance(decimal amount)
    {
        if (amount == 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        WipClearedCost += amount;
    }

    private void TryPublishCapitalization()
    {
        if (!CapitalizationPublished && CompletedAtUtc.HasValue && ReceivedReportCount >= ExpectedReportCount && ReceivedMaterialMovementCount >= ExpectedMaterialMovementCount)
        {
            CapitalizationPublished = true;
            AddDomainEvent(new WorkOrderCostCompletedDomainEvent(this));
        }
    }
}

public partial record WorkCenterCostRateId : IGuidStronglyTypedId;

public sealed class WorkCenterCostRate : Entity<WorkCenterCostRateId>, IAggregateRoot
{
    private WorkCenterCostRate() { }
    private WorkCenterCostRate(
        string organizationId,
        string environmentId,
        string workCenterId,
        decimal hourlyRate,
        string currencyCode,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        int revision,
        string changedBy,
        string reason,
        DateTimeOffset changedAtUtc)
    {
        OrganizationId = ErpText.Required(organizationId, nameof(organizationId));
        EnvironmentId = ErpText.Required(environmentId, nameof(environmentId));
        WorkCenterId = ErpText.Required(workCenterId, nameof(workCenterId));
        HourlyRate = ErpText.Positive(hourlyRate, nameof(hourlyRate));
        CurrencyCode = NormalizeCurrencyCode(currencyCode);
        EffectiveFromUtc = RequireUtc(effectiveFromUtc, nameof(effectiveFromUtc));
        EffectiveToUtc = effectiveToUtc is null ? null : RequireUtc(effectiveToUtc.Value, nameof(effectiveToUtc));
        if (EffectiveToUtc <= EffectiveFromUtc) throw new ArgumentOutOfRangeException(nameof(effectiveToUtc), "Effective end must be later than effective start.");
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision), revision, "Revision must be positive.");
        Revision = revision;
        ChangedBy = RequireCanonicalActor(changedBy);
        Reason = ErpText.Required(reason, nameof(reason));
        ChangedAtUtc = RequireUtc(changedAtUtc, nameof(changedAtUtc));
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string WorkCenterId { get; private set; } = string.Empty;
    public decimal HourlyRate { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }
    public int Revision { get; private set; }
    public string ChangedBy { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; private set; }

    public static WorkCenterCostRate Define(
        string organizationId,
        string environmentId,
        string workCenterId,
        decimal hourlyRate,
        string currencyCode,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        int revision,
        string changedBy,
        string reason,
        DateTimeOffset changedAtUtc)
        => new(organizationId, environmentId, workCenterId, hourlyRate, currencyCode, effectiveFromUtc, effectiveToUtc, revision, changedBy, reason, changedAtUtc);

    private static string NormalizeCurrencyCode(string value)
    {
        var normalized = ErpText.Required(value, nameof(value)).ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(character => character is >= 'A' and <= 'Z')
            ? normalized
            : throw new ArgumentException("Currency code must contain exactly three ASCII letters.", nameof(value));
    }

    private static string RequireCanonicalActor(string value)
    {
        var actor = ErpText.Required(value, nameof(value));
        if (!string.Equals(actor, value, StringComparison.Ordinal) || actor.Any(char.IsWhiteSpace))
            throw new ArgumentException("Actor must be a canonical authenticated actor.", nameof(value));
        var separator = actor.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 && separator < actor.Length - 1
            ? actor
            : throw new ArgumentException("Actor must be a canonical authenticated actor.", nameof(value));
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        => value.Offset == TimeSpan.Zero
            ? value
            : throw new ArgumentException("Timestamp must use UTC offset zero.", parameterName);
}

public partial record PendingMaterialCostId : IGuidStronglyTypedId;
public sealed class PendingMaterialCost : Entity<PendingMaterialCostId>, IAggregateRoot
{
    private PendingMaterialCost() { }
    private PendingMaterialCost(string organizationId, string environmentId, string movementId, string reportNo, string skuCode, decimal signedQuantity, decimal unitCost, DateTimeOffset postedAtUtc)
    { OrganizationId = organizationId; EnvironmentId = environmentId; MovementId = movementId; ReportNo = reportNo; SkuCode = skuCode; SignedQuantity = signedQuantity; UnitCost = unitCost; PostedAtUtc = postedAtUtc; }
    public string OrganizationId { get; private set; } = string.Empty; public string EnvironmentId { get; private set; } = string.Empty;
    public string MovementId { get; private set; } = string.Empty; public string ReportNo { get; private set; } = string.Empty; public string SkuCode { get; private set; } = string.Empty;
    public decimal SignedQuantity { get; private set; } public decimal UnitCost { get; private set; } public DateTimeOffset PostedAtUtc { get; private set; }
    public static PendingMaterialCost Create(string organizationId, string environmentId, string movementId, string reportNo, string skuCode, decimal signedQuantity, decimal unitCost, DateTimeOffset postedAtUtc)
        => new(organizationId, environmentId, movementId, reportNo, skuCode, signedQuantity, unitCost, postedAtUtc);
}

public sealed class WorkOrderCostDetail : Entity<WorkOrderCostDetailId>
{
    private WorkOrderCostDetail() { }
    private WorkOrderCostDetail(
        WorkOrderCostDetailType type,
        string sourceDocumentId,
        string dimensionCode,
        decimal quantity,
        decimal rate,
        decimal amount,
        DateTimeOffset occurredAtUtc,
        string? reportNo,
        LaborCostBasis? laborBasis,
        string? laborLineageId)
    {
        Type = type; SourceDocumentId = ErpText.Required(sourceDocumentId, nameof(sourceDocumentId));
        DimensionCode = ErpText.Required(dimensionCode, nameof(dimensionCode)); Quantity = quantity; Rate = rate; Amount = amount;
        OccurredAtUtc = occurredAtUtc; ReportNo = reportNo; LaborBasis = laborBasis; LaborLineageId = laborLineageId;
    }
    public WorkOrderCostDetailType Type { get; private set; }
    public string SourceDocumentId { get; private set; } = string.Empty;
    public string DimensionCode { get; private set; } = string.Empty;
    public string? ReportNo { get; private set; }
    public LaborCostBasis? LaborBasis { get; private set; }
    public string? LaborLineageId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Rate { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    internal static WorkOrderCostDetail CreateLabor(
        string sourceDocumentId,
        string dimensionCode,
        decimal quantity,
        decimal rate,
        decimal amount,
        DateTimeOffset occurredAtUtc,
        LaborCostBasis laborBasis,
        string laborLineageId)
        => new(
            WorkOrderCostDetailType.Labor,
            sourceDocumentId,
            dimensionCode,
            quantity,
            rate,
            amount,
            occurredAtUtc,
            null,
            laborBasis,
            ErpText.Required(laborLineageId, nameof(laborLineageId)));

    internal static WorkOrderCostDetail CreateMaterial(
        string sourceDocumentId,
        string dimensionCode,
        decimal quantity,
        decimal rate,
        decimal amount,
        DateTimeOffset occurredAtUtc,
        string reportNo)
        => new(
            WorkOrderCostDetailType.Material,
            sourceDocumentId,
            dimensionCode,
            quantity,
            rate,
            amount,
            occurredAtUtc,
            ErpText.Required(reportNo, nameof(reportNo)),
            null,
            null);
}
