using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
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
            new ApprovedVerifier());

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

    private sealed class ApprovedVerifier : IEngineeringApprovalVerifier
    {
        public Task EnsureApprovedAsync(
            string organizationId,
            string environmentId,
            string approvalReferenceId,
            string changeNumber,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedBusinessDateProvider(DateOnly businessDate) : IProductEngineeringBusinessDateProvider
    {
        public DateOnly GetBusinessDate() => businessDate;
    }
}
