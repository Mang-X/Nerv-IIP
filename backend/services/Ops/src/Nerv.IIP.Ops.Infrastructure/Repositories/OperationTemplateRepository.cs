using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Ops.Domain.AggregatesModel.OperationTemplateAggregate;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Ops.Infrastructure.Repositories;

public interface IOperationTemplateRepository : IRepository<OperationTemplate, OperationTemplateId>
{
    Task<OperationTemplate?> GetByOperationCodeAsync(string operationCode, CancellationToken cancellationToken = default);
    Task<OperationTemplateId> NextTemplateIdAsync(CancellationToken cancellationToken = default);
}

public sealed class OperationTemplateRepository(ApplicationDbContext context)
    : RepositoryBase<OperationTemplate, OperationTemplateId, ApplicationDbContext>(context), IOperationTemplateRepository
{
    public async Task<OperationTemplate?> GetByOperationCodeAsync(string operationCode, CancellationToken cancellationToken = default)
    {
        return await DbContext.OperationTemplates
            .SingleOrDefaultAsync(x => x.OperationCode == operationCode, cancellationToken);
    }

    public Task<OperationTemplateId> NextTemplateIdAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OperationTemplateId($"opt-{Guid.NewGuid():N}"));
    }
}
