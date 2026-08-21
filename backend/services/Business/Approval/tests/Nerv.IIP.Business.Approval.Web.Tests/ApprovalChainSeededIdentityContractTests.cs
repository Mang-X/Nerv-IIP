using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalChainAggregate;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Web.Application.Seed;
using Nerv.IIP.Contracts.Approval;

namespace Nerv.IIP.Business.Approval.Web.Tests;

/// <summary>
/// #1684 · <c>ApprovalChain</c> 确定性 id 入口的防误用契约（命名 + XML doc 之外的第三重）：
/// 生产路径 <c>Start</c> 的链 id 版本位钉在 7；<c>StartWithSeededIdentity</c> 只接受非空 id
/// 并原样落到聚合；该入口在生产代码里的引用面由源码扫描钉死为「世界观审批种子」一处。
/// </summary>
public sealed class ApprovalChainSeededIdentityContractTests
{
    /// <summary>生产路径一个字不改：<c>Start</c> 产出的链 id 仍是 <c>Guid.CreateVersion7()</c>（版本位 7）。</summary>
    [Fact]
    public void Production_start_still_creates_version7_chain_ids()
    {
        var chain = ApprovalChain.Start(Template(), Reference("NCR-2026-0001"), "user:user-emp-040");

        Assert.Equal(7, chain.Id.Id.Version);
        Assert.NotEqual(Guid.Empty, chain.Id.Id);
    }

    [Fact]
    public void Seeded_identity_entry_applies_the_caller_supplied_deterministic_id()
    {
        var seededId = new ApprovalChainId(
            WorldHistoryNcrDispositionApprovals.SeededDispositionChainId("NCR-2026-0001"));

        var chain = ApprovalChain.StartWithSeededIdentity(
            seededId,
            Template(),
            Reference("NCR-2026-0001"),
            "user:user-emp-040");

        Assert.Equal(seededId, chain.Id);
        Assert.Equal(ApprovalChainStatuses.Pending, chain.Status);
        Assert.NotEmpty(chain.Steps);
    }

    [Fact]
    public void Seeded_identity_entry_rejects_an_empty_id()
    {
        var exception = Assert.Throws<ArgumentException>(() => ApprovalChain.StartWithSeededIdentity(
            new ApprovalChainId(Guid.Empty),
            Template(),
            Reference("NCR-2026-0001"),
            "user:user-emp-040"));

        Assert.Contains("空 Guid", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>同一 NCR 单号永远得到同一链 id，不同单号必须得到不同链 id（回链可解析的前提）。</summary>
    [Fact]
    public void Seeded_identity_formula_is_deterministic_and_distinct_per_ncr()
    {
        Assert.Equal(
            WorldHistoryNcrDispositionApprovals.SeededDispositionChainId("NCR-2026-0001"),
            WorldHistoryNcrDispositionApprovals.SeededDispositionChainId("NCR-2026-0001"));
        Assert.NotEqual(
            WorldHistoryNcrDispositionApprovals.SeededDispositionChainId("NCR-2026-0001"),
            WorldHistoryNcrDispositionApprovals.SeededDispositionChainId("NCR-2026-0002"));
    }

    /// <summary>
    /// 源码扫描：<c>StartWithSeededIdentity</c> 在生产代码（<c>backend/</c> 下非 <c>tests</c> 目录）里
    /// 只允许出现在定义处（<c>ApprovalChain.cs</c>）与世界观审批种子
    /// （<c>WorldHistoryApprovalSeedService.cs</c>）；其他任何生产文件引用即红。
    /// </summary>
    [Fact]
    public void Seeded_identity_entry_is_only_referenced_by_the_world_history_seed()
    {
        var backendRoot = BackendRoot();
        var allowed = new[]
        {
            Path.Combine(
                "services", "Business", "Approval", "src", "Nerv.IIP.Business.Approval.Domain",
                "AggregatesModel", "ApprovalChainAggregate", "ApprovalChain.cs"),
            Path.Combine(
                "services", "Business", "Approval", "src", "Nerv.IIP.Business.Approval.Web",
                "Application", "Seed", "WorldHistoryApprovalSeedService.cs"),
            // 契约公式的 XML doc 里提及该入口名（仅文档引用，非调用）。
            Path.Combine(
                "common", "Contracts", "Nerv.IIP.Contracts.Approval",
                "WorldHistoryNcrDispositionApprovals.cs"),
        };

        var offenders = Directory.EnumerateFiles(backendRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(backendRoot, file))
            .Where(relative =>
            {
                var segments = relative.Split(Path.DirectorySeparatorChar);
                return !segments.Contains("obj", StringComparer.Ordinal)
                    && !segments.Contains("bin", StringComparer.Ordinal)
                    && !segments.Contains("tests", StringComparer.Ordinal);
            })
            .Where(relative => File.ReadAllText(Path.Combine(backendRoot, relative))
                .Contains("StartWithSeededIdentity", StringComparison.Ordinal))
            .Where(relative => !allowed.Contains(relative, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "StartWithSeededIdentity 是仅限世界观种子的确定性 id 入口，禁止其他生产代码引用；越界文件：" +
            string.Join("; ", offenders));
    }

    private static ApprovalTemplate Template() =>
        ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            WorldHistoryNcrDispositionApprovals.LegacyNcrDispositionTemplateCode,
            ApprovalDocumentTypes.NcrDisposition,
            version: 1,
            isActive: true,
            [
                new ApprovalTemplateStepDefinition(
                    StepNo: 1,
                    StepName: "质量主管评审",
                    ParallelGroupKey: null,
                    ApproverType: "user",
                    ApproverRef: "user-emp-033",
                    DueInHours: 24),
            ]);

    private static ApprovalDocumentReference Reference(string documentId) =>
        new(
            WorldHistoryApprovalSpec.NcrSourceService,
            ApprovalDocumentTypes.NcrDisposition,
            documentId,
            documentLineId: null);

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "backend", "services", "Business", "Approval");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "backend");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the backend directory from the test output directory.");
    }
}
