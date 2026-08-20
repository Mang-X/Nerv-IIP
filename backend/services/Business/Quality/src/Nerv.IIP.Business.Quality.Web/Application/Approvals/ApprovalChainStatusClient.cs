using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Quality.Web.Application.Approvals;

public interface IApprovalChainStatusClient
{
    Task<bool> IsApprovedForNcrDispositionAsync(
        string chainId,
        string organizationId,
        string environmentId,
        string ncrCode,
        CancellationToken cancellationToken);

    Task<bool> IsApprovedForCapaClosureAsync(
        string chainId,
        string organizationId,
        string environmentId,
        string capaCode,
        CancellationToken cancellationToken);
}

public sealed class HttpApprovalChainStatusClient(
    HttpClient httpClient,
    IInternalServiceTokenProvider tokenProvider) : IApprovalChainStatusClient
{
    /// <summary>
    /// 受理的审批链来源服务：权威码值 + 全部历史别名，取值来自审批契约的唯一事实来源
    /// （<see cref="ApprovalSourceServices.QualityAliases"/>，#1857 收敛，取值不变）。
    ///
    /// 收敛前这里是本地拼的三元素数组：领域常量 <c>QualityFacts.ServiceName</c>
    /// （跨服务不可见）+ 契约别名 + 裸字面量 <c>"quality"</c>——同一个「审批来源服务」概念
    /// 的取值面散在三处、权威不止一份。<c>QualityFacts.ServiceName</c> 与契约里的
    /// <see cref="ApprovalSourceServices.QualityServiceNameAlias"/> 的逐字相等
    /// 由 <c>ApprovalSourceServiceVocabularyContractTests</c> 对拍钉死。
    /// </summary>
    private static readonly IReadOnlySet<string> QualitySourceServices = ApprovalSourceServices.QualityAliases;

    /// <summary>
    /// 受理的 NCR 处置审批单据类型：权威码值 <c>ncr-disposition</c> + 历史别名，
    /// 取值来自审批契约的唯一事实来源（#1327：此处曾漏掉种子模板真正在用的 <c>ncr-disposition</c>，
    /// 于是种子态下处置审批永远判不通过）。
    /// </summary>
    private static readonly IReadOnlySet<string> NcrDispositionDocumentTypes = ApprovalDocumentTypes.NcrDispositionAliases;
    private static readonly IReadOnlySet<string> CapaClosureDocumentTypes = ApprovalDocumentTypes.CapaClosureAliases;

    public async Task<bool> IsApprovedForNcrDispositionAsync(
        string chainId,
        string organizationId,
        string environmentId,
        string ncrCode,
        CancellationToken cancellationToken)
    {
        var chain = await GetChainAsync(chainId, cancellationToken);
        return chain is not null
            && string.Equals(chain.Status, ApprovalChainStatuses.Approved, StringComparison.OrdinalIgnoreCase)
            && string.Equals(chain.OrganizationId, organizationId, StringComparison.Ordinal)
            && string.Equals(chain.EnvironmentId, environmentId, StringComparison.Ordinal)
            // SourceService / DocumentType 都来自反序列化，响应缺字段时是 null。
            // 这两行**不是**防异常兜底：实测 HashSet.Contains(null) 返回 false、并不抛
            // （旧注释「会抛」是错的，#1857 走查订正——真正抛的是 comparer.GetHashCode(null)，
            // 而 HashSet.Contains 有 null 短路，够不到 comparer）。
            // 它们承载的语义是「来源 / 单据类型缺失即判不通过，且**不得折叠成任何默认取值**」。
            // 这条语义由两道强度不同的护栏合守，别把任一道当成全部（#1857 二轮走查纠正）：
            //   · 两个字段声明成 string? → 删掉任一行即 CS8604 编译失败。编译器只强制「必须处理 null」，
            //     写成 `?? 某默认值` 一样能过——所以它守不住「必须拒绝」这一半；
            //   · 「必须拒绝」那一半由用例守：Missing_source_service_is_rejected_and_never_defaulted
            //     与 Missing_document_type_is_rejected_and_never_defaulted，对 `?? 默认值` 式重构变红。
            && chain.SourceService is not null
            && QualitySourceServices.Contains(chain.SourceService)
            && chain.DocumentType is not null
            && NcrDispositionDocumentTypes.Contains(chain.DocumentType)
            && string.Equals(chain.DocumentId, ncrCode, StringComparison.Ordinal);
    }

    public async Task<bool> IsApprovedForCapaClosureAsync(
        string chainId,
        string organizationId,
        string environmentId,
        string capaCode,
        CancellationToken cancellationToken)
    {
        var chain = await GetChainAsync(chainId, cancellationToken);
        return chain is not null
            && string.Equals(chain.Status, ApprovalChainStatuses.Approved, StringComparison.OrdinalIgnoreCase)
            && string.Equals(chain.OrganizationId, organizationId, StringComparison.Ordinal)
            && string.Equals(chain.EnvironmentId, environmentId, StringComparison.Ordinal)
            && chain.SourceService is not null
            && QualitySourceServices.Contains(chain.SourceService)
            && chain.DocumentType is not null
            && CapaClosureDocumentTypes.Contains(chain.DocumentType)
            && string.Equals(chain.DocumentId, capaCode, StringComparison.Ordinal);
    }

    private async Task<ApprovalChainStatusResponse?> GetChainAsync(string chainId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/business/v1/approvals/chains/{Uri.EscapeDataString(chainId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ApprovalChainStatusResponse>>(
            cancellationToken);
        return envelope?.Data;
    }

    private sealed record ApprovalChainStatusResponse(
        string ChainId,
        string OrganizationId,
        string EnvironmentId,
        string Status,
        // 缺字段时反序列化出 null，因此如实声明为可空——见上方两处判定里的裁决注释。
        string? SourceService,
        string? DocumentType,
        string DocumentId);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
