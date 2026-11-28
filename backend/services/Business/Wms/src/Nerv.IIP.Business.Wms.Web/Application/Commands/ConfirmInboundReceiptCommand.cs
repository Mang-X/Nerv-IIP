using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Contracts.Wms;

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
    IReadOnlyCollection<InboundReceiptQuantity> Lines,
    decimal? ExchangeRate = null) : ICommand<ConfirmInboundReceiptResult>;

/// <summary>入库单及本次确认写入的版本。</summary>
public sealed record ConfirmInboundReceiptResult(InboundOrderId InboundOrderId, long Version)
{
    internal Guid? PendingErpBatchId { get; init; }
}

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
    public const string StableErrorCode = "receipt-pre-stage-version-conflict";

    public async Task<ConfirmInboundReceiptResult> Handle(
        ConfirmInboundReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var inbound = await LoadAuthorizedAsync(dbContext, authorizer, request, cancellationToken);

        var key = WmsText.IdempotencyKey(request.IdempotencyKey);
        var fingerprint = Fingerprint(request);
        if (inbound.ReceiptRequestKey == key)
        {
            if (inbound.ReceiptRequestFingerprint != fingerprint || inbound.ReceiptExchangeRate != request.ExchangeRate)
            {
                throw new WmsIdempotencyConflictException();
            }

            if (inbound.Status != InboundOrderStatus.Open && inbound.ErpReceiptAcceptedVersion is null)
            {
                throw new WmsLifecycleConflictException(Action, inbound.Status.ToString());
            }

            return ErpReceiptResult(inbound);
        }

        if (inbound.ReceiptConfirmationKey == key)
        {
            if (inbound.ReceiptConfirmationFingerprint != fingerprint
                || (inbound.ReceiptBatchId is not null && inbound.ReceiptExchangeRate != request.ExchangeRate))
            {
                throw new WmsIdempotencyConflictException();
            }

            return new ConfirmInboundReceiptResult(inbound.Id,
                inbound.ErpReceiptAcceptedVersion ?? checked(request.ExpectedVersion + 1));
        }

        if (inbound.Status != InboundOrderStatus.Open || inbound.ReceiptConfirmationKey is not null
            || inbound.ReceiptBatchId is not null || inbound.ReceiptRequestKey is not null)
        {
            throw new WmsLifecycleConflictException(Action, inbound.Status.ToString());
        }

        // A superseded version cannot stage on retry. An ahead-of-server version does not prove that.
        if (request.ExpectedVersion < inbound.Version && inbound.RequiresErpReceiptAcceptance
            && inbound.Lines.All(line => line.ExpectedQuantity.HasValue && line.ReceivedQuantity == 0m))
        {
            throw new WmsLifecycleConflictException(Action, inbound.Status.ToString(), StableErrorCode);
        }

        WarehouseAssignedResourceCompletionExecution.EnsureExpectedVersion(
            inbound.Version, request.ExpectedVersion, Action);

        try
        {
            if (inbound.RequiresErpReceiptAcceptance)
            {
                ValidateExchangeRate(request.ExchangeRate);
                inbound.StageErpReceipt(request.ExpectedVersion, request.Lines, new(key, fingerprint), request.ExchangeRate!.Value);
                return ErpReceiptResult(inbound);
            }

            inbound.ConfirmReceipt(request.ExpectedVersion, request.Lines, new(key, fingerprint));
            return new ConfirmInboundReceiptResult(inbound.Id, inbound.Version);
        }
        catch (InvalidOperationException exception)
        {
            throw new WmsUnprocessableException(exception.Message);
        }
    }

    private static ConfirmInboundReceiptResult ErpReceiptResult(InboundOrder inbound) =>
        new(inbound.Id, inbound.ErpReceiptAcceptedVersion ?? inbound.Version)
        {
            PendingErpBatchId = inbound.ErpReceiptAcceptedVersion is null ? inbound.ReceiptBatchId : null,
        };

    private static void ValidateExchangeRate(decimal? exchangeRate)
    {
        if (exchangeRate is not > 0 or >= 10_000_000_000m || decimal.Round(exchangeRate.Value, 8) != exchangeRate)
        {
            throw new WmsUnprocessableException("ERP receipt acceptance requires an explicit exchange rate with precision (18,8).");
        }
    }

    internal static async Task<InboundOrder> LoadAuthorizedAsync(ApplicationDbContext dbContext,
        WarehouseAssignedResourceExecutionAuthorizer authorizer, ConfirmInboundReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId.Trim();
        var environmentId = request.EnvironmentId.Trim();
        var purchaseOrderNo = await dbContext.InboundOrders
            .Where(order => order.Id == request.InboundOrderId && order.OrganizationId == organizationId
                && order.EnvironmentId == environmentId && order.SourceDocumentType == WmsSourceDocumentTypes.PurchaseOrder
                && order.Lines.Any(line => line.ExpectedQuantity != null))
            .Select(order => order.SourceDocumentId)
            .SingleOrDefaultAsync(cancellationToken);
        if (purchaseOrderNo is not null)
        {
            await PurchaseOrderInboundLock.AcquireAsync(dbContext, organizationId, environmentId,
                purchaseOrderNo, cancellationToken);
        }

        var inbound = await dbContext.InboundOrders.Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == request.InboundOrderId, cancellationToken)
            ?? throw new WmsLifecycleConflictException(Action, "not-found");

        await authorizer.AuthorizeAsync(new WarehouseAssignedResourceExecutionRequest(
            request.OrganizationId, request.EnvironmentId, request.ActorPrincipalId,
            request.AuthorizedSiteCodes, request.ScopeKind, request.ScopeId,
            inbound.OrganizationId, inbound.EnvironmentId, inbound.SiteCode,
            inbound.AssignedPoolCode, inbound.AssignedOperatorUserId), cancellationToken);
        return inbound;
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
