using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.Behaviors;

public sealed class ReturnLineSideMaterialConcurrencyRetryBehavior<TRequest, TResponse>(
    ApplicationDbContext dbContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IBaseCommand
{
    private const int MaxAttempts = 3;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await next(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
                when (request is ReturnLineSideMaterialCommand &&
                    IsLineSideReturnRevisionConflict(exception) &&
                    attempt < MaxAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException exception)
                when (request is ReturnLineSideMaterialCommand &&
                    IsLineSideReturnRevisionConflict(exception))
            {
                dbContext.ChangeTracker.Clear();
                throw new MesLifecycleConflictException(
                    "return-line-side-material",
                    "concurrent-update");
            }
        }
    }

    private static bool IsLineSideReturnRevisionConflict(DbUpdateConcurrencyException exception) =>
        exception.Entries.Count > 0 && exception.Entries.All(entry =>
            entry.Entity is MaterialIssueRequest &&
            entry.Metadata.FindProperty(nameof(MaterialIssueRequest.LineSideReturnConcurrencyToken))?.IsConcurrencyToken == true);
}
