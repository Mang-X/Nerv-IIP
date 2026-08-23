using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;
using Nerv.IIP.Business.ProductEngineering.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.ServiceAuth;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;

public sealed record RegisterEngineeringDocumentCommand(
    string OrganizationId,
    string EnvironmentId,
    string? DocumentNumber,
    string Revision,
    string FileId,
    string FileName,
    string ContentType,
    string DocumentType,
    string? IdempotencyKey = null,
    string? ItemCode = null) : ICommand<EntityCommandResult>;

public sealed record EntityCommandResult(string Id);

public sealed record ReleasedEngineeringVersionResult(string Id, string VersionId)
{
    public static ReleasedEngineeringVersionResult Create(string id, string revision)
    {
        var normalizedId = id.Trim();
        var normalizedRevision = revision.Trim();
        return new ReleasedEngineeringVersionResult(normalizedId, $"{normalizedId}:{normalizedRevision}");
    }
}

public sealed class RegisterEngineeringDocumentCommandValidator : AbstractValidator<RegisterEngineeringDocumentCommand>
{
    public RegisterEngineeringDocumentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentNumber).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(120);
        RuleFor(x => x.DocumentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ItemCode).MaximumLength(100);
    }
}

public sealed class RegisterEngineeringDocumentCommandHandler(IEngineeringDocumentRepository repository, ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<RegisterEngineeringDocumentCommand, EntityCommandResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();

    public async Task<EntityCommandResult> Handle(RegisterEngineeringDocumentCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "engineering-document",
            request.DocumentNumber,
            request.IdempotencyKey,
            DocumentPayloadFingerprint(request),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Code);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"文档号 {allocation.Code} 的修订 {request.Revision} 已登记，请换修订号或留空取号。");
        }

        var document = EngineeringDocument.Register(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.Revision,
            request.ItemCode,
            request.FileId,
            request.FileName,
            request.ContentType,
            request.DocumentType);
        await repository.AddAsync(document, cancellationToken);
        return new EntityCommandResult(document.DocumentNumber);
    }

    private static string DocumentPayloadFingerprint(RegisterEngineeringDocumentCommand request)
    {
        var itemCode = string.IsNullOrWhiteSpace(request.ItemCode) ? null : request.ItemCode.Trim();
        return itemCode is null
            ? ProductEngineeringCodingService.Fingerprint(request.Revision, request.FileId, request.FileName, request.ContentType, request.DocumentType)
            : ProductEngineeringCodingService.Fingerprint(request.Revision, itemCode, request.FileId, request.FileName, request.ContentType, request.DocumentType);
    }
}

public sealed record PublishSopDocumentCommand(
    string OrganizationId,
    string EnvironmentId,
    string? DocumentNumber,
    string Revision,
    string OperationCode,
    string? WorkCenterCode,
    string? RoutingCode,
    string? RoutingRevision,
    DateOnly EffectiveDate,
    string FileId,
    string FileName,
    string ContentType,
    string? IdempotencyKey = null) : ICommand<EntityCommandResult>;

public sealed class PublishSopDocumentCommandValidator : AbstractValidator<PublishSopDocumentCommand>
{
    public PublishSopDocumentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentNumber).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.OperationCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkCenterCode).MaximumLength(100);
        RuleFor(x => x.RoutingCode).MaximumLength(100);
        RuleFor(x => x.RoutingRevision).MaximumLength(50);
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(120);
    }
}

public sealed class PublishSopDocumentCommandHandler(IEngineeringDocumentRepository repository, ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<PublishSopDocumentCommand, EntityCommandResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();

    public async Task<EntityCommandResult> Handle(PublishSopDocumentCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "engineering-document",
            request.DocumentNumber,
            request.IdempotencyKey,
            ProductEngineeringCodingService.Fingerprint(
                request.Revision,
                request.OperationCode,
                request.WorkCenterCode ?? string.Empty,
                request.RoutingCode ?? string.Empty,
                request.RoutingRevision ?? string.Empty,
                request.EffectiveDate,
                request.FileId,
                request.FileName,
                request.ContentType,
                "sop"),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Code);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"SOP {allocation.Code} 的修订 {request.Revision} 已发布，请换修订号或留空取号。");
        }

        var document = EngineeringDocument.PublishSop(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.Revision,
            request.OperationCode,
            request.WorkCenterCode,
            request.RoutingCode,
            request.RoutingRevision,
            request.EffectiveDate,
            request.FileId,
            request.FileName,
            request.ContentType);
        await repository.AddAsync(document, cancellationToken);
        return new EntityCommandResult(document.DocumentNumber);
    }
}

public sealed record CreateEngineeringItemRevisionCommand(
    string OrganizationId,
    string EnvironmentId,
    string? ItemCode,
    string Revision,
    string Name,
    bool Release,
    string? IdempotencyKey = null) : ICommand<EntityCommandResult>;

public sealed class CreateEngineeringItemRevisionCommandValidator : AbstractValidator<CreateEngineeringItemRevisionCommand>
{
    public CreateEngineeringItemRevisionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ItemCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
    }
}

public sealed class CreateEngineeringItemRevisionCommandHandler(IEngineeringItemRepository repository, ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<CreateEngineeringItemRevisionCommand, EntityCommandResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();

    public async Task<EntityCommandResult> Handle(CreateEngineeringItemRevisionCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "engineering-item",
            request.ItemCode,
            request.IdempotencyKey,
            ProductEngineeringCodingService.Fingerprint(request.Revision, request.Name, request.Release),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Code);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"物料 {allocation.Code} 的修订 {request.Revision} 已存在，请换一个修订号。");
        }

        var item = EngineeringItem.CreateRevision(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.Revision,
            request.Name,
            request.Release);
        await repository.AddAsync(item, cancellationToken);
        return new EntityCommandResult(item.ItemCode);
    }
}

