using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

internal static class PrincipalWorkerMapping
{
    internal static IQueryable<Worker> Find(
        ApplicationDbContext dbContext, string organizationId, string environmentId, string userId) =>
        dbContext.Workers
            .AsNoTracking()
            .Where(worker => worker.OrganizationId == organizationId
                && worker.EnvironmentId == environmentId
                && worker.UserId == userId
                && !worker.Disabled)
            .Take(2);
}
