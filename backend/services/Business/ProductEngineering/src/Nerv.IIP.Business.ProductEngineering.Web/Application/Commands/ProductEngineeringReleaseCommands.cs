using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.EngineeringDocumentAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ManufacturingBomAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.ProductionVersionAggregate;
using Nerv.IIP.Business.ProductEngineering.Domain.AggregatesModel.RoutingAggregate;
using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Commands;

public sealed record RegisterEngineeringDocumentCommand(
    string OrganizationId,
    string EnvironmentId,
    string DocumentNumber,
    string Revision,
    string FileId,
    string FileName,
    string ContentType,
    string DocumentType) : ICommand<EntityCommandResult>;

public sealed record EntityCommandResult(string Id);

public sealed class RegisterEngineeringDocumentCommandValidator : AbstractValidator<RegisterEngineeringDocumentCommand>
{
    public RegisterEngineeringDocumentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DocumentNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(120);
        RuleFor(x => x.DocumentType).NotEmpty().MaximumLength(100);
    }
}

public sealed class RegisterEngineeringDocumentCommandHandler(IEngineeringDocumentRepository repository)
    : ICommandHandler<RegisterEngineeringDocumentCommand, EntityCommandResult>
{
    public async Task<EntityCommandResult> Handle(RegisterEngineeringDocumentCommand request, CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(request.OrganizationId, request.EnvironmentId, request.DocumentNumber, request.Revision, cancellationToken))
        {
            throw new KnownException($"Engineering document '{request.DocumentNumber}' revision '{request.Revision}' already exists.");
        }

        var document = EngineeringDocument.Register(
            request.OrganizationId,
            request.EnvironmentId,
            request.DocumentNumber,
            request.Revision,
            request.FileId,
            request.FileName,
            request.ContentType,
            request.DocumentType);
        await repository.AddAsync(document, cancellationToken);
        return new EntityCommandResult(document.DocumentNumber);
    }
}

public sealed record ReleaseEngineeringBomCommand(
    string OrganizationId,
    string EnvironmentId,
    string BomCode,
    string Revision,
    string ParentItemCode,
    DateOnly EffectiveDate,
    IReadOnlyCollection<BomLineCommand> Lines) : ICommand<EntityCommandResult>;

public sealed record BomLineCommand(string ComponentCode, decimal Quantity, string UnitOfMeasureCode);

public sealed class ReleaseEngineeringBomCommandValidator : AbstractValidator<ReleaseEngineeringBomCommand>
{
    public ReleaseEngineeringBomCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BomCode).NotEmpty().MaximumLength(100);
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

public sealed class ReleaseEngineeringBomCommandHandler(IEngineeringBomRepository repository)
    : ICommandHandler<ReleaseEngineeringBomCommand, EntityCommandResult>
{
    public async Task<EntityCommandResult> Handle(ReleaseEngineeringBomCommand request, CancellationToken cancellationToken)
    {
        if (await repository.GetByBusinessKeyAsync(request.OrganizationId, request.EnvironmentId, request.BomCode, request.Revision, cancellationToken) is not null)
        {
            throw new KnownException($"Engineering BOM '{request.BomCode}' revision '{request.Revision}' already exists.");
        }

        var bom = EngineeringBom.CreateDraft(request.OrganizationId, request.EnvironmentId, request.BomCode, request.Revision, request.ParentItemCode);
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
    string BomCode,
    string Revision,
    string SkuCode,
    string EngineeringBomCode,
    string EngineeringBomRevision,
    DateOnly EffectiveDate,
    IReadOnlyCollection<ManufacturingBomMaterialLineCommand> MaterialLines,
    IReadOnlyCollection<RecipeLineCommand> RecipeLines) : ICommand<EntityCommandResult>;

public sealed record ManufacturingBomMaterialLineCommand(string SkuCode, decimal Quantity, string UnitOfMeasureCode, decimal ScrapRate);

public sealed record RecipeLineCommand(string ParameterCode, string TargetValue, string UnitOfMeasureCode);

public sealed class ReleaseManufacturingBomCommandValidator : AbstractValidator<ReleaseManufacturingBomCommand>
{
    public ReleaseManufacturingBomCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BomCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EngineeringBomCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EngineeringBomRevision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.MaterialLines).NotEmpty();
    }
}