public sealed record ReleaseEngineeringBomCommand(
    string OrganizationId,
    string EnvironmentId,
    string? BomCode,
    string Revision,
    string ParentItemCode,
    DateOnly EffectiveDate,
    IReadOnlyCollection<BomLineCommand> Lines,
    string? IdempotencyKey = null) : ICommand<EntityCommandResult>;

public sealed record BomLineCommand(
    string ComponentCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    string? ReferenceDesignators = null,
    decimal ScrapRate = 0m,
    decimal YieldRate = 1m,
    bool Backflush = false);

public sealed class ReleaseEngineeringBomCommandValidator : AbstractValidator<ReleaseEngineeringBomCommand>
{
    public ReleaseEngineeringBomCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BomCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ParentItemCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ComponentCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitOfMeasureCode).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class ReleaseEngineeringBomCommandHandler(
    IEngineeringBomRepository repository,
    IProductEngineeringMasterDataReferenceValidator? masterDataReferenceValidator = null,
    ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<ReleaseEngineeringBomCommand, EntityCommandResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();
    private readonly IProductEngineeringMasterDataReferenceValidator _masterDataReferenceValidator = masterDataReferenceValidator ?? NoopProductEngineeringMasterDataReferenceValidator.Instance;

    public async Task<EntityCommandResult> Handle(ReleaseEngineeringBomCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "engineering-bom",
            request.BomCode,
            request.IdempotencyKey,
            ProductEngineeringCodingService.Fingerprint(request.Revision, request.ParentItemCode, request.EffectiveDate, request.Lines.Select(x => $"{x.ComponentCode}:{x.Quantity}:{x.UnitOfMeasureCode}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Code);
        }

        if (await repository.GetByBusinessKeyAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken) is not null)
        {
            throw new KnownException($"EBOM {allocation.Code} 的修订 {request.Revision} 已存在，请换一个修订号。");
        }

        if (await repository.HasPublishedRevisionAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"EBOM {allocation.Code} 已有已发布修订，请先通过工程变更归档现有修订。");
        }

        await _masterDataReferenceValidator.ValidateActiveReferencesAsync(
            request.OrganizationId,
            request.EnvironmentId,
            ProductEngineeringMasterDataReference.ForSkuCodes(
                [request.ParentItemCode, .. request.Lines.Select(line => line.ComponentCode)]),
            cancellationToken);

        var bom = ProductEngineeringReleaseValidation.AsKnownException(() =>
        {
            var draft = EngineeringBom.CreateDraft(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, request.ParentItemCode);
            foreach (var line in request.Lines)
            {
                draft.AddLine(
                    line.ComponentCode,
                    line.Quantity,
                    line.UnitOfMeasureCode,
                    line.IsPhantom,
                    line.AlternateGroup,
                    line.AlternatePriority,
                    line.ReferenceDesignators,
                    line.ScrapRate,
                    line.YieldRate,
                    line.Backflush);
            }

            draft.Release(request.EffectiveDate);
            return draft;
        }, "EBOM 发布失败，请检查物料行和生效日期。");
        await repository.AddAsync(bom, cancellationToken);
        return new EntityCommandResult(bom.BomCode);
    }
}

public sealed record ReleaseManufacturingBomCommand(
    string OrganizationId,
    string EnvironmentId,
    string? BomCode,
    string Revision,
    string SkuCode,
    string EngineeringBomCode,
    string EngineeringBomRevision,
    DateOnly EffectiveDate,
    IReadOnlyCollection<ManufacturingBomMaterialLineCommand> MaterialLines,
    IReadOnlyCollection<RecipeLineCommand> RecipeLines,
    string? IdempotencyKey = null) : ICommand<ReleasedEngineeringVersionResult>;

public sealed record ManufacturingBomMaterialLineCommand(
    string SkuCode,
    decimal Quantity,
    string UnitOfMeasureCode,
    decimal ScrapRate,
    bool IsPhantom = false,
    string? AlternateGroup = null,
    int? AlternatePriority = null,
    IReadOnlyCollection<string>? SubstituteSkuCodes = null,
    string? ReferenceDesignators = null,
    decimal YieldRate = 1m,
    bool Backflush = false);

public sealed record RecipeLineCommand(string ParameterCode, string TargetValue, string UnitOfMeasureCode);

