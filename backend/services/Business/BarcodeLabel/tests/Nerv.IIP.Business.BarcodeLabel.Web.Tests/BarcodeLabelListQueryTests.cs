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
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Resolutions;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.Scans;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class BarcodeLabelListQueryTests
{
    [Fact]
    public async Task Resolve_barcode_returns_the_scoped_source_document_for_an_exact_generated_label()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create("org-001", "env-dev", "FG-A", "code128", "FGA", 40, "none", ["work-order"], "active");
        var template = LabelTemplate.Create("org-001", "env-dev", "tpl-a", "Template A", "file-a", "{}", "active");
        var batch = LabelPrintBatch.Create("org-001", "env-dev", rule, template.Id, "work-order", "WO-001", "batch-a", "{}", 1);
        dbContext.AddRange(rule, template, batch);
        await dbContext.SaveChangesAsync();

        var labelValue = batch.Items.Single().LabelValue;
        var result = await new ResolveBarcodeQueryHandler(dbContext)
            .Handle(new ResolveBarcodeQuery("org-001", "env-dev", labelValue), CancellationToken.None);

        Assert.Equal("resolved", result.Status);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("work-order", candidate.SourceDocumentType);
        Assert.Equal("WO-001", candidate.SourceDocumentId);
        Assert.Equal("barcode-label", candidate.Authority);
        Assert.Equal(batch.CreatedAtUtc, candidate.ObservedAtUtc);
    }

    [Fact]
    public async Task Resolve_barcode_keeps_the_unique_candidate_when_ambiguous_paging_starts_after_it()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create("org-001", "env-dev", "FG-A", "code128", "FGA", 40, "none", ["work-order"], "active");
        var template = LabelTemplate.Create("org-001", "env-dev", "tpl-a", "Template A", "file-a", "{}", "active");
        var batch = LabelPrintBatch.Create("org-001", "env-dev", rule, template.Id, "work-order", "WO-001", "batch-a", "{}", 1);
        dbContext.AddRange(rule, template, batch);
        await dbContext.SaveChangesAsync();

        var result = await new ResolveBarcodeQueryHandler(dbContext).Handle(
            new ResolveBarcodeQuery("org-001", "env-dev", batch.Items.Single().LabelValue, Skip: 20, Take: 10),
            CancellationToken.None);

        Assert.Equal("resolved", result.Status);
        Assert.Equal(1, result.Total);
        Assert.Equal("WO-001", Assert.Single(result.Candidates).SourceDocumentId);
    }

    [Fact]
    public async Task Resolve_barcode_does_not_leak_a_matching_label_from_another_tenant_scope()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create("org-002", "env-dev", "FG-A", "code128", "FGA", 40, "none", ["work-order"], "active");
        var template = LabelTemplate.Create("org-002", "env-dev", "tpl-a", "Template A", "file-a", "{}", "active");
        var batch = LabelPrintBatch.Create("org-002", "env-dev", rule, template.Id, "work-order", "WO-001", "batch-a", "{}", 1);
        dbContext.AddRange(rule, template, batch);
        await dbContext.SaveChangesAsync();

        var result = await new ResolveBarcodeQueryHandler(dbContext)
            .Handle(new ResolveBarcodeQuery("org-001", "env-dev", batch.Items.Single().LabelValue), CancellationToken.None);

        Assert.Equal("unsupported", result.Status);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Resolve_barcode_returns_ambiguous_when_one_value_maps_to_multiple_source_documents()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create("org-001", "env-dev", "FG-A", "code128", "FGA", 40, "none", ["work-order"], "active");
        var template = LabelTemplate.Create("org-001", "env-dev", "tpl-a", "Template A", "file-a", "{}", "active");
        var first = LabelPrintBatch.Create("org-001", "env-dev", rule, template.Id, "work-order", "WO-001", "batch-a", "{}", 1);
        var second = LabelPrintBatch.Create("org-001", "env-dev", rule, template.Id, "work-order", "WO001", "batch-b", "{}", 1);
        Assert.Equal(first.Items.Single().LabelValue, second.Items.Single().LabelValue);
        dbContext.AddRange(rule, template, first, second);
        await dbContext.SaveChangesAsync();

        var result = await new ResolveBarcodeQueryHandler(dbContext)
            .Handle(new ResolveBarcodeQuery("org-001", "env-dev", first.Items.Single().LabelValue), CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.Equal("multiple-source-documents", result.ReasonCode);
        Assert.Equal(["WO-001", "WO001"], result.Candidates.Select(candidate => candidate.SourceDocumentId));
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task Resolve_barcode_pages_ambiguous_candidates_after_counting_the_full_match_set()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create("org-001", "env-dev", "FG-A", "code128", "FGA", 40, "none", ["work-order"], "active");
        var template = LabelTemplate.Create("org-001", "env-dev", "tpl-a", "Template A", "file-a", "{}", "active");
        var first = LabelPrintBatch.Create("org-001", "env-dev", rule, template.Id, "work-order", "WO-001", "batch-a", "{}", 1);
        var second = LabelPrintBatch.Create("org-001", "env-dev", rule, template.Id, "work-order", "WO001", "batch-b", "{}", 1);
        var third = LabelPrintBatch.Create("org-001", "env-dev", rule, template.Id, "work-order", "W-O001", "batch-c", "{}", 1);
        Assert.Equal(first.Items.Single().LabelValue, second.Items.Single().LabelValue);
        Assert.Equal(first.Items.Single().LabelValue, third.Items.Single().LabelValue);
        dbContext.AddRange(rule, template, first, second, third);
        await dbContext.SaveChangesAsync();

        var result = await new ResolveBarcodeQueryHandler(dbContext)
            .Handle(
                new ResolveBarcodeQuery("org-001", "env-dev", first.Items.Single().LabelValue, Skip: 1, Take: 1),
                CancellationToken.None);

        Assert.Equal("ambiguous", result.Status);
        Assert.Equal(3, result.Total);
        Assert.Equal("WO-001", Assert.Single(result.Candidates).SourceDocumentId);
    }

    [Fact]
    public async Task Resolve_barcode_fails_closed_when_the_exact_label_value_has_been_voided()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create("org-001", "env-dev", "FG-A", "code128", "FGA", 40, "none", ["work-order"], "active");
        var template = LabelTemplate.Create("org-001", "env-dev", "tpl-a", "Template A", "file-a", "{}", "active");
        var batch = LabelPrintBatch.Create("org-001", "env-dev", rule, template.Id, "work-order", "WO-001", "batch-a", "{}", 1);
        batch.VoidItem(1, "标签破损");
        dbContext.AddRange(rule, template, batch);
        await dbContext.SaveChangesAsync();

        var result = await new ResolveBarcodeQueryHandler(dbContext)
            .Handle(new ResolveBarcodeQuery("org-001", "env-dev", batch.Items.Single().LabelValue), CancellationToken.None);

        Assert.Equal("forbidden", result.Status);
        Assert.Equal("label-voided", result.ReasonCode);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Resolve_barcode_distinguishes_unknown_managed_values_from_unsupported_values()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create("org-001", "env-dev", "FG-A", "code128", "FGA", 40, "none", ["work-order"], "active");
        dbContext.Add(rule);
        await dbContext.SaveChangesAsync();

        var handler = new ResolveBarcodeQueryHandler(dbContext);
        var unknown = await handler.Handle(
            new ResolveBarcodeQuery("org-001", "env-dev", "FGAWO9990001"),
            CancellationToken.None);
        var unsupported = await handler.Handle(
            new ResolveBarcodeQuery("org-001", "env-dev", "FOREIGN-CODE"),
            CancellationToken.None);

        Assert.Equal("unknown", unknown.Status);
        Assert.Equal("managed-label-not-found", unknown.ReasonCode);
        Assert.Equal("unsupported", unsupported.Status);
        Assert.Equal("barcode-format-unsupported", unsupported.ReasonCode);
    }

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
