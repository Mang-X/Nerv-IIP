using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringItemAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
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
    string? IdempotencyKey = null) : ICommand<EntityCommandResult>;

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
    }
}

public sealed class RegisterEngineeringDocumentCommandHandler(IEngineeringDocumentRepository repository, ProductEngineeringNumberingService? numberingService = null)
    : ICommandHandler<RegisterEngineeringDocumentCommand, EntityCommandResult>
{
    private readonly ProductEngineeringNumberingService _numberingService = numberingService ?? new ProductEngineeringNumberingService();

    public async Task<EntityCommandResult> Handle(RegisterEngineeringDocumentCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "engineering-document",
            "EDOC",
            request.DocumentNumber,
            request.IdempotencyKey,
            ProductEngineeringNumberingService.Fingerprint(request.Revision, request.FileId, request.FileName, request.ContentType, request.DocumentType),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Number);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Number, request.Revision, cancellationToken))
        {
            throw new KnownException($"Engineering document '{allocation.Number}' revision '{request.Revision}' already exists.");
        }

        var document = EngineeringDocument.Register(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
            request.Revision,
            request.FileId,
            request.FileName,
            request.ContentType,
            request.DocumentType);
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

public sealed class CreateEngineeringItemRevisionCommandHandler(IEngineeringItemRepository repository, ProductEngineeringNumberingService? numberingService = null)
    : ICommandHandler<CreateEngineeringItemRevisionCommand, EntityCommandResult>
{
    private readonly ProductEngineeringNumberingService _numberingService = numberingService ?? new ProductEngineeringNumberingService();

    public async Task<EntityCommandResult> Handle(CreateEngineeringItemRevisionCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "engineering-item",
            "ITEM",
            request.ItemCode,
            request.IdempotencyKey,
            ProductEngineeringNumberingService.Fingerprint(request.Revision, request.Name, request.Release),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Number);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Number, request.Revision, cancellationToken))
        {
            throw new KnownException($"Engineering item '{allocation.Number}' revision '{request.Revision}' already exists.");
        }

        var item = EngineeringItem.CreateRevision(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
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

public sealed record BomLineCommand(string ComponentCode, decimal Quantity, string UnitOfMeasureCode);

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

public sealed class ReleaseEngineeringBomCommandHandler(IEngineeringBomRepository repository, ProductEngineeringNumberingService? numberingService = null)
    : ICommandHandler<ReleaseEngineeringBomCommand, EntityCommandResult>
{
    private readonly ProductEngineeringNumberingService _numberingService = numberingService ?? new ProductEngineeringNumberingService();

    public async Task<EntityCommandResult> Handle(ReleaseEngineeringBomCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "engineering-bom",
            "EBOM",
            request.BomCode,
            request.IdempotencyKey,
            ProductEngineeringNumberingService.Fingerprint(request.Revision, request.ParentItemCode, request.EffectiveDate, request.Lines.Select(x => $"{x.ComponentCode}:{x.Quantity}:{x.UnitOfMeasureCode}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Number);
        }

        if (await repository.GetByBusinessKeyAsync(request.OrganizationId, request.EnvironmentId, allocation.Number, request.Revision, cancellationToken) is not null)
        {
            throw new KnownException($"Engineering BOM '{allocation.Number}' revision '{request.Revision}' already exists.");
        }

        var bom = EngineeringBom.CreateDraft(request.OrganizationId, request.EnvironmentId, allocation.Number, request.Revision, request.ParentItemCode);
        foreach (var line in request.Lines)
        {
            bom.AddLine(line.ComponentCode, line.Quantity, line.UnitOfMeasureCode);
        }

        bom.Release(request.EffectiveDate);
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

public sealed record ManufacturingBomMaterialLineCommand(string SkuCode, decimal Quantity, string UnitOfMeasureCode, decimal ScrapRate);

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
    ProductEngineeringNumberingService? numberingService = null)
    : ICommandHandler<ReleaseManufacturingBomCommand, EntityCommandResult>
{
    private readonly ProductEngineeringNumberingService _numberingService = numberingService ?? new ProductEngineeringNumberingService();

    public async Task<EntityCommandResult> Handle(ReleaseManufacturingBomCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "manufacturing-bom",
            "MBOM",
            request.BomCode,
            request.IdempotencyKey,
            ProductEngineeringNumberingService.Fingerprint(request.Revision, request.SkuCode, request.EngineeringBomCode, request.EngineeringBomRevision, request.EffectiveDate),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Number);
        }

        if (await manufacturingBomRepository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Number, request.Revision, cancellationToken))
        {
            throw new KnownException($"Manufacturing BOM '{allocation.Number}' revision '{request.Revision}' already exists.");
        }

        var ebom = await engineeringBomRepository.GetByBusinessKeyAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.EngineeringBomCode,
            request.EngineeringBomRevision,
            cancellationToken)
            ?? throw new KnownException($"Released engineering BOM '{request.EngineeringBomCode}' revision '{request.EngineeringBomRevision}' was not found.");

        var bom = ManufacturingBom.CreateDraft(request.OrganizationId, request.EnvironmentId, allocation.Number, request.Revision, request.SkuCode);
        foreach (var line in request.MaterialLines)
        {
            bom.AddMaterialLine(line.SkuCode, line.Quantity, line.UnitOfMeasureCode, line.ScrapRate);
        }

        foreach (var line in request.RecipeLines)
        {
            bom.AddRecipeLine(line.ParameterCode, line.TargetValue, line.UnitOfMeasureCode);
        }

        bom.ReleaseFromEngineeringBom($"{ebom.BomCode}:{ebom.Revision}", ebom.Status, request.EffectiveDate);
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

public sealed record RoutingOperationCommand(int Sequence, string WorkCenterCode, string OperationCode, string OperationName, int StandardMinutes);

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
            operation.RuleFor(x => x.WorkCenterCode).NotEmpty().MaximumLength(100);
            operation.RuleFor(x => x.OperationCode).NotEmpty().MaximumLength(100);
            operation.RuleFor(x => x.OperationName).NotEmpty().MaximumLength(200);
            operation.RuleFor(x => x.StandardMinutes).GreaterThan(0);
        });
    }
}