public sealed class ReleaseManufacturingBomCommandHandler(
    IEngineeringBomRepository engineeringBomRepository,
    IManufacturingBomRepository manufacturingBomRepository)
    : ICommandHandler<ReleaseManufacturingBomCommand, EntityCommandResult>
{
    public async Task<EntityCommandResult> Handle(ReleaseManufacturingBomCommand request, CancellationToken cancellationToken)
    {
        var ebom = await engineeringBomRepository.GetByBusinessKeyAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.EngineeringBomCode,
            request.EngineeringBomRevision,
            cancellationToken)
            ?? throw new KnownException($"Released engineering BOM '{request.EngineeringBomCode}' revision '{request.EngineeringBomRevision}' was not found.");

        var bom = ManufacturingBom.CreateDraft(request.OrganizationId, request.EnvironmentId, request.BomCode, request.Revision, request.SkuCode);
        foreach (var line in request.MaterialLines)
        {
            bom.AddMaterialLine(line.SkuCode, line.Quantity, line.UnitOfMeasureCode, line.ScrapRate);
        }

        foreach (var line in request.RecipeLines)
        {
            bom.AddRecipeLine(line.ParameterCode, line.TargetValue, line.UnitOfMeasureCode);
        }

        bom.ReleaseFromEngineeringBom(ebom.BomCode, ebom.Status, request.EffectiveDate);
        await manufacturingBomRepository.AddAsync(bom, cancellationToken);
        return new EntityCommandResult(bom.BomCode);
    }
}

public sealed record ReleaseRoutingCommand(
    string OrganizationId,
    string EnvironmentId,
    string RoutingCode,
    string Revision,
    string SkuCode,
    DateOnly EffectiveDate,
    IReadOnlyCollection<RoutingOperationCommand> Operations) : ICommand<EntityCommandResult>;

public sealed record RoutingOperationCommand(int Sequence, string WorkCenterCode, string OperationName, int StandardMinutes);

public sealed class ReleaseRoutingCommandValidator : AbstractValidator<ReleaseRoutingCommand>
{
    public ReleaseRoutingCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.RoutingCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Revision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Operations).NotEmpty();
    }
}

public sealed class ReleaseRoutingCommandHandler(IRoutingRepository repository)
    : ICommandHandler<ReleaseRoutingCommand, EntityCommandResult>
{
    public async Task<EntityCommandResult> Handle(ReleaseRoutingCommand request, CancellationToken cancellationToken)
    {
        var routing = Routing.CreateDraft(request.OrganizationId, request.EnvironmentId, request.RoutingCode, request.Revision, request.SkuCode);
        foreach (var operation in request.Operations)
        {
            routing.AddOperation(operation.Sequence, operation.WorkCenterCode, operation.OperationName, operation.StandardMinutes);
        }

        routing.Release(request.EffectiveDate);
        await repository.AddAsync(routing, cancellationToken);
        return new EntityCommandResult(routing.RoutingCode);
    }
}

public sealed record ReleaseEngineeringChangeCommand(
    string OrganizationId,
    string EnvironmentId,
    string ChangeNumber,
    string Reason,
    string ApprovalReferenceId,
    DateOnly EffectiveDate,
    IReadOnlyCollection<AffectedVersionCommand> AffectedVersions) : ICommand<EntityCommandResult>;

public sealed record AffectedVersionCommand(string VersionKind, string VersionId);

public sealed class ReleaseEngineeringChangeCommandValidator : AbstractValidator<ReleaseEngineeringChangeCommand>
{
    public ReleaseEngineeringChangeCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.ApprovalReferenceId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.AffectedVersions).NotEmpty();
    }
}

public sealed class ReleaseEngineeringChangeCommandHandler(IEngineeringChangeRepository repository)
    : ICommandHandler<ReleaseEngineeringChangeCommand, EntityCommandResult>
{
    public async Task<EntityCommandResult> Handle(ReleaseEngineeringChangeCommand request, CancellationToken cancellationToken)
    {
        var change = EngineeringChange.Open(request.OrganizationId, request.EnvironmentId, request.ChangeNumber, request.Reason)
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
