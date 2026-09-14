using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using NJsonSchema.Annotations;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelPrintBatchAggregate;
using Nerv.IIP.Business.BarcodeLabel.Domain.AggregatesModel.LabelTemplateAggregate;

namespace Nerv.IIP.Business.BarcodeLabel.Web.Application.Queries.PrintBatches;

public sealed record GetLabelPrintBatchQuery(LabelPrintBatchId PrintBatchId) : IQuery<LabelPrintBatchDetail>;

public sealed record LabelPrintBatchDetail(
    LabelPrintBatchId PrintBatchId,
    LabelTemplateId LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    int RequestedQuantity,
    string Status,
    string? PrinterId,
    string? PrintJobId,
    string? FailureReason,
    IReadOnlyCollection<LabelPrintItemDetail> Items);

public sealed record LabelPrintItemDetail(int SequenceNo, string LabelValue, string? FileId, string Status, string? VoidReason);

public sealed class GetLabelPrintBatchQueryValidator : AbstractValidator<GetLabelPrintBatchQuery>
{
    public GetLabelPrintBatchQueryValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
    }
}

public sealed class GetLabelPrintBatchQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetLabelPrintBatchQuery, LabelPrintBatchDetail>
{
    public async Task<LabelPrintBatchDetail> Handle(GetLabelPrintBatchQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.LabelPrintBatches
            .Where(x => x.Id == request.PrintBatchId)
            .Select(x => new LabelPrintBatchDetail(
                x.Id,
                x.LabelTemplateId,
                x.SourceDocumentType,
                x.SourceDocumentId,
                x.IdempotencyKey,
                x.RequestedQuantity,
                x.Status,
                x.PrinterId,
                x.PrintJobId,
                x.FailureReason,
                x.Items.OrderBy(item => item.SequenceNo).Select(item => new LabelPrintItemDetail(item.SequenceNo, item.LabelValue, item.FileId, item.Status, item.VoidReason)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException($"未找到打印批次，批次 ID = {request.PrintBatchId}。");
    }
}

public sealed record GetScopedLabelPrintBatchQuery(
    LabelPrintBatchId PrintBatchId,
    string OrganizationId,
    string EnvironmentId) : IQuery<ScopedLabelPrintBatchDetail>;

public sealed record GetScopedLabelPrintBatchByIdempotencyKeyQuery(
    string OrganizationId,
    string EnvironmentId,
    string IdempotencyKey) : IQuery<ScopedLabelPrintBatchDetail>;

public sealed record ScopedLabelPrintBatchDetail(
    LabelPrintBatchId PrintBatchId,
    LabelTemplateId LabelTemplateId,
    string SourceDocumentType,
    string SourceDocumentId,
    string IdempotencyKey,
    string ReportIntentKey,
    [property: Required, JsonRequired, JsonSchemaExtensionData("nullable", true)] string? ReportIntentFingerprint,
    int RequestedQuantity,
    string Status,
    string? PrinterId,
    string? PrintJobId,
    string? FailureReason,
    string? ProductionReportId,
    string? ProductionReportNo,
    IReadOnlyCollection<ScopedLabelPrintItemDetail> Items);

public sealed record ScopedLabelPrintItemDetail(
    int SequenceNo,
    string LabelValue,
    string? FileId,
    string Status,
    string? VoidReason,
    string? SerialNumber,
    string? LotNo,
    string? Gtin,
    string? EpcUri);

public sealed class GetScopedLabelPrintBatchQueryValidator : AbstractValidator<GetScopedLabelPrintBatchQuery>
{
    public GetScopedLabelPrintBatchQueryValidator()
    {
        RuleFor(x => x.PrintBatchId).NotEmpty();
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
    }
}

public sealed class GetScopedLabelPrintBatchQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetScopedLabelPrintBatchQuery, ScopedLabelPrintBatchDetail>
{
    public async Task<ScopedLabelPrintBatchDetail> Handle(
        GetScopedLabelPrintBatchQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        return await dbContext.LabelPrintBatches
            .Where(x => x.Id == request.PrintBatchId
                && x.OrganizationId == tenant.OrganizationId
                && x.EnvironmentId == tenant.EnvironmentId)
            .Select(ScopedLabelPrintBatchProjection.Detail)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException("未找到打印批次。");
    }
}

public sealed class GetScopedLabelPrintBatchByIdempotencyKeyQueryValidator
    : AbstractValidator<GetScopedLabelPrintBatchByIdempotencyKeyQuery>
{
    public GetScopedLabelPrintBatchByIdempotencyKeyQueryValidator()
    {
        this.AddTenantRules(x => x.OrganizationId, x => x.EnvironmentId);
        RuleFor(x => x.OrganizationId).MaximumLength(100);
        RuleFor(x => x.EnvironmentId).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}

public sealed class GetScopedLabelPrintBatchByIdempotencyKeyQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetScopedLabelPrintBatchByIdempotencyKeyQuery, ScopedLabelPrintBatchDetail>
{
    public async Task<ScopedLabelPrintBatchDetail> Handle(
        GetScopedLabelPrintBatchByIdempotencyKeyQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScope.From(request.OrganizationId, request.EnvironmentId);
        return await dbContext.LabelPrintBatches
            .Where(x => x.OrganizationId == tenant.OrganizationId
                && x.EnvironmentId == tenant.EnvironmentId
                && x.IdempotencyKey == request.IdempotencyKey)
            .Select(ScopedLabelPrintBatchProjection.Detail)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KnownException("未找到打印批次。");
    }
}

internal static class ScopedLabelPrintBatchProjection
{
    internal static readonly System.Linq.Expressions.Expression<Func<LabelPrintBatch, ScopedLabelPrintBatchDetail>> Detail =
        batch => new ScopedLabelPrintBatchDetail(
            batch.Id,
            batch.LabelTemplateId,
            batch.SourceDocumentType,
            batch.SourceDocumentId,
            batch.IdempotencyKey,
            batch.IdempotencyKey,
            batch.ReportIntentFingerprint,
            batch.RequestedQuantity,
            batch.Status,
            batch.PrinterId,
            batch.PrintJobId,
            batch.FailureReason,
            batch.ProductionReportId,
            batch.ProductionReportNo,
            batch.Items.OrderBy(item => item.SequenceNo).Select(item => new ScopedLabelPrintItemDetail(
                item.SequenceNo,
                item.LabelValue,
                item.FileId,
                item.Status,
                item.VoidReason,
                item.SerialNumber,
                item.LotNo,
                item.Gtin,
                item.EpcUri)).ToArray());
}
