using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.ScanRecordAggregate;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.BarcodeRules;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.LabelTemplates;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.PrintBatches;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Scans;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelListQueryTests
{
    [Fact]
    public async Task List_queries_apply_filters_skip_take_and_return_total()
    {
        await using var dbContext = CreateDbContext();
        var ruleA = BarcodeRule.Create("org-001", "env-dev", "FG-A", "code128", "FGA", 40, "none", ["work-order"], "active");
        var ruleB = BarcodeRule.Create("org-001", "env-dev", "FG-B", "code128", "FGB", 40, "none", ["work-order"], "inactive");
        var ruleOtherOrg = BarcodeRule.Create("org-002", "env-dev", "FG-C", "code128", "FGC", 40, "none", ["work-order"], "active");
        var templateA = LabelTemplate.Create("org-001", "env-dev", "tpl-a", "Template A", "file-a", "{}", "active");
        var templateB = LabelTemplate.Create("org-001", "env-dev", "tpl-b", "Template B", "file-b", "{}", "active");
        var templateInactive = LabelTemplate.Create("org-001", "env-dev", "tpl-c", "Template C", "file-c", "{}", "inactive");
        var batchA = LabelPrintBatch.Create("org-001", "env-dev", ruleA, templateA.Id, "work-order", "WO-001", "batch-a", "{}", 1);
        var batchB = LabelPrintBatch.Create("org-001", "env-dev", ruleA, templateA.Id, "work-order", "WO-002", "batch-b", "{}", 1);
        var scanA = ScanRecord.Record("org-001", "env-dev", "PDA-01", "BC-001", "wms.receiving", "ASN-001", "scan-a", "accepted", null);
        var scanB = ScanRecord.Record("org-001", "env-dev", "PDA-01", "BC-002", "wms.receiving", "ASN-002", "scan-b", "rejected", "bad");

        dbContext.AddRange(ruleA, ruleB, ruleOtherOrg, templateA, templateB, templateInactive, batchA, batchB, scanA, scanB);
        await dbContext.SaveChangesAsync();

        var rules = await new ListBarcodeRulesQueryHandler(dbContext)
            .Handle(new ListBarcodeRulesQuery("org-001", "env-dev", null, "FG", 1, 1), CancellationToken.None);
        var templates = await new ListLabelTemplatesQueryHandler(dbContext)
            .Handle(new ListLabelTemplatesQuery("org-001", "env-dev", "active", 1, 1), CancellationToken.None);
        var batches = await new ListLabelPrintBatchesQueryHandler(dbContext)
            .Handle(new ListLabelPrintBatchesQuery("org-001", "env-dev", "work-order", null, "pending", 1, 1), CancellationToken.None);
        var scans = await new ListScansQueryHandler(dbContext)
            .Handle(new ListScansQuery("org-001", "env-dev", "PDA-01", null, "wms.receiving", null, 1, 1), CancellationToken.None);

        Assert.Equal(2, rules.Total);
        Assert.Single(rules.Items);
        Assert.Equal("FG-B", rules.Items.Single().RuleCode);

        Assert.Equal(2, templates.Total);
        Assert.Single(templates.Items);
        Assert.Equal("tpl-b", templates.Items.Single().TemplateCode);

        Assert.Equal(2, batches.Total);
        Assert.Single(batches.Items);
        Assert.Equal("WO-001", batches.Items.Single().SourceDocumentId);

        Assert.Equal(2, scans.Total);
        Assert.Single(scans.Items);
        Assert.Equal("BC-001", scans.Items.Single().ScannedValue);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
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

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
