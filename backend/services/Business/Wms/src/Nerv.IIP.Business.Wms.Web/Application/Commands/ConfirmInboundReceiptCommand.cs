using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Errors;

namespace Nerv.IIP.Business.Wms.Web.Application.Commands;

public sealed record ConfirmInboundReceiptCommand(
    InboundOrderId InboundOrderId,
    string OrganizationId,
    string EnvironmentId,
    string ActorPrincipalId,
    IReadOnlyCollection<string> AuthorizedSiteCodes,
    string ScopeKind,
    string ScopeId,
    long ExpectedVersion,
    string IdempotencyKey,
    IReadOnlyCollection<InboundReceiptQuantity> Lines) : ICommand<ConfirmInboundReceiptResult>;

/// <summary>入库单及本次确认写入的版本。</summary>
public sealed record ConfirmInboundReceiptResult(InboundOrderId InboundOrderId, long Version);

public sealed class ConfirmInboundReceiptCommandValidator : AbstractValidator<ConfirmInboundReceiptCommand>
{
    public ConfirmInboundReceiptCommandValidator()
    {
        RuleFor(x => x.InboundOrderId).NotEmpty();
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ActorPrincipalId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorizedSiteCodes).NotEmpty();
        RuleForEach(x => x.AuthorizedSiteCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ScopeKind).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ScopeId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).NotNull().ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.ReceivedQuantity).GreaterThan(0);
        });
    }
}

public sealed class ConfirmInboundReceiptCommandLock : ICommandLock<ConfirmInboundReceiptCommand>
{
    public Task<CommandLockSettings> GetLockKeysAsync(
        ConfirmInboundReceiptCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CompleteInboundOrderCommandLock.ForInboundOrder(command.InboundOrderId));
    }
}

public sealed class ConfirmInboundReceiptCommandHandler(
    ApplicationDbContext dbContext,
    WarehouseAssignedResourceExecutionAuthorizer authorizer)
    : ICommandHandler<ConfirmInboundReceiptCommand, ConfirmInboundReceiptResult>
{
    private const string Action = "confirm-inbound-receipt";

    public async Task<ConfirmInboundReceiptResult> Handle(
        ConfirmInboundReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var inbound = await dbContext.InboundOrders.Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == request.InboundOrderId, cancellationToken)
            ?? throw new WmsLifecycleConflictException(Action, "not-found");

        await authorizer.AuthorizeAsync(new WarehouseAssignedResourceExecutionRequest(
            request.OrganizationId, request.EnvironmentId, request.ActorPrincipalId,
            request.AuthorizedSiteCodes, request.ScopeKind, request.ScopeId,
            inbound.OrganizationId, inbound.EnvironmentId, inbound.SiteCode,
            inbound.AssignedPoolCode, inbound.AssignedOperatorUserId), cancellationToken);

        var key = WmsText.IdempotencyKey(request.IdempotencyKey);
        var fingerprint = Fingerprint(request);
        if (inbound.ReceiptConfirmationKey == key)
        {
            if (inbound.ReceiptConfirmationFingerprint != fingerprint)
            {
                throw new WmsIdempotencyConflictException();
            }

            return new ConfirmInboundReceiptResult(inbound.Id, checked(request.ExpectedVersion + 1));
        }

        WarehouseAssignedResourceCompletionExecution.EnsureExpectedVersion(
            inbound.Version, request.ExpectedVersion, Action);
        if (inbound.Status != InboundOrderStatus.Open || inbound.ReceiptConfirmationKey is not null)
        {
            throw new WmsLifecycleConflictException(Action, inbound.Status.ToString());
        }

        try
        {
            inbound.ConfirmReceipt(request.ExpectedVersion, request.Lines, new(key, fingerprint));
        }
        catch (InvalidOperationException exception)
        {
            throw new WmsUnprocessableException(exception.Message);
        }

        return new ConfirmInboundReceiptResult(inbound.Id, inbound.Version);
    }

    private static string Fingerprint(ConfirmInboundReceiptCommand request)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorPrincipalId = request.ActorPrincipalId.Trim(),
            request.ExpectedVersion,
            Lines = request.Lines.Select(line => new
                {
                    LineNo = line.LineNo.Trim(),
                    ReceivedQuantity = line.ReceivedQuantity.ToString("G29", CultureInfo.InvariantCulture),
                })
                .OrderBy(line => line.LineNo, StringComparer.Ordinal),
        });
        return Convert.ToHexString(SHA256.HashData(payload));
    }
}
