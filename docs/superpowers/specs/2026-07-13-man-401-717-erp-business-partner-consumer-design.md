# MAN-401 #717 ERP Business Partner Consumer Design

## Scope

This is only the ERP consumer slice of #717. It consumes `BusinessPartnerChangedIntegrationEvent` and prevents newly created/submitted purchase and sales orders from using a disabled business partner. It does not modify Scheduling, MES, Maintenance, Gateway, HTTP endpoints, OpenAPI, or generated clients.

## Current code facts

- `CreatePurchaseOrderCommandHandler` creates the order, starts its approval chain, and moves it to `PendingApproval`; there is no separate purchase-order submit command.
- `CreateSalesOrderCommandHandler` creates a sales order directly in `released` or `credit-held`; there is no separate sales-order submit command.
- Therefore the two create handlers are the correct gates for new order creation/submission.
- MasterData already publishes `BusinessPartnerChangedIntegrationEvent`, and its public payload already has `Status` and `ChangedAtUtc`; no public contract change is required.
- The current MasterData converter always writes `Status = "active"`, including after `BusinessPartner.Disable`. The producer must emit the aggregate's real active/disabled state for the consumer to work.
- ERP already has a durable `processed_integration_events` inbox and persistent dead-letter infrastructure.

## Considered approaches

1. **Durable ERP projection (selected).** Store the latest active/disabled state per organization, environment, and partner code. Consume with the existing envelope guard and inbox, ignore older `ChangedAtUtc` updates, and gate the two order commands. This survives restarts and keeps ERP independent of MasterData availability.
2. **Synchronous MasterData lookup on every order.** This gives current state but does not implement the required event consumer and couples order submission to another service's availability.
3. **In-memory disabled-code cache.** This is simple but loses the gate after process restart and cannot satisfy durable/idempotent consumption.

## Design

Add `BusinessPartnerAvailability` to ERP Infrastructure. Its unique identity is `(organization_id, environment_id, partner_code)` and it records `status`, `changed_at_utc`, and the source event id. The event handler validates the envelope, source service, resource type, code, and status. Invalid business payloads go to the persistent dead-letter store and return without throwing. A valid event is recorded in the existing inbox and then upserts the projection; older events do not overwrite newer state.

The MasterData domain event carries the status already known by the aggregate (`active` or `disabled`), and the converter copies it into the unchanged public `MasterDataChangedPayload` contract.

`CreatePurchaseOrderCommandHandler` gates `SupplierCode`; `CreateSalesOrderCommandHandler` loads the quotation and gates its `CustomerCode`. Idempotent replays of already-created orders remain successful because the existing replay return happens before the new-partner gate. A disabled partner produces a `KnownException` from the command boundary; the integration-event handler itself does not raise business exceptions.

## Verification

- MasterData converter test proves disable emits `disabled` and enable emits `active`.
- ERP focused tests drive a real event through the handler, persist the projection, replay the event, verify stale-event protection, and prove subsequent PO/SO creation is rejected while re-enable permits creation.
- A PostgreSQL-gated acceptance test migrates a disposable ERP schema and proves event consumption changes both order behaviors against the real provider when `NERV_IIP_TEST_POSTGRES` is set.
- ERP schema convention tests, affected service suites, and backend solution gates remain required.

