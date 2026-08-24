using MediatR;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class CreateLabelPrintBatchCommandTests
{
    [Theory]
    [InlineData("org-other", "env-dev")]
    [InlineData("org-001", "env-other")]
    public async Task Create_rejects_rule_from_another_scope_before_persisting(
        string ruleOrganizationId,
        string ruleEnvironmentId)
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create(
            ruleOrganizationId,
            ruleEnvironmentId,
            "FG",
            "code128",
            "FG",
            40,
            "none",
            ["wms.inbound"],
            "active");
        var template = ActiveTemplate();
        dbContext.AddRange(rule, template);
        await dbContext.SaveChangesAsync();

        var handler = new CreateLabelPrintBatchCommandHandler(dbContext, ValidAssetPort());

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(NewCommand(rule.Id, template.Id), CancellationToken.None));
        Assert.Empty(dbContext.LabelPrintBatches);
    }

    [Theory]
    [InlineData("org-other", "env-dev")]
    [InlineData("org-001", "env-other")]
    public async Task Create_rejects_template_from_another_scope_before_persisting(
        string templateOrganizationId,
        string templateEnvironmentId)
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = LabelTemplate.Create(
            templateOrganizationId,
            templateEnvironmentId,
            "FG_BOX",
            "Finished goods box",
            "file-template-001",
            VariableSchemaJson,
            "active");
        dbContext.AddRange(rule, template);
        await dbContext.SaveChangesAsync();

        var handler = new CreateLabelPrintBatchCommandHandler(dbContext, ValidAssetPort());

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(NewCommand(rule.Id, template.Id), CancellationToken.None));
        Assert.Empty(dbContext.LabelPrintBatches);
    }

    [Fact]
    public async Task Create_rejects_inactive_template_before_persisting()
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = LabelTemplate.Create(
            "org-001",
            "env-dev",
            "FG_BOX",
            "Finished goods box",
            "file-template-001",
            VariableSchemaJson,
            "inactive");
        dbContext.AddRange(rule, template);
        await dbContext.SaveChangesAsync();

        var handler = new CreateLabelPrintBatchCommandHandler(dbContext, ValidAssetPort());

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(NewCommand(rule.Id, template.Id), CancellationToken.None));
        Assert.Empty(dbContext.LabelPrintBatches);
    }

    [Fact]
    public async Task Create_rejects_inactive_gs1_rule_before_persisting()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create(
            "org-001",
            "env-dev",
            "GS1-FG",
            "gs1-128",
            "0950600013435",
            80,
            "gs1-mod10",
            ["wms.inbound"],
            "inactive",
            7);
        var template = ActiveTemplate();
        dbContext.AddRange(rule, template);
        await dbContext.SaveChangesAsync();

        var handler = new CreateLabelPrintBatchCommandHandler(dbContext, ValidAssetPort());

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCommand(
                rule.Id,
                template.Id,
                """{"skuCode":"SKU-FG-1000","lotNo":"LOT-A","serialPrefix":"SN-"}"""),
            CancellationToken.None));
        Assert.Empty(dbContext.LabelPrintBatches);
    }

    [Fact]
    public async Task Create_freezes_verified_template_rule_and_renderer_facts()
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = ActiveTemplate();
        dbContext.AddRange(rule, template);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var handler = new CreateLabelPrintBatchCommandHandler(dbContext, assetPort);

        var batchId = await handler.Handle(NewCommand(rule.Id, template.Id), CancellationToken.None);
        var batch = dbContext.LabelPrintBatches.Local.Single(x => x.Id == batchId);

        Assert.Equal(template.TemplateFileId, batch.TemplateFileIdSnapshot);
        Assert.Equal(AssetSha256, batch.TemplateAssetSha256);
        Assert.Equal(VariableSchemaJson, batch.VariableSchemaJsonSnapshot);
        Assert.Equal("code128", batch.BarcodeTypeSnapshot);
        Assert.Equal(ZplV1LabelCompiler.ContractVersion, batch.RendererContractVersion);
        Assert.Equal(
            new LabelTemplateAssetReference(template.TemplateFileId, "org-001", "env-dev", template.TemplateCode),
            assetPort.Requests.Single());

        template.Update("Updated template", "file-template-002", """{"version":1,"variables":[]}""", "active");
        rule.Update("qr", "QR", 80, "none", ["wms.inbound"], "active");
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.LabelPrintBatches.SingleAsync(x => x.Id == batchId);

        Assert.Equal("file-template-001", persisted.TemplateFileIdSnapshot);
        Assert.Equal(AssetSha256, persisted.TemplateAssetSha256);
        Assert.Equal(VariableSchemaJson, persisted.VariableSchemaJsonSnapshot);
        Assert.Equal("code128", persisted.BarcodeTypeSnapshot);
        Assert.Equal("zpl-v1", persisted.RendererContractVersion);
    }

    [Fact]
    public async Task Create_rejects_bad_asset_before_persisting()
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = ActiveTemplate();
        dbContext.AddRange(rule, template);
        await dbContext.SaveChangesAsync();
        var assetPort = new RecordingAssetPort(_ => throw new InvalidDataException("checksum mismatch"));
        var handler = new CreateLabelPrintBatchCommandHandler(dbContext, assetPort);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(NewCommand(rule.Id, template.Id), CancellationToken.None));

        Assert.Empty(dbContext.LabelPrintBatches);
        Assert.Single(assetPort.Requests);
    }

    [Theory]
    [MemberData(nameof(InvalidCompilationInputs))]
    public async Task Create_rejects_invalid_template_schema_or_item_values_before_persisting(
        string templateJson,
        string variableSchemaJson,
        string labelValuesJson)
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = ActiveTemplate(variableSchemaJson);
        dbContext.AddRange(rule, template);
        await dbContext.SaveChangesAsync();
        var assetPort = new RecordingAssetPort(_ =>
            new VerifiedLabelTemplateAsset("file-template-001", AssetSha256, templateJson));
        var handler = new CreateLabelPrintBatchCommandHandler(dbContext, assetPort);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            NewCommand(rule.Id, template.Id, labelValuesJson),
            CancellationToken.None));

        Assert.Empty(dbContext.LabelPrintBatches);
    }

    [Fact]
    public async Task Create_rejects_same_idempotency_key_when_frozen_asset_changes()
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = ActiveTemplate();
        dbContext.AddRange(rule, template);
        await dbContext.SaveChangesAsync();
        var currentSha256 = AssetSha256;
        var assetPort = new RecordingAssetPort(reference =>
            new VerifiedLabelTemplateAsset(reference.FileId, currentSha256, TemplateJson));
        var handler = new CreateLabelPrintBatchCommandHandler(dbContext, assetPort);

        _ = await handler.Handle(NewCommand(rule.Id, template.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        currentSha256 = $"sha256:{new string('b', 64)}";

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(NewCommand(rule.Id, template.Id), CancellationToken.None));
        Assert.Single(dbContext.LabelPrintBatches);
    }

    private const string VariableSchemaJson =
        """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""";

    private const string TemplateJson =
        """{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"text","x":40,"y":30,"fontHeight":30,"fontWidth":30,"variable":"skuCode"},{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}""";

    private static readonly string AssetSha256 = $"sha256:{new string('a', 64)}";

    public static TheoryData<string, string, string> InvalidCompilationInputs => new()
    {
        { "{}", VariableSchemaJson, """{"skuCode":"SKU-FG-1000"}""" },
        { TemplateJson, "{}", """{"skuCode":"SKU-FG-1000"}""" },
        { TemplateJson, VariableSchemaJson, """{"undeclared":"value"}""" },
    };

    private static BarcodeRule ActiveRule() =>
        BarcodeRule.Create(
            "org-001",
            "env-dev",
            "FG",
            "code128",
            "FG",
            40,
            "none",
            ["wms.inbound"],
            "active");

    private static LabelTemplate ActiveTemplate(string variableSchemaJson = VariableSchemaJson) =>
        LabelTemplate.Create(
            "org-001",
            "env-dev",
            "FG_BOX",
            "Finished goods box",
            "file-template-001",
            variableSchemaJson,
            "active");

    private static CreateLabelPrintBatchCommand NewCommand(
        BarcodeRuleId ruleId,
        LabelTemplateId templateId,
        string labelValuesJson = """{"skuCode":"SKU-FG-1000"}""") =>
        new(
            "org-001",
            "env-dev",
            ruleId,
            templateId,
            "wms.inbound",
            "ASN-001",
            "idem-print-001",
            labelValuesJson,
            1);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static RecordingAssetPort ValidAssetPort() =>
        new(reference => new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, TemplateJson));

    private sealed class RecordingAssetPort(
        Func<LabelTemplateAssetReference, VerifiedLabelTemplateAsset> responseFactory) : ILabelTemplateAssetPort
    {
        public List<LabelTemplateAssetReference> Requests { get; } = [];

        public Task<VerifiedLabelTemplateAsset> GetVerifiedAsync(
            LabelTemplateAssetReference reference,
            CancellationToken cancellationToken)
        {
            Requests.Add(reference);
            return Task.FromResult(responseFactory(reference));
        }
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

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
