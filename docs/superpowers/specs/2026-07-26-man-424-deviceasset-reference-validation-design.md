# MAN-424 DeviceAsset Reference Validation Design

## Goal

Close GitHub #772 by making DeviceAsset supplier and parent references trustworthy at the BusinessMasterData write boundary.

## Decisions

1. `supplierPartnerCode` remains a BusinessPartner business code. When non-empty, it must resolve in the same `organizationId` and `environmentId` to an enabled partner whose `PartnerRoles` contains `supplier` case-insensitively.
2. `parentDeviceId` remains a DeviceAsset public GUID, matching the existing database comment, schema catalog, list/detail response, and lifecycle reverse-reference guard. Codes are not accepted as an alternate identifier.
3. Reference validation belongs to the MasterData application write path. EF-backed lookups stay behind a focused service-local validator and always receive the command `CancellationToken`.
4. Device hierarchy validation walks the proposed parent's ancestor chain inside the same organization/environment. It rejects a missing or disabled parent, self-parenting, a proposed descendant parent, and a pre-existing malformed cycle with `KnownException`.
5. PostgreSQL runtime writes that can race with the reference checks are serialized by one transaction-scoped advisory lock per organization/environment. The mutation and `SaveEntitiesAsync` complete before the lock transaction commits. Provider-light tests keep their existing outer save boundary and do not claim cross-process locking.
6. BusinessPartner and DeviceAsset disable guards use the same scope lock, so a reference cannot be accepted concurrently with disabling its target.
7. The change adds no cross-schema foreign key. It also does not change the DeviceAsset table, HTTP endpoints, wire contracts, facade declaration, OpenAPI snapshot, or generated client.

## Error Semantics

All expected validation failures are `KnownException` values suitable for the existing friendly 400 response mapping. Messages identify the rejected field/reference without exposing provider details. Cancellation is not translated into a business error.

## Test Strategy

Tests use real EF-backed MasterData entities and contexts, not repository mocks.

- Create: valid supplier and parent; missing, disabled, wrong-scope, and non-supplier partner; missing, disabled, wrong-scope, and malformed parent.
- Update: valid supplier/parent changes; the same invalid supplier/parent cases; self-parent; direct and multi-level descendant cycles.
- Concurrency: PostgreSQL-gated proof that opposing parent updates cannot both commit and that assignment versus target disable cannot leave an active DeviceAsset pointing to an inactive target.
- Regression: existing MasterData API contract tests, MasterData schema tests, the focused service test project, and the backend solution gate.

## Documentation Impact

The implementation-readiness record will state the validation and concurrency semantics. The existing schema catalog already defines `parent_device_id` as a public DeviceAsset ID; it only needs editing if implementation changes that established meaning, which this design does not.
