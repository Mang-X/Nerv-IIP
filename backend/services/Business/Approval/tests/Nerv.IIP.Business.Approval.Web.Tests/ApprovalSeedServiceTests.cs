using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalTemplateAggregate;
using Nerv.IIP.Business.Approval.Infrastructure;
using Nerv.IIP.Business.Approval.Web.Application.Seed;
using Nerv.IIP.Contracts.Approval;

namespace Nerv.IIP.Business.Approval.Web.Tests;

public sealed class ApprovalSeedServiceTests
{
    private static readonly string[] ProductTemplateCodes =
    [
        "purchase-order-release",
        "purchase-order-change",
        "ncr-disposition",
        "erp-sales-credit-release",
        "stock-count-variance",
        "engineering-change-order",
    ];

    [Fact]
    public async Task Product_seed_writes_all_six_templates_without_world_history()
    {
        await using var db = CreateDbContext();

        var written = await new ApprovalSeedService(db).SeedAsync("org-001", "env-dev");

        var templates = await db.ApprovalTemplates
            .AsNoTracking()
            .Include(x => x.Steps)
            .OrderBy(x => x.TemplateCode)
            .ToArrayAsync();
        Assert.Equal(6, written);
        Assert.Equal(ProductTemplateCodes.Order(StringComparer.Ordinal), templates.Select(x => x.TemplateCode));
        Assert.DoesNotContain(templates, x => x.TemplateCode.StartsWith("APT-WB-", StringComparison.Ordinal));
        Assert.All(templates, template =>
        {
            Assert.True(template.IsActive);
            Assert.Equal(1, template.Version);
            var step = Assert.Single(template.Steps);
            Assert.Equal("user", step.ApproverType);
            Assert.Equal("user-admin", step.ApproverRef);
        });
    }

    [Fact]
    public async Task Product_seed_is_idempotent_and_never_overwrites_tenant_facts()
    {
        await using var db = CreateDbContext();
        var tenantTemplate = ApprovalTemplate.Create(
            "org-001",
            "env-dev",
            ApprovalTemplateCodes.PurchaseOrderRelease,
            "tenant-purchase-document",
            version: 7,
            isActive: false,
            [new ApprovalTemplateStepDefinition(1, "租户自定义复核", null, "user", "tenant-approver", 72)]);
        var tenantTemplateId = tenantTemplate.Id;
        db.ApprovalTemplates.Add(tenantTemplate);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var firstWritten = await new ApprovalSeedService(db).SeedAsync("org-001", "env-dev");
        db.ChangeTracker.Clear();
        var secondWritten = await new ApprovalSeedService(db).SeedAsync("org-001", "env-dev");
        db.ChangeTracker.Clear();

        var preserved = await db.ApprovalTemplates
            .AsNoTracking()
            .Include(x => x.Steps)
            .SingleAsync(x => x.TemplateCode == ApprovalTemplateCodes.PurchaseOrderRelease);
        Assert.Equal(5, firstWritten);
        Assert.Equal(0, secondWritten);
        Assert.Equal(6, await db.ApprovalTemplates.CountAsync());
        Assert.Equal(tenantTemplateId, preserved.Id);
        Assert.Equal("tenant-purchase-document", preserved.DocumentType);
        Assert.Equal(7, preserved.Version);
        Assert.False(preserved.IsActive);
        var preservedStep = Assert.Single(preserved.Steps);
        Assert.Equal("租户自定义复核", preservedStep.StepName);
        Assert.Equal("tenant-approver", preservedStep.ApproverRef);
        Assert.Equal(72, preservedStep.DueInHours);
    }

    [Fact]
    public async Task World_history_seed_keeps_the_five_legacy_codes_beside_product_templates()
    {
        await using var db = CreateDbContext();
        await new ApprovalSeedService(db).SeedAsync("org-001", "env-dev");
        db.ChangeTracker.Clear();

        var report = await new WorldHistoryApprovalSeedService(db).SeedAsync(
            "org-001",
            "env-dev",
            new DateOnly(2026, 7, 26),
            0.05d);

        var codes = await db.ApprovalTemplates.AsNoTracking().Select(x => x.TemplateCode).ToArrayAsync();
        Assert.Equal(5, report.TemplatesWritten);
        Assert.Equal(11, codes.Length);
        Assert.All(ProductTemplateCodes, code => Assert.Contains(code, codes));
        Assert.Contains("APT-WB-PO-001", codes);
        Assert.Contains("APT-WB-PO-002", codes);
        Assert.Contains("APT-WB-NCR-001", codes);
        Assert.Contains("APT-WB-CNT-001", codes);
        Assert.Contains("APT-WB-ECO-001", codes);
        Assert.Equal("APT-WB-NCR-001", WorldHistoryNcrDispositionApprovals.LegacyNcrDispositionTemplateCode);
        Assert.NotEqual(ApprovalTemplateCodes.NcrDisposition, WorldHistoryApprovalSpec.NcrTemplateCode);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"approval-product-seed-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