public sealed class ReleaseManufacturingBomCommandValidator : AbstractValidator<ReleaseManufacturingBomCommand>
{
    public ReleaseManufacturingBomCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BomCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EngineeringBomCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EngineeringBomRevision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.MaterialLines).NotEmpty();
        RuleForEach(x => x.MaterialLines).ChildRules(line =>
        {
            line.RuleFor(x => x.SkuCode).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitOfMeasureCode).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(50);
            line.RuleFor(x => x.ScrapRate).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class ReleaseManufacturingBomCommandHandler(
    IEngineeringBomRepository engineeringBomRepository,
    IManufacturingBomRepository manufacturingBomRepository,
    IProductEngineeringMasterDataReferenceValidator? masterDataReferenceValidator = null,
    ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<ReleaseManufacturingBomCommand, ReleasedEngineeringVersionResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();
    private readonly IProductEngineeringMasterDataReferenceValidator _masterDataReferenceValidator = masterDataReferenceValidator ?? NoopProductEngineeringMasterDataReferenceValidator.Instance;

    public async Task<ReleasedEngineeringVersionResult> Handle(ReleaseManufacturingBomCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "manufacturing-bom",
            request.BomCode,
            request.IdempotencyKey,
            ProductEngineeringCodingService.Fingerprint(request.Revision, request.SkuCode, request.EngineeringBomCode, request.EngineeringBomRevision, request.EffectiveDate),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return ReleasedEngineeringVersionResult.Create(allocation.Code, request.Revision);
        }

        if (await manufacturingBomRepository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"MBOM {allocation.Code} 的修订 {request.Revision} 已存在，请换一个修订号。");
        }

        if (await manufacturingBomRepository.HasPublishedRevisionAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"MBOM {allocation.Code} 已有已发布修订，请先通过工程变更归档现有修订。");
        }

        var ebom = await engineeringBomRepository.GetByBusinessKeyAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.EngineeringBomCode,
            request.EngineeringBomRevision,
            cancellationToken)
            ?? throw new KnownException($"已发布 EBOM '{request.EngineeringBomCode}' 修订 '{request.EngineeringBomRevision}' 不存在。");

        ProductEngineeringReleaseValidation.ValidateManufacturingBomMaterialContinuity(ebom, request.SkuCode, request.MaterialLines);
        await _masterDataReferenceValidator.ValidateActiveReferencesAsync(
            request.OrganizationId,
            request.EnvironmentId,
            ProductEngineeringMasterDataReference.ForSkuCodes(GetManufacturingBomSkuCodes(request)),
            cancellationToken);

        var bom = ProductEngineeringReleaseValidation.AsKnownException(() =>
        {
            var draft = ManufacturingBom.CreateDraft(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, request.SkuCode);
            foreach (var line in request.MaterialLines)
            {
                draft.AddMaterialLine(
                    line.SkuCode,
                    line.Quantity,
                    line.UnitOfMeasureCode,
                    line.ScrapRate,
                    line.IsPhantom,
                    line.AlternateGroup,
                    line.AlternatePriority,
                    line.SubstituteSkuCodes is { Count: > 0 } ? string.Join(';', line.SubstituteSkuCodes.Select(x => x.Trim()).Where(x => x.Length > 0)) : null,
                    line.ReferenceDesignators,
                    line.YieldRate,
                    line.Backflush);
            }

            foreach (var line in request.RecipeLines)
            {
                draft.AddRecipeLine(line.ParameterCode, line.TargetValue, line.UnitOfMeasureCode);
            }

            draft.ReleaseFromEngineeringBom($"{ebom.BomCode}:{ebom.Revision}", ebom.Status, request.EffectiveDate);
            return draft;
        }, "MBOM 发布失败，请检查物料行、配方和来源 EBOM。");
        await manufacturingBomRepository.AddAsync(bom, cancellationToken);
        return ReleasedEngineeringVersionResult.Create(bom.BomCode, bom.Revision);
    }

    private static IReadOnlyCollection<string> GetManufacturingBomSkuCodes(ReleaseManufacturingBomCommand request)
    {
        return
        [
            request.SkuCode,
            .. request.MaterialLines.Select(line => line.SkuCode),
            .. request.MaterialLines.SelectMany(line => line.SubstituteSkuCodes ?? [])
        ];
    }
}

public sealed record ReleaseRoutingCommand(
    string OrganizationId,
    string EnvironmentId,
    string? RoutingCode,
    string Revision,
    string SkuCode,
    DateOnly EffectiveDate,
    IReadOnlyCollection<RoutingOperationCommand> Operations,
    string? IdempotencyKey = null) : ICommand<ReleasedEngineeringVersionResult>;

public sealed record RoutingOperationCommand(int Sequence, string? WorkCenterCode, string OperationCode, string? OperationName, int StandardMinutes = 0);

