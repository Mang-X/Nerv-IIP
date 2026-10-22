using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Andon;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Andon;

public sealed record RaiseAndonCallCommand(AndonCallScope Scope, string IdempotencyKey,
    AndonCallCategory Category, string WorkOrderId, string OperationTaskId, string WorkCenterId, string Actor)
    : ICommand<AndonCallResponse>;
public sealed record ClaimAndonCallCommand(AndonCallScope Scope, AndonCallId Id, string IdempotencyKey, string Actor)
    : ICommand<AndonCallResponse>;
public sealed record CloseAndonCallCommand(AndonCallScope Scope, AndonCallId Id, string IdempotencyKey, string Actor)
    : ICommand<AndonCallResponse>;

public sealed class RaiseAndonCallCommandHandler(ApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<RaiseAndonCallCommand, AndonCallResponse>
{
    public async Task<AndonCallResponse> Handle(RaiseAndonCallCommand request, CancellationToken ct)
    {
        var existing = await db.Set<AndonCall>().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.Scope.OrganizationId && x.EnvironmentId == request.Scope.EnvironmentId &&
            x.RaiseIntentKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (!await AndonCallAccess.Query(db, request.Scope).AnyAsync(x => x.Id == existing.Id, ct))
                throw new KnownException("未找到授权范围内的异常呼叫。");
            if (existing.Category != request.Category || existing.WorkOrderId != request.WorkOrderId ||
                existing.OperationTaskIdValue != request.OperationTaskId || existing.WorkCenterId != request.WorkCenterId ||
                existing.CallerId != request.Actor) throw new MesIdempotencyConflictException();
            return AndonCallResponse.From(existing);
        }
        await AndonCallAccess.EnsureSourceAsync(db, request.Scope, request.WorkOrderId, request.OperationTaskId, request.WorkCenterId, ct);
        var call = AndonCall.Raise(request.Scope.OrganizationId, request.Scope.EnvironmentId, request.IdempotencyKey,
            request.Category, request.WorkOrderId, request.OperationTaskId, request.WorkCenterId, request.Actor, clock.GetUtcNow());
        db.Set<AndonCall>().Add(call);
        return AndonCallResponse.From(call);
    }
}

public sealed class ClaimAndonCallCommandHandler(ApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<ClaimAndonCallCommand, AndonCallResponse>
{
    public async Task<AndonCallResponse> Handle(ClaimAndonCallCommand request, CancellationToken ct)
    {
        var call = await AndonCallAccess.Query(db, request.Scope).AsTracking().SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KnownException("未找到授权范围内的异常呼叫。");
        if (call.ClaimIntentKey == request.IdempotencyKey && call.ResponderId != request.Actor)
            throw new MesIdempotencyConflictException();
        if (call.Status != AndonCallStatus.Open &&
            (call.ClaimIntentKey != request.IdempotencyKey || call.ResponderId != request.Actor))
            throw new MesLifecycleConflictException("claim-andon-call", call.Status.ToString());
        call.Claim(request.Actor, request.IdempotencyKey, clock.GetUtcNow());
        return AndonCallResponse.From(call);
    }
}

public sealed class CloseAndonCallCommandHandler(ApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<CloseAndonCallCommand, AndonCallResponse>
{
    public async Task<AndonCallResponse> Handle(CloseAndonCallCommand request, CancellationToken ct)
    {
        var call = await AndonCallAccess.Query(db, request.Scope).AsTracking().SingleOrDefaultAsync(x => x.Id == request.Id, ct)
            ?? throw new KnownException("未找到授权范围内的异常呼叫。");
        call.Close(request.Actor, request.IdempotencyKey, clock.GetUtcNow());
        return AndonCallResponse.From(call);
    }
}
