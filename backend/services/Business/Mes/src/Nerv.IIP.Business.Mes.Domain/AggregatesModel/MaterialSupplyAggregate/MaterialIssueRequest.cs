using System.Globalization;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;

namespace Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;

public partial record MaterialIssueRequestId : IGuidStronglyTypedId;

/// <summary>
/// 一次线边调拨的真实库位组合：来源取库存实际持仓（领料来源单据或库存查询解析），
/// 目标取工位线边库位。领域层不提供任何默认值，避免把命名空间硬编码回来。
/// </summary>
public sealed record MaterialTransferLocations
{
    public MaterialTransferLocations(
        string sourceSiteCode,
        string sourceLocationCode,
        string targetSiteCode,
        string targetLocationCode)
    {
        SourceSiteCode = DomainGuard.Required(sourceSiteCode, nameof(sourceSiteCode));
        SourceLocationCode = DomainGuard.Required(sourceLocationCode, nameof(sourceLocationCode));
        TargetSiteCode = DomainGuard.Required(targetSiteCode, nameof(targetSiteCode));
        TargetLocationCode = DomainGuard.Required(targetLocationCode, nameof(targetLocationCode));
    }

    public string SourceSiteCode { get; }
    public string SourceLocationCode { get; }
    public string TargetSiteCode { get; }
    public string TargetLocationCode { get; }
}

/// <summary>线边收料两条库存过账腿：仓库发出（出库）与线边收入（入库）。</summary>
public enum MaterialTransferLeg
{
    /// <summary>仓库源库位出库腿（<c>mes:material-issue:</c>）。</summary>
    WarehouseIssue,

    /// <summary>线边库位入库腿（<c>mes:line-side-receipt:</c>）。</summary>
    LineSideReceipt,
}

public sealed class MaterialIssueRequest : Entity<MaterialIssueRequestId>, IAggregateRoot
{
    public const string UnspecifiedUomCode = "UNSPECIFIED";
    public const string RequestedStatus = "Requested";
    public const string PartiallyReceivedStatus = "PartiallyReceived";

    /// <summary>收料已提交、两条库存过账腿尚未双双回执。齐套一律不认这个状态下的数量。</summary>
    public const string ReceiptPostingStatus = "ReceiptPosting";
    public const string ReceivedStatus = "Received";
    public const string CancelledStatus = "Cancelled";
    public const string ReturnRequestedStatus = "ReturnRequested";
    public const string ReservationExpiredStatus = "ReservationExpired";
    public const int FailureMessageMaxLength = 500;

    /// <summary>
    /// 一次收料尝试的跨腿归一化键（两条腿共用）。带尝试序号，失败的尝试不会占用下一次重试的键。
    /// </summary>
    public const string TransferTokenPrefix = "mes:line-side-transfer:";

    private MaterialIssueRequest()
    {
    }

    private MaterialIssueRequest(
        string organizationId,
        string environmentId,
        string requestNo,
        string workOrderId,
        string? operationTaskId,
        string materialId,
        string uomCode,
        decimal requestedQuantity,
        DateTimeOffset requestedAtUtc)
    {
        OrganizationId = DomainGuard.Required(organizationId, nameof(organizationId));
        EnvironmentId = DomainGuard.Required(environmentId, nameof(environmentId));
        RequestNo = DomainGuard.Required(requestNo, nameof(requestNo));
        WorkOrderId = DomainGuard.Required(workOrderId, nameof(workOrderId));
        OperationTaskId = string.IsNullOrWhiteSpace(operationTaskId) ? null : operationTaskId.Trim();
        MaterialId = DomainGuard.Required(materialId, nameof(materialId));
        UomCode = DomainGuard.Required(uomCode, nameof(uomCode));
        RequestedQuantity = DomainGuard.Positive(requestedQuantity, nameof(requestedQuantity));
        ReceivedQuantity = 0m;
        Status = RequestedStatus;
        RequestedAtUtc = requestedAtUtc;
    }

    public string OrganizationId { get; private set; } = string.Empty;
    public string EnvironmentId { get; private set; } = string.Empty;
    public string RequestNo { get; private set; } = string.Empty;
    public string WorkOrderId { get; private set; } = string.Empty;
    public string? OperationTaskId { get; private set; }
    public string MaterialId { get; private set; } = string.Empty;
    public string UomCode { get; private set; } = string.Empty;
    public string? MaterialLotId { get; private set; }
    public decimal RequestedQuantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset? ReceivedAtUtc { get; private set; }
    public string? InventoryPostingFailureCode { get; private set; }
    public string? InventoryPostingFailureMessage { get; private set; }
    public DateTimeOffset? InventoryPostingFailedAtUtc { get; private set; }
    public string? InventoryPostingRollbackKey { get; private set; }

