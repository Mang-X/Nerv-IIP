namespace Nerv.IIP.BusinessGateway.Web;

internal static class BusinessGatewayLocalRejectionStableWireCodes
{
    public const string BarcodeResolvePageInvalid = "barcode-resolve-page-invalid";
    public const string MesOperationReadForbidden = "mes-operation-read-forbidden";
    public const string IdempotencyKeyRequired = "idempotency-key-required";
    public const string OperationTaskOutsideWorkCenter = "operation-task-outside-work-center";
    public const string WorkerNotOnDutyAtWorkCenter = "worker-not-on-duty-at-work-center";
    public const string FinishedGoodsReceiptNotFound = "finished-goods-receipt-not-found";
    public const string PermissionDenied = "permission-denied";
    public const string ApprovalPrincipalUnresolved = "approval-principal-unresolved";
    public const string PrincipalUnresolved = "principal-unresolved";
    public const string MaintenanceActionOwnerRequired = "maintenance-action-owner-required";
    public const string AssignedInspectorNotFound = "assigned-inspector-not-found";
    public const string AssignedTeamNotActive = "assigned-team-not-active";
    public const string AssignedTeamNotFound = "assigned-team-not-found";
    public const string ResourceIdRequired = "resource-id-required";
    public const string AssignmentTargetOutsideSelectedScope = "assignment-target-outside-selected-scope";
    public const string WorkCenterRequiredUseV2 = "work-center-required-use-v2";
    public const string Forbidden = "forbidden";
}
