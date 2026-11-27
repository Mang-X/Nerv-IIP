using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

/// <summary>
/// 交接班附件上传会话请求。用途、owner 与上传协议不收请求体：BusinessGateway 固定
/// <c>shift-handover-photo</c> 用途与 <c>business-mes / shift-handover-attachment / {principalId}</c> owner，
/// 使 <c>business.mes.handovers.manage</c> 只能开出交接班照片会话，不能借这条门面上传别的用途。
/// </summary>
public sealed record BusinessConsoleCreateShiftHandoverAttachmentUploadSessionRequest(
    string OrganizationId,
    string EnvironmentId,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string? Checksum = null);

/// <summary>
/// 交接班附件上传会话。<c>UploadProtocol</c> 按 ADR 0023 只有 <c>tus</c> 一个取值：
/// FileStorage 未按 tus 运行时本门面失败关闭，不返回无字节入口的占位指令。
/// <c>UploadUrl</c> 已改写为 BusinessGateway 受控 tus 路径，调用方拿不到 FileStorage 内部 URL。
/// </summary>
public sealed record BusinessConsoleShiftHandoverAttachmentUploadSessionResponse(
    string UploadSessionId,
    string FileId,
    string UploadProtocol,
    DateTimeOffset ExpiresAtUtc,
    string UploadUrl,
    IReadOnlyDictionary<string, string> UploadHeaders);

/// <summary>交接班附件上传 complete 请求；组织/环境与用途由门面按会话固定值补齐。</summary>
public sealed record BusinessConsoleCompleteShiftHandoverAttachmentUploadRequest(
    string OrganizationId,
    string EnvironmentId,
    string? Checksum = null,
    long? SizeBytes = null);

public sealed record BusinessConsoleMesShiftHandoverListResponse(
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverRow> Items,
    int Total);

public sealed record BusinessConsoleMesShiftHandoverRow(
    string HandoverId,
    string ShiftId,
    string TeamId,
    string HandoverStatus,
    int OpenIssueCount,
    DateTimeOffset CreatedAtUtc,
    string? TeamName = null,
    string? OutgoingUserId = null,
    string? OutgoingUserName = null,
    string? IncomingUserId = null,
    string? IncomingUserName = null,
    DateTimeOffset? AcceptedAtUtc = null,
    int WipItemCount = 0,
    int UnfinishedWorkOrderCount = 0,
    int OpenIssueDetailCount = 0);

/// <summary>交班时点的在制清点行。</summary>
public sealed record BusinessConsoleMesShiftHandoverWipItem(
    string WorkOrderId,
    string? OperationTaskId,
    decimal Quantity);

/// <summary>交班时点的未完工单进度快照。</summary>
public sealed record BusinessConsoleMesShiftHandoverUnfinishedWorkOrder(
    string WorkOrderId,
    decimal PlannedQuantity,
    decimal CompletedQuantity,
    string WorkOrderStatus);

/// <summary>
/// 随交班一并提交的 FileStorage 附件引用；文件名、内容类型与大小是交班时点快照。
/// <c>FileId</c> 是 FileStorage 文件 id，字节本身按 <c>shift-handover-photo</c> 用途存在 FileStorage，不落在 MES。
///
/// 两端通路见 <c>/api/business-console/v1/files/shift-handover-attachments/**</c>（#3085）：
/// 上传走 <c>business.mes.handovers.manage</c> 的 tus 会话 + <c>HEAD</c>/<c>PATCH</c> + complete，
/// complete 直接返回本记录；下载走 <c>business.mes.handovers.read</c> 的
/// <c>GET .../{fileId}/content</c> 单跳，网关在取字节前复核目标文件用途必须是
/// <c>shift-handover-photo</c>，且不把 FileStorage 的 download grant id 交给调用方。
/// SOP 那条 <c>/files/{fileId}/download-grants</c> 仍只认 <c>business.engineering.documents.read</c>，
/// 交接班读者不经由它取字节。
/// </summary>
public sealed record BusinessConsoleMesShiftHandoverAttachment(
    string FileId,
    string FileName,
    string ContentType,
    long SizeBytes);

/// <summary>交班时点的遗留问题；<c>Category</c> 取 Equipment/Quality，<c>Severity</c> 取 Low/Medium/High。</summary>
public sealed record BusinessConsoleMesShiftHandoverOpenIssue(
    string Category,
    string Severity,
    string Description,
    string? ReferenceId = null);

public sealed record BusinessConsoleMesShiftHandoverDetail(
    string HandoverId,
    string ShiftId,
    string TeamId,
    string HandoverStatus,
    int OpenIssueCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AcceptedAtUtc,
    string? TeamName,
    string? OutgoingUserId,
    string? OutgoingUserName,
    string? IncomingUserId,
    string? IncomingUserName,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverWipItem> WipItems,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverUnfinishedWorkOrder> UnfinishedWorkOrders,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverOpenIssue> OpenIssues,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverAttachment> Attachments);

public sealed record BusinessConsoleMesShiftHandoverDetailRequest(
    [property: RouteParam] string HandoverId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

public sealed record BusinessConsoleMesCreateShiftHandoverRequest(
    string OrganizationId,
    string EnvironmentId,
    string ShiftId,
    string TeamId,
    IReadOnlyCollection<string>? OpenIssueIds,
    string IdempotencyKey,
    string? TeamName = null,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverWipItem>? WipItems = null,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverUnfinishedWorkOrder>? UnfinishedWorkOrders = null,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverOpenIssue>? OpenIssues = null,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverAttachment>? Attachments = null);

/// <summary>
/// 转发给 MES 的建单载荷：交班人身份由 Gateway 从认证 principal 注入，显示名从 MasterData 员工目录解析，
/// 都不出现在公开请求体里。
/// </summary>
public sealed record BusinessConsoleMesCreateShiftHandoverForwardRequest(
    string OrganizationId,
    string EnvironmentId,
    string ShiftId,
    string TeamId,
    IReadOnlyCollection<string>? OpenIssueIds,
    string IdempotencyKey,
    string? TeamName,
    string? OutgoingUserId,
    string? OutgoingUserName,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverWipItem>? WipItems,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverUnfinishedWorkOrder>? UnfinishedWorkOrders,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverOpenIssue>? OpenIssues,
    IReadOnlyCollection<BusinessConsoleMesShiftHandoverAttachment>? Attachments);

public sealed record BusinessConsoleMesAcceptShiftHandoverRequest(
    [property: RouteParam] string HandoverId,
    [property: QueryParam] string OrganizationId,
    [property: QueryParam] string EnvironmentId);

/// <summary>
/// 转发给 MES 的接班载荷：接班人身份同样由 Gateway 从认证 principal 注入。
/// </summary>
public sealed record BusinessConsoleMesAcceptShiftHandoverForwardRequest(
    string OrganizationId,
    string EnvironmentId,
    string? IncomingUserId,
    string? IncomingUserName);