    /// <summary>本次收料尝试的在途数量：已提交给库存、尚未双腿回执，齐套不计。</summary>
    public decimal PendingReceiptQuantity { get; private set; }

    /// <summary>收料尝试序号，单调递增；失败的尝试不复用键，重试因此不会被库存去重吞掉。</summary>
    public int ReceiptAttempt { get; private set; }

    /// <summary>本次在途尝试的跨腿归一化键；为 null 表示当前没有在途过账。</summary>
    public string? PendingPostingToken { get; private set; }

    /// <summary>
    /// 仓库出库腿是否已回执。**跨尝试保留**：一腿成功一腿失败时，重试只会重发未过账的那条腿，
    /// 否则已经在库存实扣过的出库腿会被再扣一次（#1322 二轮审核）。
    /// </summary>
    public bool PendingIssueLegPosted { get; private set; }

    /// <summary>线边入库腿是否已回执，语义同 <see cref="PendingIssueLegPosted"/>。</summary>
    public bool PendingReceiptLegPosted { get; private set; }

    /// <summary>发料来源站点（库存实际持仓站点），由应用层从领料来源/库存查询解析后落库。</summary>
    public string? SourceSiteCode { get; private set; }

    /// <summary>发料来源库位（库存实际持仓库位），禁止由领域层臆造。</summary>
    public string? SourceLocationCode { get; private set; }

    /// <summary>收料目标站点（工位线边）。</summary>
    public string? TargetSiteCode { get; private set; }

    /// <summary>收料目标库位（工位线边库位）。</summary>
    public string? TargetLocationCode { get; private set; }

    public static MaterialIssueRequest Create(
        string organizationId,
        string environmentId,
        string requestNo,
        string workOrderId,
        string? operationTaskId,
        string materialId,
        string uomCode,
        decimal requestedQuantity,
        DateTimeOffset requestedAtUtc)
    {
        var request = new MaterialIssueRequest(
            organizationId,
            environmentId,
            requestNo,
            workOrderId,
            operationTaskId,
            materialId,
            uomCode,
            requestedQuantity,
            requestedAtUtc);
        return request;
    }

