using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Contracts.Wms;
using static Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters.ErpIntegrationEventConverterHelpers;

namespace Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;

public sealed class DeliveryOrderReleasedIntegrationEventConverter
    : IIntegrationEventConverter<DeliveryOrderReleasedDomainEvent, ErpIntegrationEvent<DeliveryOrderReleasedPayload>>
{
    public ErpIntegrationEvent<DeliveryOrderReleasedPayload> Convert(DeliveryOrderReleasedDomainEvent domainEvent)
    {
        var delivery = domainEvent.DeliveryOrder;
        return Envelope(
            ErpIntegrationEventTypes.DeliveryOrderReleased,
            delivery.OrganizationId,
            delivery.EnvironmentId,
            EventIds.Idempotency("delivery-order-released", delivery.OrganizationId, delivery.EnvironmentId, delivery.DeliveryOrderNo),
            new DeliveryOrderReleasedPayload(PublicId(delivery.Id), delivery.DeliveryOrderNo, delivery.SalesOrderNo, delivery.CustomerCode));
    }
}

public sealed class DeliveryOrderOutboundOrderRequestedIntegrationEventConverter
    : IIntegrationEventConverter<DeliveryOrderReleasedDomainEvent, WmsOutboundOrderRequestedIntegrationEvent>
{
    public WmsOutboundOrderRequestedIntegrationEvent Convert(DeliveryOrderReleasedDomainEvent domainEvent)
    {
        var delivery = domainEvent.DeliveryOrder;
        return new WmsOutboundOrderRequestedIntegrationEvent(
            EventIds.New(),
            WmsIntegrationEventTypes.OutboundOrderRequested,
            WmsIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            WmsIntegrationEventSources.BusinessErp,
            "system:erp",
            "system:erp",
            delivery.OrganizationId,
            delivery.EnvironmentId,
            "system:erp",
            EventIds.Idempotency("delivery-order-wms-outbound-requested", delivery.OrganizationId, delivery.EnvironmentId, delivery.DeliveryOrderNo),
            new WmsOutboundOrderRequestedPayload(
                delivery.DeliveryOrderNo,
                delivery.SalesOrderNo,
                delivery.CustomerCode,
                null,
                delivery.Lines
                    .OrderBy(x => x.SalesOrderLineNo, StringComparer.Ordinal)
                    .Select(x => new WmsOutboundOrderRequestedLine(
                        x.SalesOrderLineNo,
                        x.SkuCode,
                        x.UomCode,
                        x.LocationCode,
                        x.LotNo,
                        x.Quantity))
                    .ToArray()));
    }
}

public sealed class AccountPayableCreatedIntegrationEventConverter
    : IIntegrationEventConverter<AccountPayableCreatedDomainEvent, ErpIntegrationEvent<AccountPayableCreatedPayload>>
{
    public ErpIntegrationEvent<AccountPayableCreatedPayload> Convert(AccountPayableCreatedDomainEvent domainEvent)
    {
        var payable = domainEvent.AccountPayable;
        return Envelope(
            ErpIntegrationEventTypes.AccountPayableCreated,
            payable.OrganizationId,
            payable.EnvironmentId,
            EventIds.Idempotency("account-payable-created", payable.OrganizationId, payable.EnvironmentId, payable.PayableNo),
            new AccountPayableCreatedPayload(PublicId(payable.Id), payable.PayableNo, payable.SourceDocumentNo, payable.SupplierCode, payable.Amount, payable.CurrencyCode));
    }
}