public sealed class ReleaseRoutingCommandValidator : AbstractValidator<ReleaseRoutingCommand>
{
    public ReleaseRoutingCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingCode).MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Operations).NotEmpty();
        RuleForEach(x => x.Operations).ChildRules(operation =>
        {
            operation.RuleFor(x => x.Sequence).GreaterThan(0);
            operation.RuleFor(x => x.WorkCenterCode).MaximumLength(100);
            operation.RuleFor(x => x.OperationCode).Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(100);
            operation.RuleFor(x => x.OperationName).MaximumLength(200);
            operation.RuleFor(x => x.StandardMinutes).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class ReleaseRoutingCommandHandler(
    IRoutingRepository repository,
    IStandardOperationRepository standardOperationRepository,
    IProductEngineeringMasterDataReferenceValidator? masterDataReferenceValidator = null,
    ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<ReleaseRoutingCommand, ReleasedEngineeringVersionResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();
    private readonly IProductEngineeringMasterDataReferenceValidator _masterDataReferenceValidator = masterDataReferenceValidator ?? NoopProductEngineeringMasterDataReferenceValidator.Instance;

    public async Task<ReleasedEngineeringVersionResult> Handle(ReleaseRoutingCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "routing",
            request.RoutingCode,
            request.IdempotencyKey,
            ProductEngineeringCodingService.Fingerprint(request.Revision, request.SkuCode, request.EffectiveDate, request.Operations.Select(x => $"{x.Sequence}:{x.OperationCode}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return ReleasedEngineeringVersionResult.Create(allocation.Code, request.Revision);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"工艺路线 {allocation.Code} 的修订 {request.Revision} 已存在，请换一个修订号。");
        }

        if (await repository.HasPublishedRevisionAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"工艺路线 {allocation.Code} 已有已发布修订，请先通过工程变更归档现有修订。");
        }

        var standardOperations = new Dictionary<string, StandardOperation>(StringComparer.Ordinal);
        foreach (var operation in request.Operations)
        {
            var standardOperation = await standardOperationRepository.GetByCodeAsync(
                request.OrganizationId,
                request.EnvironmentId,
                operation.OperationCode,
                cancellationToken)
                ?? throw new KnownException($"标准工序 '{operation.OperationCode}' 不存在。");

            if (!standardOperation.Enabled)
            {
                throw new KnownException($"标准工序 '{operation.OperationCode}' 已归档，不能用于新工艺路线版本。");
            }

            standardOperations[operation.OperationCode] = standardOperation;
        }

        await _masterDataReferenceValidator.ValidateActiveReferencesAsync(
            request.OrganizationId,
            request.EnvironmentId,
            ProductEngineeringMasterDataReference.Distinct(
                [new ProductEngineeringMasterDataReference("sku", request.SkuCode),
                    .. standardOperations.Values.Select(operation => new ProductEngineeringMasterDataReference("work-center", operation.DefaultWorkCenterCode))]),
            cancellationToken);

        var routing = ProductEngineeringReleaseValidation.AsKnownException(() =>
        {
            var draft = Routing.CreateDraft(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, request.SkuCode);
            foreach (var operation in request.Operations)
            {
                var standardOperation = standardOperations[operation.OperationCode];

                draft.AddOperation(
                    operation.Sequence,
                    standardOperation.DefaultWorkCenterCode,
                    standardOperation.OperationCode,
                    standardOperation.OperationName,
                    standardOperation.StandardSetupMinutes,
                    standardOperation.StandardRunMinutes,
                    teardownMinutes: 0,
                    standardOperation.ControlKey,
                    standardOperation.RequiresReporting,
                    standardOperation.RequiresQualityInspection,
                    standardOperation.IsOutsourced);
            }

            draft.Release(request.EffectiveDate);
            return draft;
        }, "工艺路线发布失败，请检查工序和生效日期。");
        await repository.AddAsync(routing, cancellationToken);
        return ReleasedEngineeringVersionResult.Create(routing.RoutingCode, routing.Revision);
    }
}

internal static class ProductEngineeringReleaseValidation
{
    public static void ValidateManufacturingBomMaterialContinuity(
        EngineeringBom engineeringBom,
        string manufacturingBomSkuCode,
        IReadOnlyCollection<ManufacturingBomMaterialLineCommand> materialLines)
    {
        var normalizedManufacturingBomSkuCode = manufacturingBomSkuCode.Trim();
        if (!string.Equals(engineeringBom.ParentItemCode, normalizedManufacturingBomSkuCode, StringComparison.Ordinal))
        {
            throw new KnownException($"MBOM SKU '{normalizedManufacturingBomSkuCode}' 必须与 EBOM 父 SKU '{engineeringBom.ParentItemCode}' 一致。");
        }

        var requiredEbomChildSkuCodes = engineeringBom.Lines
            .Where(line => !line.IsPhantom)
            .Select(line => line.ChildItemCode)
            .ToHashSet(StringComparer.Ordinal);
        var mbomMaterialSkuCodes = materialLines
            .Select(line => line.SkuCode?.Trim() ?? string.Empty)
            .Where(code => code.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var missingMaterialSkuCodes = requiredEbomChildSkuCodes
            .Where(code => !mbomMaterialSkuCodes.Contains(code))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missingMaterialSkuCodes.Length > 0)
        {
            throw new KnownException($"MBOM 缺少 EBOM 子 SKU 的物料行：{string.Join(", ", missingMaterialSkuCodes)}。");
        }
    }

    public static T AsKnownException<T>(Func<T> action, string? message = null)
    {
        try
        {
            // Keep the action limited to aggregate construction and invariant checks.
            return action();
        }
        catch (InvalidOperationException exception)
        {
            var resolvedMessage = message ?? exception.Message;
            throw new KnownException(resolvedMessage, exception);
        }
        catch (ArgumentException exception)
        {
            var resolvedMessage = message ?? exception.Message;
            throw new KnownException(resolvedMessage, exception);
        }
    }

    public static void AsKnownException(Action action, string? message = null)
    {
        try
        {
            // Keep the action limited to aggregate construction and invariant checks.
            action();
        }
        catch (InvalidOperationException exception)
        {
            var resolvedMessage = message ?? exception.Message;
            throw new KnownException(resolvedMessage, exception);
        }
        catch (ArgumentException exception)
        {
            var resolvedMessage = message ?? exception.Message;
            throw new KnownException(resolvedMessage, exception);
        }
    }
}

public sealed record ProductEngineeringMasterDataReference(string ResourceType, string Code)
{
    public static IReadOnlyCollection<ProductEngineeringMasterDataReference> ForSkuCodes(IEnumerable<string?> skuCodes)
    {
        return Distinct(skuCodes.Select(code => new ProductEngineeringMasterDataReference("sku", code ?? string.Empty)));
    }

    public static IReadOnlyCollection<ProductEngineeringMasterDataReference> Distinct(IEnumerable<ProductEngineeringMasterDataReference> references)
    {
        var results = new List<ProductEngineeringMasterDataReference>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            var resourceType = reference.ResourceType.Trim();
            var code = reference.Code.Trim();
            if (resourceType.Length == 0 || code.Length == 0)
            {
                continue;
            }

            if (keys.Add($"{resourceType}:{code}"))
            {
                results.Add(new ProductEngineeringMasterDataReference(resourceType, code));
            }
        }

        return results;
    }
}

public interface IProductEngineeringMasterDataReferenceValidator
{
    Task ValidateActiveReferencesAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<ProductEngineeringMasterDataReference> references,
        CancellationToken cancellationToken);
}

internal sealed class NoopProductEngineeringMasterDataReferenceValidator : IProductEngineeringMasterDataReferenceValidator
{
    public static readonly NoopProductEngineeringMasterDataReferenceValidator Instance = new();

    private NoopProductEngineeringMasterDataReferenceValidator()
    {
    }

    public Task ValidateActiveReferencesAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<ProductEngineeringMasterDataReference> references,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class HttpProductEngineeringMasterDataReferenceValidator(HttpClient httpClient, IInternalServiceTokenProvider tokenProvider)
    : IProductEngineeringMasterDataReferenceValidator
{
    public async Task ValidateActiveReferencesAsync(
        string organizationId,
        string environmentId,
        IReadOnlyCollection<ProductEngineeringMasterDataReference> references,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0)
        {
            return;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/business/v1/master-data/references/validate")
        {
            Content = JsonContent.Create(new ValidateMasterDataReferencesRequest(
                organizationId,
                environmentId,
                references.Select(reference => new MasterDataReferenceRequest(reference.ResourceType, reference.Code)).ToArray()))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new KnownException("主数据引用校验服务暂不可用，请稍后重试。");
        }

        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<ValidateMasterDataReferencesResponse>>(cancellationToken);
        var validation = envelope?.Data ?? throw new KnownException("主数据引用校验返回无效结果，请稍后重试。");
        if (validation.Valid)
        {
            return;
        }

        throw new KnownException("存在缺失或未启用的主数据引用，请检查后重试。");
    }

    private sealed record ValidateMasterDataReferencesRequest(
        string OrganizationId,
        string EnvironmentId,
        IReadOnlyCollection<MasterDataReferenceRequest> References);

    private sealed record MasterDataReferenceRequest(string ResourceType, string Code);

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);