public sealed class ReleaseRoutingCommandHandler(IRoutingRepository repository, ProductEngineeringNumberingService? numberingService = null)
    : ICommandHandler<ReleaseRoutingCommand, EntityCommandResult>
{
    private readonly ProductEngineeringNumberingService _numberingService = numberingService ?? new ProductEngineeringNumberingService();

    public async Task<EntityCommandResult> Handle(ReleaseRoutingCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "routing",
            "RTG",
            request.RoutingCode,
            request.IdempotencyKey,
            ProductEngineeringNumberingService.Fingerprint(request.Revision, request.SkuCode, request.EffectiveDate, request.Operations.Select(x => $"{x.Sequence}:{x.WorkCenterCode}:{x.OperationCode}:{x.OperationName}:{x.StandardMinutes}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Number);
        }

        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, allocation.Number, request.Revision, cancellationToken))
        {
            throw new KnownException($"Routing '{allocation.Number}' revision '{request.Revision}' already exists.");
        }

        var routing = Routing.CreateDraft(request.OrganizationId, request.EnvironmentId, allocation.Number, request.Revision, request.SkuCode);
        foreach (var operation in request.Operations)
        {
            routing.AddOperation(operation.Sequence, operation.WorkCenterCode, operation.OperationCode, operation.OperationName, operation.StandardMinutes);
        }

        routing.Release(request.EffectiveDate);
        await repository.AddAsync(routing, cancellationToken);
        return new EntityCommandResult(routing.RoutingCode);
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

public sealed class ReleaseEngineeringChangeCommandHandler(IEngineeringChangeRepository repository, ProductEngineeringNumberingService? numberingService = null)
    : ICommandHandler<ReleaseEngineeringChangeCommand, EntityCommandResult>
{
    private readonly ProductEngineeringNumberingService _numberingService = numberingService ?? new ProductEngineeringNumberingService();

    public async Task<EntityCommandResult> Handle(ReleaseEngineeringChangeCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _numberingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "engineering-change",
            "ECO",
            request.ChangeNumber,
            request.IdempotencyKey,
            ProductEngineeringNumberingService.Fingerprint(request.Reason, request.ApprovalReferenceId, request.EffectiveDate, request.AffectedVersions.Select(x => $"{x.VersionKind}:{x.VersionId}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return new EntityCommandResult(allocation.Number);
        }

        var change = EngineeringChange.Open(request.OrganizationId, request.EnvironmentId, allocation.Number, request.Reason)
            .Approve(request.ApprovalReferenceId);
        foreach (var affectedVersion in request.AffectedVersions)
        {
            change.Affect(affectedVersion.VersionKind, affectedVersion.VersionId);
        }

        change.Release(request.EffectiveDate);
        await repository.AddAsync(change, cancellationToken);
        return new EntityCommandResult(change.ChangeNumber);
    }
}