public sealed class AccountReceivableCreatedIntegrationEventConverter
    : IIntegrationEventConverter<AccountReceivableCreatedDomainEvent, ErpIntegrationEvent<AccountReceivableCreatedPayload>>
{
    public ErpIntegrationEvent<AccountReceivableCreatedPayload> Convert(AccountReceivableCreatedDomainEvent domainEvent)
    {
        var receivable = domainEvent.AccountReceivable;
        return Envelope(
            ErpIntegrationEventTypes.AccountReceivableCreated,
            receivable.OrganizationId,
            receivable.EnvironmentId,
            EventIds.Idempotency("account-receivable-created", receivable.OrganizationId, receivable.EnvironmentId, receivable.ReceivableNo),
            new AccountReceivableCreatedPayload(PublicId(receivable.Id), receivable.ReceivableNo, receivable.SourceDocumentNo, receivable.CustomerCode, receivable.Amount, receivable.CurrencyCode));
    }
}

public sealed class CostCandidateCreatedIntegrationEventConverter
    : IIntegrationEventConverter<CostCandidateCreatedDomainEvent, ErpIntegrationEvent<CostCandidateCreatedPayload>>
{
    public ErpIntegrationEvent<CostCandidateCreatedPayload> Convert(CostCandidateCreatedDomainEvent domainEvent)
    {
        var candidate = domainEvent.CostCandidate;
        return Envelope(
            ErpIntegrationEventTypes.CostCandidateCreated,
            candidate.OrganizationId,
            candidate.EnvironmentId,
            EventIds.Idempotency("cost-candidate-created", candidate.OrganizationId, candidate.EnvironmentId, candidate.CandidateNo),
            new CostCandidateCreatedPayload(PublicId(candidate.Id), candidate.CandidateNo, candidate.SourceType, candidate.SourceDocumentNo, candidate.Amount, candidate.CurrencyCode));
    }
}

public sealed class JournalVoucherPostedIntegrationEventConverter
    : IIntegrationEventConverter<JournalVoucherPostedDomainEvent, ErpIntegrationEvent<JournalVoucherPostedPayload>>
{
    public ErpIntegrationEvent<JournalVoucherPostedPayload> Convert(JournalVoucherPostedDomainEvent domainEvent)
    {
        var voucher = domainEvent.JournalVoucher;
        return Envelope(
            ErpIntegrationEventTypes.JournalVoucherPosted,
            voucher.OrganizationId,
            voucher.EnvironmentId,
            EventIds.Idempotency("journal-voucher-posted", voucher.OrganizationId, voucher.EnvironmentId, voucher.VoucherNo),
            new JournalVoucherPostedPayload(PublicId(voucher.Id), voucher.VoucherNo, voucher.PostingDate));
    }
}

public sealed class SalesReturnAuthorizedIntegrationEventConverter
    : IIntegrationEventConverter<SalesReturnAuthorizedDomainEvent, SalesReturnAuthorizedIntegrationEvent>
{
    public SalesReturnAuthorizedIntegrationEvent Convert(SalesReturnAuthorizedDomainEvent domainEvent)
    {
        var rma = domainEvent.SalesReturnAuthorization;
        var idempotencyKey = EventIds.Idempotency("sales-return-authorized", rma.OrganizationId, rma.EnvironmentId, rma.RmaNo);
        return new SalesReturnAuthorizedIntegrationEvent(
            EventIds.New(),
            ErpIntegrationEventTypes.SalesReturnAuthorized,
            ErpIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            ErpIntegrationEventSources.BusinessErp,
            idempotencyKey,
            rma.RmaNo,
            rma.OrganizationId,
            rma.EnvironmentId,
            "system:erp",
            idempotencyKey,
            new SalesReturnAuthorizedPayload(
                rma.RmaNo,
                rma.SalesOrderNo,
                rma.CustomerCode,
                rma.SiteCode,
                rma.Lines
                    .OrderBy(x => x.SalesOrderLineNo, StringComparer.Ordinal)
                    .Select(x => new SalesReturnAuthorizedLinePayload(
                        x.SalesOrderLineNo,
                        x.SkuCode,
                        x.UomCode,
                        x.Quantity,
                        x.LocationCode,
                        x.LotNo))
                    .ToArray()));
    }
}
