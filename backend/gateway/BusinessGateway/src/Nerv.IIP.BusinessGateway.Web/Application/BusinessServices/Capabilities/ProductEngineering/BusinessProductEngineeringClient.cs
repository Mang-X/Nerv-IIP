namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;


public interface IBusinessProductEngineeringClient
{
    Task<BusinessConsoleEngineeringEntityResponse> RegisterEngineeringDocumentAsync(
        string internalBearerToken,
        BusinessConsoleRegisterEngineeringDocumentRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> PublishSopDocumentAsync(
        string internalBearerToken,
        BusinessConsolePublishSopDocumentRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCurrentSopDocumentsResponse> GetCurrentSopDocumentsAsync(
        string internalBearerToken,
        BusinessConsoleCurrentSopDocumentsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringDocumentListResponse> ListEngineeringDocumentsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringDocumentsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringDocumentItem> GetEngineeringDocumentAsync(
        string internalBearerToken,
        string documentNumber,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> CreateEngineeringItemRevisionAsync(
        string internalBearerToken,
        BusinessConsoleCreateEngineeringItemRevisionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringItemListResponse> ListEngineeringItemsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringItemsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringItemRevisionItem> GetEngineeringItemAsync(
        string internalBearerToken,
        string itemCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringBomRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringBomListResponse> ListEngineeringBomsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringBomsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringBomItem> GetEngineeringBomAsync(
        string internalBearerToken,
        string bomCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomExplosionResponse> GetEngineeringBomExplosionAsync(
        string internalBearerToken,
        BusinessConsoleBomExplosionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomWhereUsedResponse> GetEngineeringBomWhereUsedAsync(
        string internalBearerToken,
        BusinessConsoleBomWhereUsedRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomDiffResponse> GetBomDiffAsync(
        string internalBearerToken,
        BusinessConsoleBomDiffRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleManufacturingBomListResponse> ListManufacturingBomsAsync(
        string internalBearerToken,
        BusinessConsoleListManufacturingBomsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleManufacturingBomItem> GetManufacturingBomAsync(
        string internalBearerToken,
        string bomCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomExplosionResponse> GetManufacturingBomExplosionAsync(
        string internalBearerToken,
        BusinessConsoleManufacturingBomExplosionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleBomWhereUsedResponse> GetManufacturingBomWhereUsedAsync(
        string internalBearerToken,
        BusinessConsoleBomWhereUsedRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleReleasedEngineeringVersionResponse> ReleaseManufacturingBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseManufacturingBomRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRoutingListResponse> ListRoutingsAsync(
        string internalBearerToken,
        BusinessConsoleListRoutingsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleRoutingItem> GetRoutingAsync(
        string internalBearerToken,
        string routingCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleReleasedEngineeringVersionResponse> ReleaseRoutingAsync(
        string internalBearerToken,
        BusinessConsoleReleaseRoutingRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStandardOperationListResponse> ListStandardOperationsAsync(
        string internalBearerToken,
        BusinessConsoleListStandardOperationsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStandardOperationItem> GetStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStandardOperationResponse> CreateStandardOperationAsync(
        string internalBearerToken,
        BusinessConsoleCreateStandardOperationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleStandardOperationResponse> UpdateStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleUpdateStandardOperationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ArchiveStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleArchiveStandardOperationRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringChangeRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> CancelScheduledEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleCancelScheduledEngineeringChangeRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringEntityResponse> RescheduleEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleRescheduleEngineeringChangeRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringChangeImpactPreviewResponse> PreviewEngineeringChangeImpactAsync(
        string internalBearerToken,
        BusinessConsoleEngineeringChangeImpactPreviewRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringChangeListResponse> ListEngineeringChangesAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringChangesRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleEngineeringChangeItem> GetEngineeringChangeAsync(
        string internalBearerToken,
        string changeNumber,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleProductionVersionListResponse> ListProductionVersionsAsync(
        string internalBearerToken,
        BusinessConsoleListProductionVersionsRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleResolveProductionVersionResponse> ResolveProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleResolveProductionVersionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateProductionVersionResponse> CreateProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionVersionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleCreateProductionVersionResponse> UpdateProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleUpdateProductionVersionRequest request,
        CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> ArchiveProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleArchiveProductionVersionRequest request,
        CancellationToken cancellationToken);
}


public sealed class HttpBusinessProductEngineeringClient(HttpClient httpClient)
    : BusinessServiceHttpClient(httpClient), IBusinessProductEngineeringClient
{
    public Task<BusinessConsoleEngineeringEntityResponse> RegisterEngineeringDocumentAsync(
        string internalBearerToken,
        BusinessConsoleRegisterEngineeringDocumentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/documents",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> PublishSopDocumentAsync(
        string internalBearerToken,
        BusinessConsolePublishSopDocumentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/sops/publish",
            request,
            cancellationToken);

    public Task<BusinessConsoleCurrentSopDocumentsResponse> GetCurrentSopDocumentsAsync(
        string internalBearerToken,
        BusinessConsoleCurrentSopDocumentsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCurrentSopDocumentsResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/sops/current?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("operationCode", request.OperationCode),
                ("workCenterCode", request.WorkCenterCode),
                ("routingCode", request.RoutingCode),
                ("routingRevision", request.RoutingRevision),
                ("asOfDate", request.AsOfDate)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringDocumentListResponse> ListEngineeringDocumentsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringDocumentsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringDocumentListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/documents?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("itemCode", request.ItemCode),
                ("documentType", request.DocumentType),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringDocumentItem> GetEngineeringDocumentAsync(
        string internalBearerToken,
        string documentNumber,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringDocumentItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/documents/{Uri.EscapeDataString(documentNumber)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> CreateEngineeringItemRevisionAsync(
        string internalBearerToken,
        BusinessConsoleCreateEngineeringItemRevisionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/items",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringItemListResponse> ListEngineeringItemsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringItemsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringItemListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/items?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("itemCode", request.ItemCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringItemRevisionItem> GetEngineeringItemAsync(
        string internalBearerToken,
        string itemCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringItemRevisionItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/items/{Uri.EscapeDataString(itemCode)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringBomRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-boms/release",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringBomListResponse> ListEngineeringBomsAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringBomsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringBomListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/engineering-boms?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("parentItemCode", request.ParentItemCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringBomItem> GetEngineeringBomAsync(
        string internalBearerToken,
        string bomCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringBomItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/engineering-boms/{Uri.EscapeDataString(bomCode)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomExplosionResponse> GetEngineeringBomExplosionAsync(
        string internalBearerToken,
        BusinessConsoleBomExplosionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomExplosionResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/engineering-boms/explosion?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("itemCode", request.ItemCode),
                ("effectiveDate", request.EffectiveDate),
                ("lotSize", request.LotSize),
                ("bomCode", request.BomCode),
                ("revision", request.Revision)),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomWhereUsedResponse> GetEngineeringBomWhereUsedAsync(
        string internalBearerToken,
        BusinessConsoleBomWhereUsedRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomWhereUsedResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/engineering-boms/where-used?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("componentCode", request.ComponentCode),
                ("effectiveDate", request.EffectiveDate)),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomDiffResponse> GetBomDiffAsync(
        string internalBearerToken,
        BusinessConsoleBomDiffRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomDiffResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/boms/diff?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("bomKind", request.BomKind),
                ("fromBomCode", request.FromBomCode),
                ("fromRevision", request.FromRevision),
                ("toBomCode", request.ToBomCode),
                ("toRevision", request.ToRevision)),
            null,
            cancellationToken);

    public Task<BusinessConsoleManufacturingBomListResponse> ListManufacturingBomsAsync(
        string internalBearerToken,
        BusinessConsoleListManufacturingBomsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleManufacturingBomListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/manufacturing-boms?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleManufacturingBomItem> GetManufacturingBomAsync(
        string internalBearerToken,
        string bomCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleManufacturingBomItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/manufacturing-boms/{Uri.EscapeDataString(bomCode)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomExplosionResponse> GetManufacturingBomExplosionAsync(
        string internalBearerToken,
        BusinessConsoleManufacturingBomExplosionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomExplosionResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/manufacturing-boms/explosion?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("effectiveDate", request.EffectiveDate),
                ("lotSize", request.LotSize),
                ("bomCode", request.BomCode),
                ("revision", request.Revision)),
            null,
            cancellationToken);

    public Task<BusinessConsoleBomWhereUsedResponse> GetManufacturingBomWhereUsedAsync(
        string internalBearerToken,
        BusinessConsoleBomWhereUsedRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleBomWhereUsedResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/manufacturing-boms/where-used?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("componentCode", request.ComponentCode),
                ("effectiveDate", request.EffectiveDate)),
            null,
            cancellationToken);

    public Task<BusinessConsoleReleasedEngineeringVersionResponse> ReleaseManufacturingBomAsync(
        string internalBearerToken,
        BusinessConsoleReleaseManufacturingBomRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleReleasedEngineeringVersionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/manufacturing-boms/release",
            request,
            cancellationToken);

    public Task<BusinessConsoleRoutingListResponse> ListRoutingsAsync(
        string internalBearerToken,
        BusinessConsoleListRoutingsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRoutingListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/routings?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleRoutingItem> GetRoutingAsync(
        string internalBearerToken,
        string routingCode,
        string revision,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleRoutingItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/routings/{Uri.EscapeDataString(routingCode)}/{Uri.EscapeDataString(revision)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleReleasedEngineeringVersionResponse> ReleaseRoutingAsync(
        string internalBearerToken,
        BusinessConsoleReleaseRoutingRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleReleasedEngineeringVersionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/routings/release",
            request,
            cancellationToken);

    public Task<BusinessConsoleStandardOperationListResponse> ListStandardOperationsAsync(
        string internalBearerToken,
        BusinessConsoleListStandardOperationsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStandardOperationListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/standard-operations?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("enabled", request.Enabled),
                ("search", request.Search),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleStandardOperationItem> GetStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStandardOperationItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/standard-operations/{Uri.EscapeDataString(operationCode)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleStandardOperationResponse> CreateStandardOperationAsync(
        string internalBearerToken,
        BusinessConsoleCreateStandardOperationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStandardOperationResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/standard-operations",
            request,
            cancellationToken);

    public Task<BusinessConsoleStandardOperationResponse> UpdateStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleUpdateStandardOperationRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleStandardOperationResponse>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/engineering/standard-operations/{Uri.EscapeDataString(operationCode)}",
            request with { OperationCode = operationCode },
            cancellationToken);

    public async Task<BusinessConsoleAcceptedResponse> ArchiveStandardOperationAsync(
        string internalBearerToken,
        string operationCode,
        BusinessConsoleArchiveStandardOperationRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<object>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/engineering/standard-operations/{Uri.EscapeDataString(operationCode)}/archive",
            request with { OperationCode = operationCode },
            cancellationToken);
        return new BusinessConsoleAcceptedResponse(true);
    }

    public Task<BusinessConsoleEngineeringEntityResponse> ReleaseEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleReleaseEngineeringChangeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-changes/release",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> CancelScheduledEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleCancelScheduledEngineeringChangeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-changes/cancel-scheduled",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringEntityResponse> RescheduleEngineeringChangeAsync(
        string internalBearerToken,
        BusinessConsoleRescheduleEngineeringChangeRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringEntityResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-changes/reschedule",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringChangeImpactPreviewResponse> PreviewEngineeringChangeImpactAsync(
        string internalBearerToken,
        BusinessConsoleEngineeringChangeImpactPreviewRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringChangeImpactPreviewResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/engineering-changes/impact-preview",
            request,
            cancellationToken);

    public Task<BusinessConsoleEngineeringChangeListResponse> ListEngineeringChangesAsync(
        string internalBearerToken,
        BusinessConsoleListEngineeringChangesRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringChangeListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/engineering-changes?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleEngineeringChangeItem> GetEngineeringChangeAsync(
        string internalBearerToken,
        string changeNumber,
        BusinessConsoleEngineeringContextRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleEngineeringChangeItem>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/engineering/engineering-changes/{Uri.EscapeDataString(changeNumber)}?" + ContextQuery(request.OrganizationId, request.EnvironmentId),
            null,
            cancellationToken);

    public Task<BusinessConsoleProductionVersionListResponse> ListProductionVersionsAsync(
        string internalBearerToken,
        BusinessConsoleListProductionVersionsRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleProductionVersionListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/production-versions?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("status", request.Status),
                ("skip", request.Skip),
                ("take", request.Take)),
            null,
            cancellationToken);

    public Task<BusinessConsoleResolveProductionVersionResponse> ResolveProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleResolveProductionVersionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResolveProductionVersionResponse>(
            internalBearerToken,
            HttpMethod.Get,
            "/api/business/v1/engineering/production-versions/resolve?" + Query(
                ("organizationId", request.OrganizationId),
                ("environmentId", request.EnvironmentId),
                ("skuCode", request.SkuCode),
                ("effectiveDate", request.EffectiveDate),
                ("lotSize", request.LotSize)),
            null,
            cancellationToken);

    public Task<BusinessConsoleCreateProductionVersionResponse> CreateProductionVersionAsync(
        string internalBearerToken,
        BusinessConsoleCreateProductionVersionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateProductionVersionResponse>(
            internalBearerToken,
            HttpMethod.Post,
            "/api/business/v1/engineering/production-versions",
            request,
            cancellationToken);

    public Task<BusinessConsoleCreateProductionVersionResponse> UpdateProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleUpdateProductionVersionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleCreateProductionVersionResponse>(
            internalBearerToken,
            HttpMethod.Put,
            $"/api/business/v1/engineering/production-versions/{Uri.EscapeDataString(productionVersionId)}",
            request with { ProductionVersionId = productionVersionId },
            cancellationToken);

    public async Task<BusinessConsoleAcceptedResponse> ArchiveProductionVersionAsync(
        string internalBearerToken,
        string productionVersionId,
        BusinessConsoleArchiveProductionVersionRequest request,
        CancellationToken cancellationToken)
    {
        await SendAsync<object>(
            internalBearerToken,
            HttpMethod.Post,
            $"/api/business/v1/engineering/production-versions/{Uri.EscapeDataString(productionVersionId)}/archive",
            new DownstreamArchiveProductionVersionRequest(request.OrganizationId, request.EnvironmentId, productionVersionId, request.Reason),
            cancellationToken);
        return new BusinessConsoleAcceptedResponse(true);
    }

    private static string ContextQuery(string organizationId, string environmentId) =>
        Query(("organizationId", organizationId), ("environmentId", environmentId));

    private sealed record DownstreamArchiveProductionVersionRequest(
        string OrganizationId,
        string EnvironmentId,
        string ProductionVersionId,
        string Reason);
}
