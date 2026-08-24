using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.BarcodeRuleAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.Printing;
using Nerv.IIP.Business.BarcodeLabel.Infrastructure;
using Nerv.IIP.Business.BarcodeLabel.Web.Application.Commands.PrintBatches;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Tests;

public sealed class PrintLabelLifecycleCommandTests
{
    [Fact]
    public void Every_lifecycle_rejection_reason_has_an_explicit_user_message_mapping()
    {
        const int sequenceNo = 37;
        var expectedMessages = new Dictionary<LabelPrintLifecycleRejectionReason, string>
        {
            [LabelPrintLifecycleRejectionReason.BatchCannotBeDispatched] = "当前打印批次状态不允许再次下发。",
            [LabelPrintLifecycleRejectionReason.BatchDeliveryUnknownCannotBeDispatched] = "交付结果未知，禁止再次下发打印批次。",
            [LabelPrintLifecycleRejectionReason.BatchCannotBeReprinted] = "当前打印批次状态不允许单项再次传输。",
            [LabelPrintLifecycleRejectionReason.BatchDeliveryUnknownCannotBeReprinted] = "交付结果未知，禁止再次传输标签。",
            [LabelPrintLifecycleRejectionReason.FailedBatchRequiresDispatch] = "整批打印失败后不能单项再次传输，请改用整批下发。",
            [LabelPrintLifecycleRejectionReason.PrintItemNotFound] = "未找到打印项，序号 = 37。",
            [LabelPrintLifecycleRejectionReason.PrintItemVoided] = "已作废标签不允许再次传输。",
            [LabelPrintLifecycleRejectionReason.PrintItemConsumed] = "已消费标签不允许再次传输。",
            [LabelPrintLifecycleRejectionReason.ConsumedPrintItemCannotBeVoided] = "已消费标签不允许作废。",
        };

        Assert.Equal(
            Enum.GetValues<LabelPrintLifecycleRejectionReason>().Order(),
            expectedMessages.Keys.Order());
        foreach (var (reason, expectedMessage) in expectedMessages)
        {
            var rejection = new LabelPrintLifecycleRejectedException(reason, "领域拒绝。");

            var exception = LabelPrintLifecycleKnownExceptionMapper.Create(rejection, sequenceNo);

            Assert.Equal(expectedMessage, exception.Message);
            Assert.Same(rejection, exception.InnerException);
        }
    }