    private sealed record ValidateMasterDataReferencesResponse(
        bool Valid,
        IReadOnlyCollection<MasterDataReferenceResponse> References);

    private sealed record MasterDataReferenceResponse(
        string ResourceType,
        string Code,
        bool Exists,
        bool Active,
        string DisplayName,
        string SnapshotVersion,
        string DisabledReason);
}

public sealed record ReleaseEngineeringChangeCommand(
    string OrganizationId,
    string EnvironmentId,
    string? ChangeNumber,
    string Reason,
    string ApprovalReferenceId,
    DateOnly EffectiveDate,
    IReadOnlyCollection<AffectedVersionCommand> AffectedVersions,
    string? IdempotencyKey = null) : ICommand<EntityCommandResult>;

public sealed record AffectedVersionCommand(string VersionKind, string VersionId, string? SupersededByVersionId = null);

public sealed class ReleaseEngineeringChangeCommandValidator : AbstractValidator<ReleaseEngineeringChangeCommand>
{
    public ReleaseEngineeringChangeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeNumber).MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ApprovalReferenceId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AffectedVersions).NotEmpty();
        RuleForEach(x => x.AffectedVersions).ChildRules(affectedVersion =>
        {
            affectedVersion.RuleFor(x => x.VersionKind).NotEmpty().MaximumLength(100);
            affectedVersion.RuleFor(x => x.VersionId).NotEmpty().MaximumLength(150);
            affectedVersion.RuleFor(x => x.SupersededByVersionId).MaximumLength(150);
        });
    }
}