    /// <summary>
    /// 确认线边收料：只登记「在途」数量并发起两条库存过账腿，<see cref="ReceivedQuantity"/>
    /// 必须等 <see cref="MarkInventoryPosted"/> 双腿回执后才增加 —— 齐套因此不可能先于库存实扣翻绿。
    /// </summary>
    public void ConfirmLineSideReceipt(
        MaterialTransferLocations locations,
        DateTimeOffset receivedAtUtc,
        decimal? receivedQuantity = null,
        string? materialLotId = null)
    {
        ArgumentNullException.ThrowIfNull(locations);
        if (Status == ReservationExpiredStatus)
        {
            throw new InvalidOperationException("已失效的领料预留不能确认收料。");
        }

        if (PendingPostingToken is not null && InventoryPostingFailureCode is null)
        {
            throw new InvalidOperationException("上一次收料过账尚未回执，不能重复提交收料。");
        }

        // 上一次尝试里已经有一条腿过账成功（另一条被拒），这次只是补发失败的那条腿：
        // 数量必须与在途的那一笔一致，否则两条腿会记到不同数量上。
        var hasSettledLeg = PendingReceiptQuantity > 0m && (PendingIssueLegPosted || PendingReceiptLegPosted);
        var quantity = receivedQuantity ?? (hasSettledLeg
            ? PendingReceiptQuantity
            : RequestedQuantity - ReceivedQuantity);
        DomainGuard.Positive(quantity, nameof(receivedQuantity));
        if (hasSettledLeg && quantity != PendingReceiptQuantity)
        {
            throw new InvalidOperationException(
                $"上一次收料已有库存腿过账成功，重试数量必须为 {PendingReceiptQuantity:0.######}。");
        }

        if (ReceivedQuantity + quantity > RequestedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedQuantity), "Received quantity cannot exceed requested quantity.");
        }

        var normalizedMaterialLotId = string.IsNullOrWhiteSpace(materialLotId) ? null : materialLotId.Trim();
        if (!string.IsNullOrWhiteSpace(MaterialLotId) &&
            !string.IsNullOrWhiteSpace(normalizedMaterialLotId) &&
            !string.Equals(MaterialLotId, normalizedMaterialLotId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("同一领料申请不能混用多个物料批次。");
        }

        SourceSiteCode = locations.SourceSiteCode;
        SourceLocationCode = locations.SourceLocationCode;
        TargetSiteCode = locations.TargetSiteCode;
        TargetLocationCode = locations.TargetLocationCode;
        MaterialLotId = normalizedMaterialLotId ?? MaterialLotId;
        ReceiptAttempt += 1;
        PendingReceiptQuantity = quantity;
        if (!hasSettledLeg)
        {
            PendingIssueLegPosted = false;
            PendingReceiptLegPosted = false;
        }

        PendingPostingToken = BuildTransferToken(quantity);
        ReceivedAtUtc = receivedAtUtc;
        Status = ReceiptPostingStatus;
        InventoryPostingFailureCode = null;
        InventoryPostingFailureMessage = null;
        InventoryPostingFailedAtUtc = null;
        InventoryPostingRollbackKey = null;

        // 只重发尚未过账的腿：已经在库存实扣过的腿不再发第二次请求。
        if (!PendingIssueLegPosted)
        {
            AddDomainEvent(new MaterialIssueRequestedDomainEvent(this, quantity));
        }

        if (!PendingReceiptLegPosted)
        {
            AddDomainEvent(new MaterialLineSideReceiptConfirmedDomainEvent(this, quantity));
        }
    }

    /// <summary>
    /// 确认收料并立即补齐两条腿的库存回执。**仅用于回填既成事实**（世界观历史种子、测试夹具）：
    /// 真实链路必须走「确认 → 库存过账 → 回执」，状态不能凭 MES 单方面翻绿。
    /// </summary>
    public void ConfirmAndPostLineSideReceipt(
        MaterialTransferLocations locations,
        DateTimeOffset receivedAtUtc,
        decimal? receivedQuantity = null,
        string? materialLotId = null)
    {
        ConfirmLineSideReceipt(locations, receivedAtUtc, receivedQuantity, materialLotId);
        var postingToken = PendingPostingToken!;
        MarkInventoryPosted(postingToken, MaterialTransferLeg.WarehouseIssue, receivedAtUtc);
        MarkInventoryPosted(postingToken, MaterialTransferLeg.LineSideReceipt, receivedAtUtc);
    }

    /// <summary>
    /// 库存过账回执。两条腿都回执后才把在途数量转成 <see cref="ReceivedQuantity"/> 并翻状态。
    /// 键不匹配的回执（旧尝试或重复投递）一律忽略，因此 CAP 重复消费不会重复记账。
    /// </summary>
    public void MarkInventoryPosted(string postingToken, MaterialTransferLeg leg, DateTimeOffset postedAtUtc)
    {
        // 按「收料步」匹配而非整键匹配：失败后重试会换尝试序号，旧尝试迟到的成功回执仍然必须记账，
        // 否则那条腿会被当成没过账、重试时再扣一次库存。
        if (!MatchesCurrentReceiptStep(postingToken))
        {
            return;
        }

        if (leg == MaterialTransferLeg.WarehouseIssue)
        {
            PendingIssueLegPosted = true;
        }
        else
        {
            PendingReceiptLegPosted = true;
        }

        if (!PendingIssueLegPosted || !PendingReceiptLegPosted)
        {
            return;
        }

        ReceivedQuantity += PendingReceiptQuantity;
        ReceivedAtUtc = postedAtUtc;
        PendingReceiptQuantity = 0m;
        PendingPostingToken = null;
        PendingIssueLegPosted = false;
        PendingReceiptLegPosted = false;
        Status = ReceivedQuantity >= RequestedQuantity ? ReceivedStatus : PartiallyReceivedStatus;
        InventoryPostingFailureCode = null;
        InventoryPostingFailureMessage = null;
        InventoryPostingFailedAtUtc = null;
    }

    /// <summary>
    /// 库存过账失败。已收数量从未被这次尝试增加过，所以无需回滚账面 —— 只丢弃在途数量并把状态放回可重试态。
    /// 尝试序号保持不变（下一次确认才 +1），于是重试用的是全新幂等键，不会被库存去重当成重放。
    /// </summary>
    public void MarkInventoryPostingFailed(
        string failureCode,
        string failureMessage,
        DateTimeOffset failedAtUtc,
        string? postingToken = null)
    {
        if (postingToken is not null &&
            PendingPostingToken is not null &&
            !MatchesCurrentReceiptStep(postingToken))
        {
            // 别的收料步的失败回执不得推翻当前在途尝试。
            return;
        }

        if (PendingPostingToken is not null)
        {
            InventoryPostingRollbackKey = PendingPostingToken;
            if (!PendingIssueLegPosted && !PendingReceiptLegPosted)
            {
                // 两条腿都没落账：整笔在途作废，回到「还没收料」的形态。
                PendingReceiptQuantity = 0m;
                PendingPostingToken = null;
                if (ReceivedQuantity == 0m)
                {
                    ReceivedAtUtc = null;
                    MaterialLotId = null;
                }
            }

            // 有腿已落账时保留在途数量与该腿的完成标记，等重试补发剩下那条腿。
            Status = ReceivedQuantity == 0m
                ? RequestedStatus
                : (ReceivedQuantity >= RequestedQuantity ? ReceivedStatus : PartiallyReceivedStatus);
        }

        InventoryPostingFailureCode = DomainGuard.Required(failureCode, nameof(failureCode));
        InventoryPostingFailureMessage = NormalizeFailureMessage(failureMessage);
        InventoryPostingFailedAtUtc = failedAtUtc;
    }

    /// <summary>同一「收料步」判定：忽略尝试序号后缀 <c>:aN</c>，只比对作用域 + 单号 + 批次 + 累计量。</summary>
    private bool MatchesCurrentReceiptStep(string postingToken)
    {
        if (PendingPostingToken is null || string.IsNullOrWhiteSpace(postingToken))
        {
            return false;
        }

        return string.Equals(
            ReceiptStepOf(PendingPostingToken),
            ReceiptStepOf(NormalizeToken(postingToken)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ReceiptStepOf(string token)
    {
        var separatorIndex = token.LastIndexOf(":a", StringComparison.Ordinal);
        return separatorIndex < 0 ? token : token[..separatorIndex];
    }

    /// <summary>
    /// 两条腿共用的归一化幂等键：作用域 + 单号 + 批次 + 累计收料量 + 尝试序号。
    /// 尝试序号是修掉 #1322「失败尝试永久占用键」的关键。
    /// </summary>
    private string BuildTransferToken(decimal quantity)
    {
        var cumulative = ReceivedQuantity + quantity;
        return TransferTokenPrefix + string.Join(
            ':',
            OrganizationId,
            EnvironmentId,
            RequestNo,
            MaterialLotId ?? "-",
            cumulative.ToString("0.######", CultureInfo.InvariantCulture),
            $"a{ReceiptAttempt.ToString(CultureInfo.InvariantCulture)}");
    }

    private static string NormalizeToken(string token) => token.Trim();

    /// <summary>仓库出库腿的库存幂等键前缀。</summary>
    public const string WarehouseIssueKeyPrefix = "mes:material-issue:";

    /// <summary>线边入库腿的库存幂等键前缀。</summary>
    public const string LineSideReceiptKeyPrefix = "mes:line-side-receipt:";

    /// <summary>把跨腿归一化键转成某条腿的库存幂等键。</summary>
    public static string BuildLegIdempotencyKey(string transferToken, MaterialTransferLeg leg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transferToken);
        var suffix = transferToken.StartsWith(TransferTokenPrefix, StringComparison.OrdinalIgnoreCase)
            ? transferToken[TransferTokenPrefix.Length..]
            : transferToken;
        return (leg == MaterialTransferLeg.WarehouseIssue ? WarehouseIssueKeyPrefix : LineSideReceiptKeyPrefix) + suffix;
    }

    /// <summary>把库存回执里的幂等键还原成跨腿归一化键与腿别。</summary>
    public static bool TryParseLegIdempotencyKey(string? idempotencyKey, out string transferToken, out MaterialTransferLeg leg)
    {
        transferToken = string.Empty;
        leg = MaterialTransferLeg.LineSideReceipt;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return false;
        }

        var key = idempotencyKey.Trim();
        if (key.StartsWith(WarehouseIssueKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            leg = MaterialTransferLeg.WarehouseIssue;
            transferToken = TransferTokenPrefix + key[WarehouseIssueKeyPrefix.Length..];
            return true;
        }

        if (key.StartsWith(LineSideReceiptKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            leg = MaterialTransferLeg.LineSideReceipt;
            transferToken = TransferTokenPrefix + key[LineSideReceiptKeyPrefix.Length..];
            return true;
        }

        return false;
    }

    /// <summary>取已落库的调拨库位；缺失即为配置/来源解析漏配，宁可显式失败也不臆造库位。</summary>
    public MaterialTransferLocations RequireTransferLocations()
    {
        if (string.IsNullOrWhiteSpace(SourceSiteCode) ||
            string.IsNullOrWhiteSpace(SourceLocationCode) ||
            string.IsNullOrWhiteSpace(TargetSiteCode) ||
            string.IsNullOrWhiteSpace(TargetLocationCode))
        {
            throw new InvalidOperationException(
                $"领料申请缺少调拨库位，无法向库存过账，RequestNo = {RequestNo}");
        }

        return new MaterialTransferLocations(SourceSiteCode, SourceLocationCode, TargetSiteCode, TargetLocationCode);
    }

    public void ReturnLineSideMaterial(DateTimeOffset returnedAtUtc, decimal returnedQuantity, decimal consumedQuantity = 0m)
    {
        DomainGuard.Positive(returnedQuantity, nameof(returnedQuantity));
        DomainGuard.NonNegative(consumedQuantity, nameof(consumedQuantity));

        if (string.IsNullOrWhiteSpace(MaterialLotId))
        {
            // Received material without a lot cannot be returned to warehouse stock. Per #557 this is a
            // business rule: WorkOrderCancellationOrchestrator wraps this into a KnownException so the
            // cancel surfaces as a clear business error rather than a silent success.
            throw new InvalidOperationException("Line-side material return requires a received material lot.");
        }

        var returnableQuantity = Math.Max(0m, ReceivedQuantity - consumedQuantity);
        if (returnedQuantity > returnableQuantity)
        {
            throw new InvalidOperationException("退料数量不能超过当前线边可退数量。");
        }

        var returnedMaterialLotId = MaterialLotId;
        ReceivedQuantity -= returnedQuantity;
        if (ReceivedQuantity == 0m)
        {
            ReceivedAtUtc = null;
            MaterialLotId = null;
            Status = RequestedStatus;
        }
        else
        {
            ReceivedAtUtc = returnedAtUtc;
            Status = ReceivedQuantity >= RequestedQuantity ? ReceivedStatus : PartiallyReceivedStatus;
        }

        AddDomainEvent(new MaterialLineSideReturnRequestedDomainEvent(this, returnedQuantity, returnedMaterialLotId, returnedAtUtc));
        AddDomainEvent(new MaterialReturnedToWarehouseDomainEvent(this, returnedQuantity, returnedMaterialLotId, returnedAtUtc));
    }

    public void CancelForWorkOrderCancellation(DateTimeOffset cancelledAtUtc, decimal consumedQuantity = 0m)
    {
        DomainGuard.NonNegative(consumedQuantity, nameof(consumedQuantity));

        if (Status is CancelledStatus or ReturnRequestedStatus)
        {
            return;
        }

        if (ReceivedQuantity <= 0m)
        {
            Status = CancelledStatus;
            ReceivedAtUtc = null;
            MaterialLotId = null;
            return;
        }

        var returnableQuantity = Math.Max(0m, ReceivedQuantity - consumedQuantity);
        if (returnableQuantity > 0m)
        {
            ReturnLineSideMaterial(cancelledAtUtc, returnableQuantity, consumedQuantity);
        }

        Status = ReturnRequestedStatus;
    }

    public void MarkInventoryReservationExpired(DateTimeOffset expiredAtUtc)
    {
        _ = expiredAtUtc;
        if (Status is CancelledStatus or ReturnRequestedStatus or ReservationExpiredStatus || ReceivedQuantity > 0m)
        {
            return;
        }

        Status = ReservationExpiredStatus;
    }

    private static string NormalizeFailureMessage(string failureMessage)
    {
        var normalized = DomainGuard.Required(failureMessage, nameof(failureMessage));
        return normalized.Length <= FailureMessageMaxLength
            ? normalized
            : normalized[..FailureMessageMaxLength];
    }
}
