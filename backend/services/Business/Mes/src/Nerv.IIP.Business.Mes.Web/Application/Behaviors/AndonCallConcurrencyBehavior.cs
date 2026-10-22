using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Andon;
using Nerv.IIP.Business.Mes.Web.Application.Errors;

namespace Nerv.IIP.Business.Mes.Web.Application.Behaviors;

public sealed class AndonCallConcurrencyBehavior<TRequest, TResponse>(ApplicationDbContext db)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IBaseCommand
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        try
        {
            return await next(ct);
        }
        catch (DbUpdateException exception) when (
            request is RaiseAndonCallCommand or ClaimAndonCallCommand or CloseAndonCallCommand &&
            AndonCallRepository.IsWriteConflict(exception))
        {
            // 冲突事务已由 UnitOfWork 回滚；重新读取已提交事实，返回原回执或业务冲突。
            db.ChangeTracker.Clear();
            try
            {
                return await next(ct);
            }
            catch (DbUpdateException repeated) when (AndonCallRepository.IsWriteConflict(repeated))
            {
                throw new MesLifecycleConflictException("andon-call", "concurrent-update");
            }
        }
    }
}