public sealed class ReleaseEngineeringChangeCommandHandler(
    IEngineeringChangeRepository repository,
    IEngineeringBomRepository engineeringBomRepository,
    IManufacturingBomRepository manufacturingBomRepository,
    IRoutingRepository routingRepository,
    IProductionVersionRepository productionVersionRepository,
    IEngineeringApprovalVerifier? approvalVerifier = null,
    ProductEngineeringCodingService? codingService = null,
    IProductEngineeringBusinessDateProvider? businessDateProvider = null,
    IEngineeringDocumentRepository? engineeringDocumentRepository = null)
    : ICommandHandler<ReleaseEngineeringChangeCommand, EntityCommandResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();
    private readonly IEngineeringApprovalVerifier _approvalVerifier = approvalVerifier ?? new RejectingEngineeringApprovalVerifier();
    private readonly IProductEngineeringBusinessDateProvider _businessDateProvider = businessDateProvider ?? UtcProductEngineeringBusinessDateProvider.Instance;

    public async Task<EntityCommandResult> Handle(ReleaseEngineeringChangeCommand request, CancellationToken cancellationToken)
    {
        var normalizedAffectedVersions = NormalizeAffectedVersions(request.AffectedVersions);
        EnsureAcyclicSupersedeTopology(normalizedAffectedVersions);
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "engineering-change",
            request.ChangeNumber,
            request.IdempotencyKey,
            ProductEngineeringCodingService.Fingerprint(request.Reason, request.ApprovalReferenceId, request.EffectiveDate, normalizedAffectedVersions.Select(x => $"{x.VersionKind}:{x.VersionId}->{x.SupersededByVersionId ?? string.Empty}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Code);
        }

        await _approvalVerifier.EnsureApprovedAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.ApprovalReferenceId,
            allocation.Code,
            cancellationToken);

        var affectedVersions = new List<Action<string, DateOnly>>();
        var change = EngineeringChange.Open(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Reason)
            .Approve(request.ApprovalReferenceId);
        foreach (var affectedVersion in normalizedAffectedVersions)
        {
            affectedVersions.Add(await ResolveAffectedVersionAsync(request, affectedVersion, cancellationToken));
            change.Affect(affectedVersion.VersionKind, affectedVersion.VersionId, affectedVersion.SupersededByVersionId);
        }

        if (request.EffectiveDate > _businessDateProvider.GetBusinessDate())
        {
            change.Schedule(request.EffectiveDate);
        }
        else
        {
            ProductEngineeringReleaseValidation.AsKnownException(
                () => change.Release(request.EffectiveDate),
                "工程变更发布失败，请检查变更状态和受影响版本。");
            foreach (var archive in affectedVersions)
            {
                archive(change.ChangeNumber, request.EffectiveDate);
            }
        }

        await repository.AddAsync(change, cancellationToken);
        return new EntityCommandResult(change.ChangeNumber);
    }

    private async Task<Action<string, DateOnly>> ResolveAffectedVersionAsync(
        ReleaseEngineeringChangeCommand request,
        AffectedVersionCommand affectedVersion,
        CancellationToken cancellationToken)
    {
        return affectedVersion.VersionKind.Trim().ToLowerInvariant() switch
        {
            "engineering-bom" => ArchiveEngineeringBom(await engineeringBomRepository.GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId, await GetSuccessorEngineeringBomAsync(request, affectedVersion, cancellationToken)),
            "manufacturing-bom" => ArchiveManufacturingBom(await manufacturingBomRepository.GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId, await GetSuccessorManufacturingBomAsync(request, affectedVersion, cancellationToken)),
            "routing" => ArchiveRouting(await routingRepository.GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId, await GetSuccessorRoutingAsync(request, affectedVersion, cancellationToken)),
            "production-version" => ArchiveProductionVersion(await productionVersionRepository.GetByIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId, await GetSuccessorProductionVersionAsync(request, affectedVersion, cancellationToken)),
            "engineering-document" => ArchiveEngineeringDocument(await GetEngineeringDocumentRepository().GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId, await GetSuccessorEngineeringDocumentAsync(request, affectedVersion, cancellationToken)),
            _ => throw new KnownException($"受影响版本 '{affectedVersion.VersionKind}:{affectedVersion.VersionId}' 不受支持，请检查提交内容。")
        };
    }

    private static IReadOnlyList<AffectedVersionCommand> NormalizeAffectedVersions(IEnumerable<AffectedVersionCommand> affectedVersions)
    {
        return affectedVersions.Select(affectedVersion => new AffectedVersionCommand(
            NormalizeRequired(affectedVersion.VersionKind, nameof(AffectedVersionCommand.VersionKind)).ToLowerInvariant(),
            NormalizeRequired(affectedVersion.VersionId, nameof(AffectedVersionCommand.VersionId)),
            NormalizeOptional(affectedVersion.SupersededByVersionId))).ToArray();
    }

    private static void EnsureAcyclicSupersedeTopology(IReadOnlyList<AffectedVersionCommand> affectedVersions)
    {
        var edgesByVersion = new Dictionary<string, AffectedVersionCommand>(StringComparer.Ordinal);
        foreach (var affectedVersion in affectedVersions)
        {
            var key = AffectedVersionKey(affectedVersion.VersionKind, affectedVersion.VersionId);
            if (affectedVersion.SupersededByVersionId is not null &&
                string.Equals(affectedVersion.VersionId, affectedVersion.SupersededByVersionId, StringComparison.OrdinalIgnoreCase))
            {
                throw new KnownException($"受影响版本 '{affectedVersion.VersionKind}:{affectedVersion.VersionId}' 不能将自身设为替代版本，请修改替代版本。");
            }

            if (edgesByVersion.TryGetValue(key, out var existing))
            {
                if (!string.Equals(existing.SupersededByVersionId ?? string.Empty, affectedVersion.SupersededByVersionId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    throw new KnownException($"受影响版本 '{affectedVersion.VersionKind}:{affectedVersion.VersionId}' 已指定其他替代版本，请删除重复项。");
                }

                throw new KnownException($"受影响版本 '{affectedVersion.VersionKind}:{affectedVersion.VersionId}' 重复声明，请保留一项。");
            }

            edgesByVersion.Add(key, affectedVersion);
        }

        foreach (var affectedVersion in edgesByVersion.Values)
        {
            if (affectedVersion.SupersededByVersionId is null)
            {
                continue;
            }

            var startKey = AffectedVersionKey(affectedVersion.VersionKind, affectedVersion.VersionId);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = affectedVersion;
            while (current.SupersededByVersionId is not null)
            {
                var currentKey = AffectedVersionKey(current.VersionKind, current.VersionId);
                if (!visited.Add(currentKey))
                {
                    throw SupersedeCycleException(affectedVersion);
                }

                var successorKey = AffectedVersionKey(current.VersionKind, current.SupersededByVersionId);
                if (successorKey == startKey || visited.Contains(successorKey))
                {
                    throw SupersedeCycleException(affectedVersion);
                }

                if (!edgesByVersion.TryGetValue(successorKey, out current))
                {
                    break;
                }
            }
        }
    }

    private static KnownException SupersedeCycleException(AffectedVersionCommand start)
    {
        return new KnownException($"受影响版本 '{start.VersionKind}:{start.VersionId}' 的替代关系形成循环，请修改替代版本。");
    }

    private static string AffectedVersionKey(string versionKind, string versionId)
    {
        return $"{versionKind}\u001F{versionId.ToUpperInvariant()}";
    }

    private static string NormalizeRequired(string value, string fieldName)
    {
        var displayName = fieldName switch
        {
            nameof(AffectedVersionCommand.VersionKind) => "受影响版本类型",
            nameof(AffectedVersionCommand.VersionId) => "受影响版本标识",
            _ => throw new UnreachableException($"不支持的工程变更字段：{fieldName}")
        };
        return string.IsNullOrWhiteSpace(value)
            ? throw new KnownException($"{displayName}不能为空。")
            : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private async Task<EngineeringBom?> GetSuccessorEngineeringBomAsync(
        ReleaseEngineeringChangeCommand request,
        AffectedVersionCommand affectedVersion,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(affectedVersion.SupersededByVersionId)
            ? null
            : await engineeringBomRepository.GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.SupersededByVersionId,
                cancellationToken)
            ?? throw new KnownException($"替代工程 BOM 版本 '{affectedVersion.SupersededByVersionId}' 不存在。");
    }

    private async Task<ManufacturingBom?> GetSuccessorManufacturingBomAsync(
        ReleaseEngineeringChangeCommand request,
        AffectedVersionCommand affectedVersion,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(affectedVersion.SupersededByVersionId)
            ? null
            : await manufacturingBomRepository.GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.SupersededByVersionId,
                cancellationToken)
            ?? throw new KnownException($"替代制造 BOM 版本 '{affectedVersion.SupersededByVersionId}' 不存在。");
    }

    private async Task<Routing?> GetSuccessorRoutingAsync(
        ReleaseEngineeringChangeCommand request,
        AffectedVersionCommand affectedVersion,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(affectedVersion.SupersededByVersionId)
            ? null
            : await routingRepository.GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.SupersededByVersionId,
                cancellationToken)
            ?? throw new KnownException($"替代工艺路线版本 '{affectedVersion.SupersededByVersionId}' 不存在。");
    }

    private async Task<ProductionVersion?> GetSuccessorProductionVersionAsync(
        ReleaseEngineeringChangeCommand request,
        AffectedVersionCommand affectedVersion,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(affectedVersion.SupersededByVersionId)
            ? null
            : await productionVersionRepository.GetByIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.SupersededByVersionId,
                cancellationToken)
            ?? throw new KnownException($"替代生产版本 '{affectedVersion.SupersededByVersionId}' 不存在。");
    }

    private async Task<EngineeringDocument?> GetSuccessorEngineeringDocumentAsync(
        ReleaseEngineeringChangeCommand request,
        AffectedVersionCommand affectedVersion,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(affectedVersion.SupersededByVersionId)
            ? null
            : await GetEngineeringDocumentRepository().GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.SupersededByVersionId,
                cancellationToken)
            ?? throw new KnownException($"替代工程文档版本 '{affectedVersion.SupersededByVersionId}' 不存在。");
    }

    private static Action<string, DateOnly> ArchiveEngineeringBom(EngineeringBom? bom, string versionId, EngineeringBom? successor)
    {
        if (bom is not null && successor is not null)
        {
            EnsurePublishedSuccessor("工程 BOM", successor.Status, successor.BomCode == bom.BomCode, successor.BomCode);
        }

        return bom is null
            ? throw new KnownException($"工程 BOM 版本 '{versionId}' 不存在。")
            : (reason, _) => ProductEngineeringReleaseValidation.AsKnownException(
                () => bom.Archive(reason),
                "工程 BOM 归档失败，请检查版本状态和替代版本。");
    }

    private static Action<string, DateOnly> ArchiveManufacturingBom(ManufacturingBom? bom, string versionId, ManufacturingBom? successor)
    {
        if (bom is not null && successor is not null)
        {
            EnsurePublishedSuccessor("制造 BOM", successor.Status, successor.BomCode == bom.BomCode, successor.BomCode);
        }

        return bom is null
            ? throw new KnownException($"制造 BOM 版本 '{versionId}' 不存在。")
            : (reason, _) => ProductEngineeringReleaseValidation.AsKnownException(
                () => bom.Archive(reason),
                "制造 BOM 归档失败，请检查版本状态和替代版本。");
    }

    private static Action<string, DateOnly> ArchiveRouting(Routing? routing, string versionId, Routing? successor)
    {
        if (routing is not null && successor is not null)
        {
            EnsurePublishedSuccessor("工艺路线", successor.Status, successor.RoutingCode == routing.RoutingCode, successor.RoutingCode);
        }

        return routing is null
            ? throw new KnownException($"工艺路线版本 '{versionId}' 不存在。")
            : (reason, _) => ProductEngineeringReleaseValidation.AsKnownException(
                () => routing.Archive(reason),
                "工艺路线归档失败，请检查版本状态和替代版本。");
    }

    private static Action<string, DateOnly> ArchiveProductionVersion(ProductionVersion? version, string versionId, ProductionVersion? successor)
    {
        if (version is not null && successor is not null)
        {
            EnsureActiveSuccessor(successor, version);
        }

        return version is null
            ? throw new KnownException($"生产版本 '{versionId}' 不存在。")
            : successor is null
                ? (reason, _) => ProductEngineeringReleaseValidation.AsKnownException(
                    () => version.Archive(reason),
                    "生产版本归档失败，请检查版本状态和生效日期。")
                : (reason, effectiveDate) => ProductEngineeringReleaseValidation.AsKnownException(
                    () => version.SupersedeWith(successor, effectiveDate, reason),
                    "生产版本替代失败，请检查版本状态、生效日期和替代版本窗口。");
    }

    private static Action<string, DateOnly> ArchiveEngineeringDocument(EngineeringDocument? document, string versionId, EngineeringDocument? successor)
    {
        if (document is not null && successor is not null)
        {
            EnsurePublishedSuccessor("工程文档", successor.Status, successor.DocumentNumber == document.DocumentNumber, successor.DocumentNumber);
        }

        return document is null
            ? throw new KnownException($"工程文档版本 '{versionId}' 不存在。")
            : (reason, _) => ProductEngineeringReleaseValidation.AsKnownException(
                () => document.Archive(reason),
                "工程文档归档失败，请检查版本状态和替代版本。");
    }

    private IEngineeringDocumentRepository GetEngineeringDocumentRepository()
    {
        return engineeringDocumentRepository
            ?? throw new KnownException("发布工程文档变更需要配置工程文档仓储。");
    }

    private static void EnsurePublishedSuccessor(
        string versionType,
        EngineeringVersionStatus status,
        bool sameBusinessCode,
        string successorCode)
    {
        if (status != EngineeringVersionStatus.Published || !sameBusinessCode)
        {
            throw new KnownException($"替代{versionType} '{successorCode}' 必须与原版本使用相同编码且已发布。");
        }
    }

    private static void EnsureActiveSuccessor(ProductionVersion successor, ProductionVersion version)
    {
        if (successor.Status != ProductionVersionStatus.Active || successor.SkuCode != version.SkuCode)
        {
            throw new KnownException("替代生产版本的 SKU 或状态不符合要求，请检查替代版本。");
        }
    }
}

