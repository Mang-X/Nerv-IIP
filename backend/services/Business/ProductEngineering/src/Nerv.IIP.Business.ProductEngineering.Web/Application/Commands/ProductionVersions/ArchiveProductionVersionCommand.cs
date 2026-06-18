using Nerv.IIP.Business.ProductEngineering.Infrastructure.Repositories;

namespace Nerv.IIP.Business.ProductEngineering.Web.Application.Commands.ProductionVersions;

public sealed record ArchiveProductionVersionCommand(
    string OrganizationId,
    string EnvironmentId,
    string ProductionVersionId,
    string Reason) : ICommand;

public sealed class ArchiveProductionVersionCommandValidator : AbstractValidator<ArchiveProductionVersionCommand>
{
    public ArchiveProductionVersionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ProductionVersionId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ArchiveProductionVersionCommandHandler(IProductionVersionRepository repository)
    : ICommandHandler<ArchiveProductionVersionCommand>
{
    public async Task Handle(ArchiveProductionVersionCommand request, CancellationToken cancellationToken)
    {
        var version = await repository.GetByIdAsync(request.OrganizationId, request.EnvironmentId, request.ProductionVersionId, cancellationToken)
            ?? throw new KnownException($"Production version '{request.ProductionVersionId}' was not found.");
        version.Archive(request.Reason);
    }
}
