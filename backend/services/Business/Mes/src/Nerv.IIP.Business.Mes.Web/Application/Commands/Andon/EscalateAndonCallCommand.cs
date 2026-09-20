using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.AndonCallAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.Andon;

public sealed record EscalateAndonCallCommand(AndonCallId CallId, DateTimeOffset NowUtc, TimeSpan UnclaimedTimeout, string RecipientId) : ICommand<bool>;

public sealed class EscalateAndonCallCommandHandler(ApplicationDbContext db) : ICommandHandler<EscalateAndonCallCommand, bool>
{
    public async Task<bool> Handle(EscalateAndonCallCommand command, CancellationToken ct)
    {
        var call = await db.AndonCalls.SingleAsync(x => x.Id == command.CallId, ct);
        return call.TryEscalate(command.NowUtc, command.UnclaimedTimeout, command.RecipientId);
    }
}