public sealed record PromoteScheduledEngineeringChangeCommand(
    string OrganizationId,
    string EnvironmentId,
    string ChangeNumber,
    DateOnly BusinessDate) : ICommand<bool>;

public sealed class PromoteScheduledEngineeringChangeCommandLock : ICommandLock<PromoteScheduledEngineeringChangeCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(PromoteScheduledEngineeringChangeCommand command, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var lockKey = string.Join(
            ':',
            "business-product-engineering",
            "eco-scheduled-release",
            Normalize(command.OrganizationId),
            Normalize(command.EnvironmentId),
            Normalize(command.ChangeNumber),
            command.BusinessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        return Task.FromResult(new CommandLockSettings(lockKey, 30));
    }

    private static string Normalize(string value)
    {
        return Uri.EscapeDataString(value.Trim().ToLowerInvariant());
    }
}

public sealed class PromoteScheduledEngineeringChangeCommandHandler(
    ApplicationDbContext dbContext,
    IEngineeringBomRepository engineeringBomRepository,
    IManufacturingBomRepository manufacturingBomRepository,
    IRoutingRepository routingRepository,
    IProductionVersionRepository productionVersionRepository,
    IEngineeringDocumentRepository? engineeringDocumentRepository = null)
    : ICommandHandler<PromoteScheduledEngineeringChangeCommand, bool>
{
    public async Task<bool> Handle(PromoteScheduledEngineeringChangeCommand request, CancellationToken cancellationToken)
    {
        var change = await dbContext.EngineeringChanges
            .Include(x => x.AffectedVersions)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.ChangeNumber == request.ChangeNumber,
                cancellationToken)
            ?? throw new KnownException($"Engineering change '{request.ChangeNumber}' was not found.");
        if (change.Status != EngineeringVersionStatus.Scheduled ||
            !change.EffectiveDate.HasValue ||
            change.EffectiveDate.Value > request.BusinessDate)
        {
            return false;
        }

        var effectiveDate = change.EffectiveDate.Value;
        var resolver = new ScheduledEngineeringChangeArchiveResolver(
            engineeringBomRepository,
            manufacturingBomRepository,
            routingRepository,
            productionVersionRepository,
            engineeringDocumentRepository);
        var archiveActions = await resolver.ResolveArchiveActionsAsync(change, cancellationToken);
        foreach (var archive in archiveActions)
        {
            archive(change.ChangeNumber, effectiveDate);
        }

        ProductEngineeringReleaseValidation.AsKnownException(() => change.Release(effectiveDate));
        return true;
    }
}

