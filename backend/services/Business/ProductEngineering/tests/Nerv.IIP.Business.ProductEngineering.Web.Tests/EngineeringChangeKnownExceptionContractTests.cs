using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Queries;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Scheduling;

namespace Nerv.IIP.Business.ProductEngineering.Web.Tests;

public sealed class EngineeringChangeKnownExceptionContractTests
{
    [Fact]
    public async Task Public_release_hides_change_state_message_behind_stable_chinese_text()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new ApprovedVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-RELEASE-DRAFT",
                "Reject empty affected version set",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                []),
            CancellationToken.None));

        Assert.Equal("工程变更发布失败，请检查变更状态和受影响版本。", exception.Message);
        Assert.DoesNotContain("requires at least one affected version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Public_release_hides_domain_archive_message_behind_stable_chinese_text()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var draftBom = EngineeringBom.CreateDraft("org-001", "env-dev", "EBOM-DRAFT", "A", "ENG-3000")
            .AddLine("ENG-3001", 1m, "EA");
        dbContext.EngineeringBoms.Add(draftBom);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new ApprovedVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-DRAFT-ARCHIVE",
                "Reject draft archive as business error",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("engineering-bom", "EBOM-DRAFT:A")]),
            CancellationToken.None));

        Assert.Equal("工程 BOM 归档失败，请检查版本状态和替代版本。", exception.Message);
        Assert.DoesNotContain("Only released", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Public_release_active_production_successor_message_is_actionable_and_within_display_limit()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldVersion = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-OLD",
            "MBOM-OLD:A",
            "ROUTE-OLD:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            10,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        var successor = ProductionVersion.Create(
            "org-001",
            "env-dev",
            "SKU-NEW",
            "MBOM-NEW:A",
            "ROUTE-NEW:A",
            new DateOnly(2026, 1, 1),
            null,
            null,
            null,
            20,
            true,
            EngineeringVersionStatus.Published,
            EngineeringVersionStatus.Published);
        dbContext.ProductionVersions.AddRange(oldVersion, successor);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new ApprovedVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)));

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-PV-SKU-MISMATCH",
                "Reject successor SKU mismatch",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand("production-version", oldVersion.Id.Id.ToString("D"), successor.Id.Id.ToString("D"))]),
            CancellationToken.None));

        Assert.Equal("替代生产版本的 SKU 或状态不符合要求，请检查替代版本。", exception.Message);
        Assert.Contains("SKU", exception.Message, StringComparison.Ordinal);
        Assert.Contains("状态", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length <= 60, $"消息 {exception.Message.Length} 字，超过前端 60 字透传上限");
        Assert.DoesNotContain(successor.Id.Id.ToString("D"), exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unsupported", "engineering-change", "EC-001", "受影响版本 'engineering-change:EC-001' 不受支持，请检查提交内容。")]
    [InlineData("self", "engineering-bom", "EBOM-SELF:A", "受影响版本 'engineering-bom:EBOM-SELF:A' 不能将自身设为替代版本，请修改替代版本。")]
    [InlineData("duplicate-different-successor", "engineering-bom", "EBOM-DUP:A", "受影响版本 'engineering-bom:EBOM-DUP:A' 已指定其他替代版本，请删除重复项。")]
    [InlineData("duplicate-same-successor", "engineering-bom", "EBOM-DUP:A", "受影响版本 'engineering-bom:EBOM-DUP:A' 重复声明，请保留一项。")]
    [InlineData("cycle", "engineering-bom", "EBOM-CYCLE:A", "受影响版本 'engineering-bom:EBOM-CYCLE:A' 的替代关系形成循环，请修改替代版本。")]
    public async Task Public_release_batch_validation_names_the_affected_version_and_next_action(
        string caseName,
        string versionKind,
        string versionId,
        string expectedMessage)
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var handler = CreateHandler(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        IReadOnlyCollection<AffectedVersionCommand> affectedVersions = caseName switch
        {
            "unsupported" => [new AffectedVersionCommand(versionKind, versionId)],
            "self" => [new AffectedVersionCommand(versionKind, versionId, versionId)],
            "duplicate-different-successor" => [
                new AffectedVersionCommand(versionKind, versionId, "EBOM-A:A"),
                new AffectedVersionCommand(versionKind, versionId, "EBOM-B:A")],
            "duplicate-same-successor" => [
                new AffectedVersionCommand(versionKind, versionId, "EBOM-A:A"),
                new AffectedVersionCommand(versionKind, versionId, "EBOM-A:A")],
            "cycle" => [
                new AffectedVersionCommand(versionKind, versionId, "EBOM-CYCLE:B"),
                new AffectedVersionCommand(versionKind, "EBOM-CYCLE:B", versionId)],
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                $"ECO-BATCH-{caseName}",
                "Reject invalid affected version batch",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                affectedVersions),
            CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.Contains(versionKind, exception.Message, StringComparison.Ordinal);
        Assert.Contains(versionId, exception.Message, StringComparison.Ordinal);
        Assert.Contains("请", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Public_release_batch_validation_uses_estimated_display_budget_for_maximum_version_id()
    {
        const string versionKind = "engineering-bom";
        var versionId = new string('V', 150);
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var handler = CreateHandler(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new ReleaseEngineeringChangeCommand(
                "org-001",
                "env-dev",
                "ECO-BATCH-LONG-ID",
                "Reject self supersede with maximum identifier",
                Guid.NewGuid().ToString("D"),
                new DateOnly(2026, 6, 1),
                [new AffectedVersionCommand(versionKind, versionId, versionId)]),
            CancellationToken.None));

        Assert.Contains($"{versionKind}:{versionId}", exception.Message, StringComparison.Ordinal);
        Assert.Contains("不能将自身设为替代版本", exception.Message, StringComparison.Ordinal);
        Assert.True(exception.Message.Length > 60, "最大 150 字符 VersionId 的运行时文案不应伪称严格 <=60 字符。");
    }

    [Fact]
    public async Task Public_cancel_and_reschedule_hide_domain_state_messages_behind_stable_chinese_text()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.EngineeringChanges.AddRange(
            EngineeringChange.Open("org-001", "env-dev", "ECO-CANCEL-DRAFT", "Cancel draft"),
            EngineeringChange.Open("org-001", "env-dev", "ECO-RESCHEDULE-DRAFT", "Reschedule draft"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var cancelException = await Assert.ThrowsAsync<KnownException>(() => new CancelScheduledEngineeringChangeCommandHandler(dbContext).Handle(
            new CancelScheduledEngineeringChangeCommand("org-001", "env-dev", "ECO-CANCEL-DRAFT", "operator cancelled"),
            CancellationToken.None));
        var rescheduleException = await Assert.ThrowsAsync<KnownException>(() => new RescheduleEngineeringChangeCommandHandler(dbContext).Handle(
            new RescheduleEngineeringChangeCommand("org-001", "env-dev", "ECO-RESCHEDULE-DRAFT", new DateOnly(2026, 6, 10), "supplier delay"),
            CancellationToken.None));

        Assert.Equal("取消工程变更失败，请确认变更处于已排期状态。", cancelException.Message);
        Assert.Equal("改期工程变更失败，请确认变更处于已排期状态。", rescheduleException.Message);
        Assert.Equal("Only scheduled engineering changes can be changed by this operation.", cancelException.InnerException?.Message);
        Assert.Equal("Only scheduled engineering changes can be changed by this operation.", rescheduleException.InnerException?.Message);
    }

    [Fact]
    public async Task Public_engineering_change_queries_use_chinese_not_found_messages()
    {
        await using var provider = CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var getException = await Assert.ThrowsAsync<KnownException>(() => new GetEngineeringChangeQueryHandler(dbContext).Handle(
            new GetEngineeringChangeQuery("org-001", "env-dev", "ECO-MISSING"),
            CancellationToken.None));
        var previewException = await Assert.ThrowsAsync<KnownException>(() => new GetEngineeringChangeImpactPreviewQueryHandler(dbContext).Handle(
            new GetEngineeringChangeImpactPreviewQuery("org-001", "env-dev", new DateOnly(2026, 6, 1), []),
            CancellationToken.None));

        Assert.Equal("工程变更 'ECO-MISSING' 不存在。", getException.Message);
        Assert.Equal("影响预览至少需要一个受影响版本。", previewException.Message);
    }

    private static ServiceProvider CreateInMemoryProvider()
    {
        var databaseName = $"product-engineering-known-exception-contract-{Guid.NewGuid():N}";
        return new ServiceCollection()
            .AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(Program).Assembly))
            .AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();
    }

    private static ReleaseEngineeringChangeCommandHandler CreateHandler(ApplicationDbContext dbContext)
    {
        return new ReleaseEngineeringChangeCommandHandler(
            new EngineeringChangeRepository(dbContext),
            new EngineeringBomRepository(dbContext),
            new ManufacturingBomRepository(dbContext),
            new RoutingRepository(dbContext),
            new ProductionVersionRepository(dbContext),
            new ApprovedVerifier(),
            businessDateProvider: new FixedBusinessDateProvider(new DateOnly(2026, 6, 1)));
    }

    private sealed class ApprovedVerifier : IEngineeringApprovalVerifier
    {
        public Task EnsureApprovedAsync(
            string organizationId,
            string environmentId,
            string approvalReferenceId,
            string changeNumber,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
