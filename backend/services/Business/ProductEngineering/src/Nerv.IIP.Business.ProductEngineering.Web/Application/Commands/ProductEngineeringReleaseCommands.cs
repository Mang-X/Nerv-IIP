using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.StandardOperationAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

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
            throw new KnownException($"Engineering document '{allocation.Code}' revision '{request.Revision}' already exists.");
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
            throw new KnownException($"Engineering item '{allocation.Code}' revision '{request.Revision}' already exists.");
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

public sealed class ReleaseEngineeringBomCommandHandler(IEngineeringBomRepository repository, ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<ReleaseEngineeringBomCommand, EntityCommandResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();

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
            throw new KnownException($"Engineering BOM '{allocation.Code}' revision '{request.Revision}' already exists.");
        }

        if (await repository.HasPublishedRevisionAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"Engineering BOM '{allocation.Code}' already has a published revision. Archive the current published revision through ECO before releasing a new revision.");
        }

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
        });
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
    string? IdempotencyKey = null) : ICommand<EntityCommandResult>;

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
    }
}

public sealed class ReleaseManufacturingBomCommandHandler(
    IEngineeringBomRepository engineeringBomRepository,
    IManufacturingBomRepository manufacturingBomRepository,
    ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<ReleaseManufacturingBomCommand, EntityCommandResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();

    public async Task<EntityCommandResult> Handle(ReleaseManufacturingBomCommand request, CancellationToken cancellationToken)
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
            return new EntityCommandResult(allocation.Code);
        }

        if (await manufacturingBomRepository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"Manufacturing BOM '{allocation.Code}' revision '{request.Revision}' already exists.");
        }

        if (await manufacturingBomRepository.HasPublishedRevisionAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"Manufacturing BOM '{allocation.Code}' already has a published revision. Archive the current published revision through ECO before releasing a new revision.");
        }

        var ebom = await engineeringBomRepository.GetByBusinessKeyAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.EngineeringBomCode,
            request.EngineeringBomRevision,
            cancellationToken)
            ?? throw new KnownException($"Released engineering BOM '{request.EngineeringBomCode}' revision '{request.EngineeringBomRevision}' was not found.");

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
        });
        await manufacturingBomRepository.AddAsync(bom, cancellationToken);
        return new EntityCommandResult(bom.BomCode);
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
    string? IdempotencyKey = null) : ICommand<EntityCommandResult>;

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
    ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<ReleaseRoutingCommand, EntityCommandResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();

    public async Task<EntityCommandResult> Handle(ReleaseRoutingCommand request, CancellationToken cancellationToken)
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
            return new EntityCommandResult(allocation.Code);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"Routing '{allocation.Code}' revision '{request.Revision}' already exists.");
        }

        if (await repository.HasPublishedRevisionAsync(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Revision, cancellationToken))
        {
            throw new KnownException($"Routing '{allocation.Code}' already has a published revision. Archive the current published revision through ECO before releasing a new revision.");
        }

        var standardOperations = new Dictionary<string, StandardOperation>(StringComparer.Ordinal);
        foreach (var operation in request.Operations)
        {
            var standardOperation = await standardOperationRepository.GetByCodeAsync(
                request.OrganizationId,
                request.EnvironmentId,
                operation.OperationCode,
                cancellationToken)
                ?? throw new KnownException($"Standard operation '{operation.OperationCode}' was not found.");

            if (!standardOperation.Enabled)
            {
                throw new KnownException($"Standard operation '{operation.OperationCode}' is archived and cannot be selected by a new routing version.");
            }

            standardOperations[operation.OperationCode] = standardOperation;
        }

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
        });
        await repository.AddAsync(routing, cancellationToken);
        return new EntityCommandResult(routing.RoutingCode);
    }
}

internal static class ProductEngineeringReleaseValidation
{
    public static T AsKnownException<T>(Func<T> action)
    {
        try
        {
            // Keep the action limited to aggregate construction and invariant checks.
            return action();
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
        catch (ArgumentException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
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

public sealed record AffectedVersionCommand(string VersionKind, string VersionId);

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
    }
}

public sealed class ReleaseEngineeringChangeCommandHandler(
    IEngineeringChangeRepository repository,
    IEngineeringBomRepository engineeringBomRepository,
    IManufacturingBomRepository manufacturingBomRepository,
    IRoutingRepository routingRepository,
    IProductionVersionRepository productionVersionRepository,
    ProductEngineeringCodingService? codingService = null)
    : ICommandHandler<ReleaseEngineeringChangeCommand, EntityCommandResult>
{
    private readonly ProductEngineeringCodingService _codingService = codingService ?? new ProductEngineeringCodingService();

    public async Task<EntityCommandResult> Handle(ReleaseEngineeringChangeCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "engineering-change",
            request.ChangeNumber,
            request.IdempotencyKey,
            ProductEngineeringCodingService.Fingerprint(request.Reason, request.ApprovalReferenceId, request.EffectiveDate, request.AffectedVersions.Select(x => $"{x.VersionKind}:{x.VersionId}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Code);
        }

        var affectedVersions = new List<Action<string>>();
        var change = EngineeringChange.Open(request.OrganizationId, request.EnvironmentId, allocation.Code, request.Reason)
            .Approve(request.ApprovalReferenceId);
        foreach (var affectedVersion in request.AffectedVersions)
        {
            affectedVersions.Add(await ResolveAffectedVersionAsync(request, affectedVersion, cancellationToken));
            change.Affect(affectedVersion.VersionKind, affectedVersion.VersionId);
        }

        change.Release(request.EffectiveDate);
        foreach (var archive in affectedVersions)
        {
            archive(change.ChangeNumber);
        }

        await repository.AddAsync(change, cancellationToken);
        return new EntityCommandResult(change.ChangeNumber);
    }

    private async Task<Action<string>> ResolveAffectedVersionAsync(
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
                cancellationToken), affectedVersion.VersionId),
            "manufacturing-bom" => ArchiveManufacturingBom(await manufacturingBomRepository.GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId),
            "routing" => ArchiveRouting(await routingRepository.GetByVersionIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId),
            "production-version" => ArchiveProductionVersion(await productionVersionRepository.GetByIdAsync(
                request.OrganizationId,
                request.EnvironmentId,
                affectedVersion.VersionId,
                cancellationToken), affectedVersion.VersionId),
            _ => throw new KnownException($"Affected version kind '{affectedVersion.VersionKind}' is not supported.")
        };
    }

    private static Action<string> ArchiveEngineeringBom(EngineeringBom? bom, string versionId)
    {
        return bom is null
            ? throw new KnownException($"Engineering BOM version '{versionId}' was not found.")
            : reason => bom.Archive(reason);
    }

    private static Action<string> ArchiveManufacturingBom(ManufacturingBom? bom, string versionId)
    {
        return bom is null
            ? throw new KnownException($"Manufacturing BOM version '{versionId}' was not found.")
            : reason => bom.Archive(reason);
    }

    private static Action<string> ArchiveRouting(Routing? routing, string versionId)
    {
        return routing is null
            ? throw new KnownException($"Routing version '{versionId}' was not found.")
            : reason => routing.Archive(reason);
    }

    private static Action<string> ArchiveProductionVersion(ProductionVersion? version, string versionId)
    {
        return version is null
            ? throw new KnownException($"Production version '{versionId}' was not found.")
            : reason => version.Archive(reason);
    }
}
