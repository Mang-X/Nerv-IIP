using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Contracts.IntegrationEvents;

namespace Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

// ADR0011: retain the existing Quality event-id inbox and commit it with consumption/outbox.
internal static class QualityIntegrationEventInboxConsumption
{
    public static async Task ExecuteAsync(ApplicationDbContext dbContext, string consumerName,
        IIntegrationEventEnvelope integrationEvent, Func<Task> consume, CancellationToken token)
    {
        var unitOfWork = (ITransactionUnitOfWork)dbContext;
        if (!dbContext.Database.IsRelational())
        {
            await ConsumeAsync();
            await dbContext.SaveChangesAsync(token);
            return;
        }

        if (unitOfWork.CurrentTransaction is not null)
        {
            await ConsumeAsync();
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(token);
            return;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(token);
        unitOfWork.CurrentTransaction = transaction;
        try
        {
            await ConsumeAsync();
            await ((IUnitOfWork)unitOfWork).SaveEntitiesAsync(token);
            await unitOfWork.CommitAsync(token);
        }
        catch
        {
            await unitOfWork.RollbackAsync(token);
            throw;
        }
        finally
        {
            unitOfWork.CurrentTransaction = null;
        }

        async Task ConsumeAsync()
        {
            if (await QualityProcessedIntegrationEventInbox.TryRecordAsync(dbContext, consumerName, integrationEvent, token))
                await consume();
        }
    }
}