    [Fact]
    public async Task Dispatch_sent_to_printer_records_delivery_without_claiming_items_were_printed()
    {
        await using var dbContext = CreateDbContext();
        var (batch, template) = AddReplayableBatch(dbContext, 2);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("dispatch-job"));
        var handler = new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer);

        var result = await handler.Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);

        Assert.Equal(batch.Id, result);
        Assert.Equal("sent-to-printer", batch.Status);
        Assert.All(batch.Items, item => Assert.Equal("created", item.Status));
        Assert.Equal(template.TemplateFileId, assetPort.Requests.Single().FileId);
        Assert.Equal(2, printer.Calls.Single().Count);
    }

    [Fact]
    public async Task Dispatch_delivery_unknown_records_uncertain_delivery_without_claiming_items_were_printed()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.DeliveryUnknown("dispatch-job", "连接在写入后中断。"));

        await new DispatchLabelPrintBatchCommandHandler(dbContext, ValidAssetPort(), printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);

        Assert.Equal("delivery-unknown", batch.Status);
        Assert.Equal("created", batch.Items.Single().Status);
        Assert.Equal("连接在写入后中断。", batch.FailureReason);
    }

    [Fact]
    public async Task Reprint_sent_to_printer_records_the_new_dispatch_without_claiming_the_item_reprinted()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 2);
        batch.RecordSentToPrinter("printer-01", "initial-job");
        batch.VoidItem(1, "damaged");
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("reprint-job"));
        var handler = new ReprintLabelCommandHandler(dbContext, ValidAssetPort(), printer);

        var result = await handler.Handle(
            new ReprintLabelCommand("org-001", "env-dev", batch.Id, 2, "printer-01"),
            CancellationToken.None);

        Assert.Equal("sent-to-printer", result.Status);
        Assert.Equal("sent-to-printer", batch.Status);
        Assert.Equal("printer-01", batch.PrinterId);
        Assert.Equal("reprint-job", batch.PrintJobId);
        Assert.Equal("voided", batch.Items.Single(x => x.SequenceNo == 1).Status);
        Assert.Equal("created", batch.Items.Single(x => x.SequenceNo == 2).Status);
        Assert.Single(printer.Calls.Single());
    }

    [Fact]
    public async Task Reprint_can_repeat_after_sent_to_printer_without_physical_confirmation()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "initial-job");
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("reprint-job"));
        var handler = new ReprintLabelCommandHandler(dbContext, ValidAssetPort(), printer);
        var command = new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-01");

        await handler.Handle(command, CancellationToken.None);
        await handler.Handle(command, CancellationToken.None);

        Assert.Equal("sent-to-printer", batch.Status);
        Assert.Equal("created", batch.Items.Single().Status);
        Assert.Equal(2, printer.Calls.Count);
    }

    [Fact]
    public async Task Failed_batch_rejects_single_item_reprint_before_asset_or_transport_and_recovers_by_batch_dispatch()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordPrintFailed("整批连接前失败。");
        await dbContext.SaveChangesAsync();
        var reprintAssetPort = ValidAssetPort();
        var reprintPrinter = new RecordingPrinter(LabelPrinterDispatchResult.Sent("reprint-job"));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ReprintLabelCommandHandler(dbContext, reprintAssetPort, reprintPrinter).Handle(
                new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-01"),
                CancellationToken.None));

        Assert.Equal("整批打印失败后不能单项再次传输，请改用整批下发。", exception.Message);
        Assert.Empty(reprintAssetPort.Requests);
        Assert.Empty(reprintPrinter.Calls);

        var dispatchPrinter = new RecordingPrinter(LabelPrinterDispatchResult.Sent("dispatch-retry-job"));
        await new DispatchLabelPrintBatchCommandHandler(dbContext, ValidAssetPort(), dispatchPrinter).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);

        Assert.Equal("sent-to-printer", batch.Status);
        Assert.Single(dispatchPrinter.Calls);
    }

    [Fact]
    public async Task Reprint_can_retry_a_known_pre_write_failure()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "initial-job");
        await dbContext.SaveChangesAsync();
        var command = new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-01");

        await new ReprintLabelCommandHandler(
            dbContext,
            ValidAssetPort(),
            new RecordingPrinter(LabelPrinterDispatchResult.Failed("连接前失败。")))
            .Handle(command, CancellationToken.None);
        Assert.Equal("sent-to-printer", batch.Status);
        Assert.Equal("printer-01", batch.PrinterId);
        Assert.Null(batch.PrintJobId);
        Assert.Equal("连接前失败。", batch.FailureReason);

        var retryPrinter = new RecordingPrinter(LabelPrinterDispatchResult.Sent("retry-job"));
        await new ReprintLabelCommandHandler(dbContext, ValidAssetPort(), retryPrinter)
            .Handle(command, CancellationToken.None);

        Assert.Equal("sent-to-printer", batch.Status);
        Assert.Equal("retry-job", batch.PrintJobId);
        Assert.Equal("created", batch.Items.Single().Status);
        Assert.Single(retryPrinter.Calls);
    }

    [Fact]
    public async Task Reprint_delivery_unknown_preserves_the_batch_state_and_returns_operator_guidance()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "initial-job");
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.DeliveryUnknown("reprint-job", "连接在写入后中断。"));

        var result = await new ReprintLabelCommandHandler(dbContext, ValidAssetPort(), printer).Handle(
            new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-01"),
            CancellationToken.None);

        Assert.Equal("delivery-unknown", result.Status);
        Assert.Contains("先现场确认上一张标签是否已出纸", result.FailureReason, StringComparison.Ordinal);
        Assert.Equal("sent-to-printer", batch.Status);
        Assert.Equal("printer-01", batch.PrinterId);
        Assert.Equal("reprint-job", batch.PrintJobId);
        Assert.Equal(result.FailureReason, batch.FailureReason);
        Assert.Equal("created", batch.Items.Single().Status);
    }

    [Fact]
    public async Task Reprint_failure_updates_the_latest_attempt_without_reopening_whole_batch_dispatch()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "initial-job");
        await dbContext.SaveChangesAsync();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Failed("连接前失败。"));

        var result = await new ReprintLabelCommandHandler(dbContext, ValidAssetPort(), printer).Handle(
            new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-01"),
            CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Equal("sent-to-printer", batch.Status);
        Assert.Equal("printer-01", batch.PrinterId);
        Assert.Null(batch.PrintJobId);
        Assert.Equal("连接前失败。", batch.FailureReason);
        Assert.Equal("created", batch.Items.Single().Status);

        var assetPort = ValidAssetPort();
        var redispatchPrinter = new RecordingPrinter(LabelPrinterDispatchResult.Sent("duplicate-batch-job"));
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, redispatchPrinter).Handle(
                new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
                CancellationToken.None));

        Assert.Equal("当前打印批次状态不允许再次下发。", exception.Message);
        Assert.Empty(assetPort.Requests);
        Assert.Empty(redispatchPrinter.Calls);
    }

    [Fact]
    public async Task Reprint_from_printed_preserves_completion_and_records_the_latest_attempt()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "dispatch-job");
        batch.RecordPrinted();
        var completedAtUtc = batch.CompletedAtUtc;
        await dbContext.SaveChangesAsync();

        var result = await new ReprintLabelCommandHandler(
            dbContext,
            ValidAssetPort(),
            new RecordingPrinter(LabelPrinterDispatchResult.Sent("reprint-job"))).Handle(
                new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-02"),
                CancellationToken.None);

        Assert.Equal("sent-to-printer", result.Status);
        Assert.Equal("printed", batch.Status);
        Assert.Equal(completedAtUtc, batch.CompletedAtUtc);
        Assert.Equal("printer-02", batch.PrinterId);
        Assert.Equal("reprint-job", batch.PrintJobId);
        Assert.Null(batch.FailureReason);
        Assert.Equal("printed", batch.Items.Single().Status);
    }

    [Fact]
    public async Task Reprint_pending_batch_reports_the_batch_rejection_even_when_the_item_is_voided()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.VoidItem(1, "damaged");
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("reprint-job"));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ReprintLabelCommandHandler(dbContext, assetPort, printer).Handle(
                new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-01"),
                CancellationToken.None));

        Assert.Equal("当前打印批次状态不允许单项再次传输。", exception.Message);
        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Theory]
    [InlineData("voided", "已作废标签不允许再次传输。")]
    [InlineData("consumed", "已消费标签不允许再次传输。")]
    public async Task Reprint_rejects_non_reprintable_items_before_asset_or_transport(
        string itemStatus,
        string expectedMessage)
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "initial-job");
        if (itemStatus == "voided")
        {
            batch.VoidItem(1, "damaged");
        }
        else
        {
            batch.RecordPrinted();
            batch.ConsumeItem(batch.Items.Single().LabelValue);
        }
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("reprint-job"));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ReprintLabelCommandHandler(dbContext, assetPort, printer).Handle(
                new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-01"),
                CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_and_reprint_compile_the_same_frozen_item_to_identical_bytes()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 2);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);
        await new ReprintLabelCommandHandler(dbContext, assetPort, printer).Handle(
            new ReprintLabelCommand("org-001", "env-dev", batch.Id, 2, "printer-01"),
            CancellationToken.None);

        Assert.Equal(2, printer.Calls.Count);
        Assert.Equal(printer.Calls[0][1], printer.Calls[1].Single());
    }

    [Fact]
    public async Task Dispatch_binds_the_frozen_epc_uri_from_the_print_item()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create(
            "org-001", "env-dev", "GS1-FG", "gs1-128", "0950600013435", 80,
            "gs1-mod10", ["wms.inbound"], "active", 7);
        var template = ActiveTemplate();
        const string gs1VariableSchema =
            """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80},{"name":"lotNo","type":"string","required":true,"maxLength":100},{"name":"serialPrefix","type":"string","required":true,"maxLength":100}]}""";
        var batch = LabelPrintBatch.Create(
            "org-001", "env-dev", rule, template.Id,
            new LabelPrintBatchSnapshot(
                template.TemplateFileId,
                AssetSha256,
                gs1VariableSchema,
                rule.BarcodeType,
                ZplV1LabelCompiler.ContractVersion),
            "wms.inbound",
            "ASN-001",
            "idem-epc-uri",
            """{"skuCode":"SKU-FG-1000","lotNo":"LOT-A","serialPrefix":"SN-"}""",
            1);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        const string epcTemplate =
            """{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"text","x":40,"y":30,"fontHeight":30,"fontWidth":30,"variable":"label.epcUri"},{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}""";
        var assetPort = new RecordingAssetPort(reference =>
            new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, epcTemplate));
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job-epc"));

        await new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);

        var zpl = Encoding.UTF8.GetString(printer.Calls.Single().Single());
        Assert.Contains($"^FD{batch.Items.Single().EpcUri}^FS", zpl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_rejects_persisted_gs1_components_that_conflict_with_the_frozen_label_value()
    {
        await using var dbContext = CreateDbContext();
        var rule = BarcodeRule.Create(
            "org-001", "env-dev", "GS1-FROZEN", "gs1-128", "0950600013435", 80,
            "gs1-mod10", ["wms.inbound"], "active", 7);
        var template = ActiveTemplate();
        const string gs1VariableSchema =
            """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80},{"name":"lotNo","type":"string","required":true,"maxLength":100},{"name":"serialPrefix","type":"string","required":true,"maxLength":100}]}""";
        var batch = LabelPrintBatch.Create(
            "org-001", "env-dev", rule, template.Id,
            new LabelPrintBatchSnapshot(
                template.TemplateFileId,
                AssetSha256,
                gs1VariableSchema,
                rule.BarcodeType,
                ZplV1LabelCompiler.ContractVersion),
            "wms.inbound",
            "ASN-001",
            "idem-frozen-gs1-components",
            """{"skuCode":"SKU-FG-1000","lotNo":"ORIGINAL-LOT","serialPrefix":"ORIGINAL-SERIAL-"}""",
            1);
        var item = batch.Items.Single();
        SetPersistedValue(item, nameof(LabelPrintItem.Gtin), "09501101530003");
        SetPersistedValue(item, nameof(LabelPrintItem.LotNo), "FROZEN-LOT");
        SetPersistedValue(item, nameof(LabelPrintItem.SerialNumber), "FROZEN-SERIAL");
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        const string frozenFieldsTemplate =
            """{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"text","x":40,"y":30,"fontHeight":30,"fontWidth":30,"variable":"label.gtin"},{"kind":"text","x":40,"y":60,"fontHeight":30,"fontWidth":30,"variable":"label.lotNo"},{"kind":"text","x":40,"y":90,"fontHeight":30,"fontWidth":30,"variable":"label.serialNumber"},{"kind":"barcode","x":40,"y":130,"moduleWidth":2,"height":100,"variable":"label.value"}]}""";
        var assetPort = new RecordingAssetPort(reference =>
            new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, frozenFieldsTemplate));
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job-frozen-gs1"));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Equal("打印批次冻结快照验证或编译失败。", exception.Message);
        Assert.Single(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_ignores_live_template_file_schema_and_status_changes_after_batch_creation()
    {
        await using var dbContext = CreateDbContext();
        var (batch, template) = AddReplayableBatch(dbContext, 1);
        template.Update("Changed", "file-live-changed", "{}", "inactive");
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None);

        Assert.Equal("file-template-001", assetPort.Requests.Single().FileId);
        Assert.Single(printer.Calls.Single());
    }

    [Theory]
    [InlineData("org-other", "env-dev")]
    [InlineData("org-001", "env-other")]
    public async Task Dispatch_rejects_a_batch_outside_the_requested_scope_before_asset_or_transport(
        string organizationId,
        string environmentId)
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));
        var handler = new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer);

        await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
            new DispatchLabelPrintBatchCommand(organizationId, environmentId, batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_rejects_a_legacy_batch_before_asset_or_transport()
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = ActiveTemplate();
        var batch = LabelPrintBatch.CreateLegacyWithoutReplaySnapshot(
            "org-001", "env-dev", rule, template.Id, "wms.inbound", "ASN-001", "idem-legacy", LabelValuesJson, 1);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() => new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_rejects_an_in_flight_batch_before_asset_or_transport()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "existing-job");
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("new-job"));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
                new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
                CancellationToken.None));

        Assert.Equal("当前打印批次状态不允许再次下发。", exception.Message);
        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_rejects_a_printed_batch_without_erasing_its_completion_fact()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "dispatch-job");
        batch.RecordPrinted();
        var completedAtUtc = batch.CompletedAtUtc;
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("duplicate-batch-job"));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
                new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
                CancellationToken.None));

        Assert.Equal("当前打印批次状态不允许再次下发。", exception.Message);
        Assert.Equal("printed", batch.Status);
        Assert.Equal(completedAtUtc, batch.CompletedAtUtc);
        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Theory]
    [InlineData(2, "未找到打印项，序号 = 2。")]
    [InlineData(1, "已消费标签不允许作废。")]
    public async Task Void_maps_item_rejections_to_precise_chinese_known_exceptions(
        int sequenceNo,
        string expectedMessage)
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordSentToPrinter("printer-01", "dispatch-job");
        batch.RecordPrinted();
        if (sequenceNo == 1)
        {
            batch.ConsumeItem(batch.Items.Single().LabelValue);
        }
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new VoidLabelCommandHandler(dbContext).Handle(
                new VoidLabelCommand("org-001", "env-dev", batch.Id, sequenceNo, "damaged"),
                CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task Reprint_rejects_a_batch_with_unknown_delivery_before_asset_or_transport()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordDeliveryUnknown("printer-01", "unknown-job", "partial write");
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("new-job"));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new ReprintLabelCommandHandler(dbContext, assetPort, printer).Handle(
                new ReprintLabelCommand("org-001", "env-dev", batch.Id, 1, "printer-01"),
                CancellationToken.None));

        Assert.Equal("交付结果未知，禁止再次传输标签。", exception.Message);
        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_rejects_a_batch_with_unknown_delivery_before_asset_or_transport()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        batch.RecordDeliveryUnknown("printer-01", "unknown-job", "partial write");
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("new-job"));

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
                new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
                CancellationToken.None));

        Assert.Equal("交付结果未知，禁止再次下发打印批次。", exception.Message);
        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_rejects_an_asset_with_a_different_sha256_before_transport()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 1);
        await dbContext.SaveChangesAsync();
        var assetPort = new RecordingAssetPort(reference =>
            new VerifiedLabelTemplateAsset(reference.FileId, $"sha256:{new string('b', 64)}", TemplateJson));
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() => new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Single(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_precompiles_the_entire_batch_before_the_single_transport_call()
    {
        await using var dbContext = CreateDbContext();
        var (batch, _) = AddReplayableBatch(dbContext, 2);
        await dbContext.SaveChangesAsync();
        var assetPort = new RecordingAssetPort(reference =>
            new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, "{}"));
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() => new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Single(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    [Fact]
    public async Task Dispatch_rejects_an_unknown_renderer_before_asset_or_transport()
    {
        await using var dbContext = CreateDbContext();
        var rule = ActiveRule();
        var template = ActiveTemplate();
        var batch = LabelPrintBatch.Create(
            "org-001", "env-dev", rule, template.Id,
            new LabelPrintBatchSnapshot(template.TemplateFileId, AssetSha256, VariableSchemaJson, rule.BarcodeType, "zpl-v2"),
            "wms.inbound", "ASN-001", "idem-renderer", LabelValuesJson, 1);
        dbContext.AddRange(template, batch);
        await dbContext.SaveChangesAsync();
        var assetPort = ValidAssetPort();
        var printer = new RecordingPrinter(LabelPrinterDispatchResult.Sent("job"));

        await Assert.ThrowsAsync<KnownException>(() => new DispatchLabelPrintBatchCommandHandler(dbContext, assetPort, printer).Handle(
            new DispatchLabelPrintBatchCommand("org-001", "env-dev", batch.Id, "printer-01"),
            CancellationToken.None));

        Assert.Empty(assetPort.Requests);
        Assert.Empty(printer.Calls);
    }

    private const string VariableSchemaJson =
        """{"version":1,"variables":[{"name":"skuCode","type":"string","required":true,"maxLength":80}]}""";
    private const string LabelValuesJson = """{"skuCode":"SKU-FG-1000"}""";
    private const string TemplateJson =
        """{"format":"nerv-iip.label-template","version":1,"media":{"dpi":203,"widthDots":812,"heightDots":406},"fields":[{"kind":"text","x":40,"y":30,"fontHeight":30,"fontWidth":30,"variable":"skuCode"},{"kind":"barcode","x":40,"y":90,"moduleWidth":2,"height":100,"variable":"label.value"}]}""";
    private static readonly string AssetSha256 = $"sha256:{new string('a', 64)}";

    private static (LabelPrintBatch Batch, LabelTemplate Template) AddReplayableBatch(ApplicationDbContext dbContext, int quantity)
    {
        var rule = ActiveRule();
        var template = ActiveTemplate();
        var batch = LabelPrintBatch.Create(
            "org-001", "env-dev", rule, template.Id,
            new LabelPrintBatchSnapshot(template.TemplateFileId, AssetSha256, VariableSchemaJson, rule.BarcodeType, ZplV1LabelCompiler.ContractVersion),
            "wms.inbound", "ASN-001", $"idem-print-{Guid.NewGuid():N}", LabelValuesJson, quantity);
        dbContext.AddRange(template, batch);
        return (batch, template);
    }

    private static BarcodeRule ActiveRule() =>
        BarcodeRule.Create("org-001", "env-dev", "FG", "code128", "FG", 40, "none", ["wms.inbound"], "active");

    private static LabelTemplate ActiveTemplate() =>
        LabelTemplate.Create("org-001", "env-dev", "FG_BOX", "Finished goods box", "file-template-001", VariableSchemaJson, "active");

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static RecordingAssetPort ValidAssetPort() =>
        new(reference => new VerifiedLabelTemplateAsset(reference.FileId, AssetSha256, TemplateJson));

    private static void SetPersistedValue(LabelPrintItem item, string propertyName, string value) =>
        typeof(LabelPrintItem).GetProperty(propertyName)!.SetValue(item, value);

    private sealed class RecordingAssetPort(Func<LabelTemplateAssetReference, VerifiedLabelTemplateAsset> responseFactory) : ILabelTemplateAssetPort
    {
        public List<LabelTemplateAssetReference> Requests { get; } = [];

        public Task<VerifiedLabelTemplateAsset> GetVerifiedAsync(LabelTemplateAssetReference reference, CancellationToken cancellationToken)
        {
            Requests.Add(reference);
            return Task.FromResult(responseFactory(reference));
        }
    }

    private sealed class RecordingPrinter(LabelPrinterDispatchResult result) : ILabelPrinter
    {
        public List<IReadOnlyList<byte[]>> Calls { get; } = [];

        public Task<LabelPrinterDispatchResult> PrintAsync(
            string printerId,
            IReadOnlyCollection<CompiledLabelDocument> documents,
            CancellationToken cancellationToken)
        {
            Calls.Add(documents.Select(document => document.Payload.ToArray()).ToArray());
            return Task.FromResult(result);
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
