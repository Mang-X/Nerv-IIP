using System.Net;
using System.Text.Json;
using Nerv.IIP.Business.Quality.Domain;
using Nerv.IIP.Business.Quality.Web.Application.Approvals;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// #1857 · 审批链**状态**与**来源服务受理集合**下沉进契约后的回归锁。
///
/// 三件事各自钉死：
/// <list type="bullet">
/// <item>放行判定比的是 <c>ApprovalChainStatuses.Approved</c>——桩里的 <c>status</c>
/// 是写死的字面量 <c>"approved"</c>，把契约常量改成事故值本文件立刻红（变异哨兵）；</item>
/// <item><c>ApprovalSourceServices.QualityAliases</c> 的三个成员各自对应一种历史拼写，
/// 少任何一个都会让对应拼写落库的既有链判不通过——逐成员各一条用例（变异哨兵）；</item>
/// <item>契约里的 <c>QualityServiceNameAlias</c> 与 Quality 领域层的 <c>QualityFacts.ServiceName</c>
/// 是同一取值的两份，逐字对拍（副本一致性）。</item>
/// </list>
/// </summary>
public sealed class QualityApprovalChainVocabularyContractTests
{
    /// <summary>
    /// 副本对拍：契约是权威、领域层那份是副本，两侧必须逐字相等。
    /// 领域常量跨服务不可见，所以受理集合只能建在契约侧；对拍是防这两份静默分叉的唯一手段。
    /// </summary>
    [Fact]
    public void Contract_alias_stays_identical_to_the_quality_domain_service_name()
    {
        Assert.Equal(QualityFacts.ServiceName, ApprovalSourceServices.QualityServiceNameAlias);
        Assert.Contains(QualityFacts.ServiceName, ApprovalSourceServices.QualityAliases);
    }

    /// <summary>
    /// 链状态词表与「审批结果」词表**不是同一个取值面**：前者多出 <c>pending</c> 与 <c>withdrawn</c>。
    /// 本用例是「不得借用 <c>ApprovalResults.Approved</c>」这条裁决的可执行形式——
    /// 若有人把两者合并成一份，成员差立刻消失、用例变红。
    /// </summary>
    [Fact]
    public void Chain_status_vocabulary_is_not_the_approval_result_vocabulary()
    {
        var resultValues = new[] { ApprovalResults.Approved, ApprovalResults.Rejected, ApprovalResults.Returned };

        Assert.DoesNotContain(ApprovalChainStatuses.Pending, resultValues);
        Assert.DoesNotContain(ApprovalChainStatuses.Withdrawn, resultValues);
    }

    [Fact]
    public async Task Approved_chain_from_the_canonical_source_service_is_accepted()
    {
        Assert.True(await CreateClient("approved", ApprovalSourceServices.Quality)
            .IsApprovedForNcrDispositionAsync("chain-001", "org-001", "env-dev", "NCR-2026-0001", CancellationToken.None));
    }

    /// <summary>
    /// 受理集合的三个成员各自代表一种历史拼写：<c>quality</c>（权威）、
    /// <c>business-quality</c>（事件信封形态的旧别名）、<c>BusinessQuality</c>（领域标识形态的旧别名）。
    /// 集合按序数忽略大小写去重，后两者因连字符互不相等——任一成员被删或被 <c>Quality</c> 顶替，
    /// 对应用例即红（集合会静默退化成重复项、丢掉一种拼写）。
    /// </summary>
    [Theory]
    [InlineData("quality")]
    [InlineData("business-quality")]
    [InlineData("BusinessQuality")]
    public async Task Legacy_source_services_stay_accepted_for_chains_started_before_the_convergence(string sourceService)
    {
        Assert.True(await CreateClient("approved", sourceService)
            .IsApprovedForNcrDispositionAsync("chain-001", "org-001", "env-dev", "NCR-2026-0001", CancellationToken.None));
        Assert.True(await CreateClient("approved", sourceService)
            .IsApprovedForCapaClosureAsync("chain-002", "org-001", "env-dev", "CAPA-2026-0001", CancellationToken.None));
    }

    [Fact]
    public async Task Unrelated_source_service_is_still_rejected()
    {
        Assert.False(await CreateClient("approved", ApprovalSourceServices.BusinessErp)
            .IsApprovedForNcrDispositionAsync("chain-001", "org-001", "env-dev", "NCR-2026-0001", CancellationToken.None));
    }

    /// <summary>
    /// 非 <c>approved</c> 的链状态一律判不通过，含「审批结果」词表根本没有的
    /// <c>pending</c> / <c>withdrawn</c> 两态。
    /// </summary>
    [Theory]
    [InlineData("pending")]
    [InlineData("rejected")]
    [InlineData("returned")]
    [InlineData("withdrawn")]
    public async Task Chains_that_are_not_approved_are_rejected(string status)
    {
        Assert.False(await CreateClient(status, ApprovalSourceServices.Quality)
            .IsApprovedForNcrDispositionAsync("chain-001", "org-001", "env-dev", "NCR-2026-0001", CancellationToken.None));
        Assert.False(await CreateClient(status, ApprovalSourceServices.Quality)
            .IsApprovedForCapaClosureAsync("chain-002", "org-001", "env-dev", "CAPA-2026-0001", CancellationToken.None));
    }

    /// <summary>
    /// 审批链响应缺 <c>sourceService</c> 字段时反序列化出 null：判定必须是「不通过」，
    /// 而不是让 <c>HashSet.Contains(null)</c> 抛 ArgumentNullException 冒成 500
    /// （受理集合从 <c>string[]</c> 换成 <c>IReadOnlySet&lt;string&gt;</c> 后新出现的边界，
    /// 与 <c>documentType</c> 那侧同一裁决）。
    /// </summary>
    [Fact]
    public async Task Missing_source_service_is_rejected_instead_of_throwing()
    {
        Assert.False(await CreateClient("approved", sourceService: null)
            .IsApprovedForNcrDispositionAsync("chain-001", "org-001", "env-dev", "NCR-2026-0001", CancellationToken.None));
    }

    private static HttpApprovalChainStatusClient CreateClient(string status, string? sourceService) =>
        new(
            new HttpClient(new StubHandler(status, sourceService))
            {
                BaseAddress = new Uri("http://approval.test"),
            },
            new StubTokenProvider());

    private sealed class StubHandler(string status, string? sourceService) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var isCapa = request.RequestUri!.AbsolutePath.EndsWith("chain-002", StringComparison.Ordinal);
            var payload = JsonSerializer.Serialize(new
            {
                success = true,
                message = string.Empty,
                code = 0,
                data = new
                {
                    chainId = isCapa ? "chain-002" : "chain-001",
                    organizationId = "org-001",
                    environmentId = "env-dev",
                    status,
                    sourceService,
                    documentType = isCapa ? ApprovalDocumentTypes.CapaClosure : ApprovalDocumentTypes.NcrDisposition,
                    documentId = isCapa ? "CAPA-2026-0001" : "NCR-2026-0001",
                },
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubTokenProvider : IInternalServiceTokenProvider
    {
        public string BearerToken => "test-token";
    }
}