public sealed record CancelScheduledEngineeringChangeCommand(
    string OrganizationId,
    string EnvironmentId,
    string ChangeNumber,
    string Reason) : ICommand;

public sealed class CancelScheduledEngineeringChangeCommandValidator : AbstractValidator<CancelScheduledEngineeringChangeCommand>
{
    public CancelScheduledEngineeringChangeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class CancelScheduledEngineeringChangeCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CancelScheduledEngineeringChangeCommand>
{
    public async Task Handle(CancelScheduledEngineeringChangeCommand request, CancellationToken cancellationToken)
    {
        var change = await dbContext.EngineeringChanges.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.ChangeNumber == request.ChangeNumber,
            cancellationToken)
            ?? throw new KnownException($"工程变更 '{request.ChangeNumber}' 不存在。");

        ProductEngineeringReleaseValidation.AsKnownException(
            change.CancelScheduled,
            "取消工程变更失败，请确认变更处于已排期状态。");
    }
}

public sealed record RescheduleEngineeringChangeCommand(
    string OrganizationId,
    string EnvironmentId,
    string ChangeNumber,
    DateOnly EffectiveDate,
    string Reason) : ICommand;

public sealed class RescheduleEngineeringChangeCommandValidator : AbstractValidator<RescheduleEngineeringChangeCommand>
{
    public RescheduleEngineeringChangeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RescheduleEngineeringChangeCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RescheduleEngineeringChangeCommand>
{
    public async Task Handle(RescheduleEngineeringChangeCommand request, CancellationToken cancellationToken)
    {
        var change = await dbContext.EngineeringChanges.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.ChangeNumber == request.ChangeNumber,
            cancellationToken)
            ?? throw new KnownException($"工程变更 '{request.ChangeNumber}' 不存在。");

        ProductEngineeringReleaseValidation.AsKnownException(
            () => change.Reschedule(request.EffectiveDate),
            "改期工程变更失败，请确认变更处于已排期状态。");
    }
}

public interface IEngineeringApprovalVerifier
{
    Task EnsureApprovedAsync(
        string organizationId,
        string environmentId,
        string approvalReferenceId,
        string changeNumber,
        CancellationToken cancellationToken);
}

internal sealed class RejectingEngineeringApprovalVerifier : IEngineeringApprovalVerifier
{
    public Task EnsureApprovedAsync(
        string organizationId,
        string environmentId,
        string approvalReferenceId,
        string changeNumber,
        CancellationToken cancellationToken)
    {
        throw new KnownException("Engineering change release requires a verified approved BusinessApproval chain.");
    }
}

public sealed class HttpEngineeringApprovalVerifier(HttpClient httpClient, IInternalServiceTokenProvider tokenProvider)
    : IEngineeringApprovalVerifier
{
    public async Task EnsureApprovedAsync(
        string organizationId,
        string environmentId,
        string approvalReferenceId,
        string changeNumber,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(approvalReferenceId, out _))
        {
            throw new KnownException("审批引用标识必须是 BusinessApproval 审批链 ID。");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/business/v1/approvals/chains/{Uri.EscapeDataString(approvalReferenceId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider.BearerToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new KnownException("BusinessApproval 审批链校验失败，请稍后重试。");
        }

        var envelope = await response.Content.ReadFromJsonAsync<ApprovalChainEnvelope>(cancellationToken);
        var chain = envelope?.Data ?? throw new KnownException("BusinessApproval 审批链返回为空，请稍后重试。");
        ValidateApprovedChain(chain, organizationId, environmentId, changeNumber);
    }

    private static void ValidateApprovedChain(
        ApprovalChainResponse chain,
        string organizationId,
        string environmentId,
        string changeNumber)
    {
        if (!string.Equals(chain.OrganizationId, organizationId, StringComparison.Ordinal)
            || !string.Equals(chain.EnvironmentId, environmentId, StringComparison.Ordinal)
            || !string.Equals(chain.Status, ApprovalChainStatuses.Approved, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(chain.TemplateCode, ApprovalTemplateCodes.EngineeringChangeOrder, StringComparison.Ordinal)
            || !string.Equals(chain.SourceService, ApprovalSourceServices.ProductEngineering, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(chain.DocumentType, ApprovalDocumentTypes.EngineeringChangeOrder, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(chain.DocumentId, changeNumber, StringComparison.Ordinal))
        {
            throw new KnownException("工程变更发布需要同一工程变更的已批准 BusinessApproval 审批链。");
        }
    }

    private sealed record ApprovalChainEnvelope(ApprovalChainResponse? Data);

    private sealed record ApprovalChainResponse(
        string OrganizationId,
        string EnvironmentId,
        string Status,
        string TemplateCode,
        string SourceService,
        string DocumentType,
        string DocumentId);
}
