# MAN-424 DeviceAsset Reference Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reject invalid DeviceAsset supplier and parent references, including cyclic hierarchies and concurrent reference/disable races, without changing the public API or database schema.

**Architecture:** A service-local EF-backed validator enforces supplier and parent invariants from the MasterData application write paths. A PostgreSQL transaction-scoped organization/environment lock serializes DeviceAsset reference mutations with the existing BusinessPartner/DeviceAsset disable guards, and commits the mutation before releasing the lock.

**Tech Stack:** .NET 10, C#, MediatR/NetCorePal commands, EF Core, PostgreSQL advisory locks, xUnit, SQLite provider-light tests, optional PostgreSQL profile tests.

## Global Constraints

- `parentDeviceId` accepts only the DeviceAsset public GUID and is stored in canonical GUID form; DeviceAsset code is not an alternate input.
- A non-empty `supplierPartnerCode` must resolve to an enabled same-organization/same-environment BusinessPartner whose `PartnerRoles` contains `supplier` case-insensitively.
- A non-empty parent must resolve to an enabled same-organization/same-environment DeviceAsset, cannot be the device itself, and cannot make the device an ancestor of itself.
- All service-local queries are asynchronous and receive the caller's `CancellationToken`.
- Expected reference, scope, role, self-parent, and cycle failures surface as `KnownException`; cancellation and unexpected provider failures are not disguised as business validation errors.
- PostgreSQL reference mutations and BusinessPartner/DeviceAsset disable guards for the same organization/environment are serialized through one transaction-scoped advisory lock, with persistence before lock release.
- Do not add cross-schema foreign keys, raw SQL outside Infrastructure, provider APIs outside Infrastructure, secrets, or provider-specific types to public contracts.
- No schema, migration, HTTP endpoint, facade declaration, OpenAPI snapshot, generated client, or product-doc change is expected.
- Follow strict TDD: every behavior test must be observed failing for the expected missing behavior before production implementation makes it pass.

---

### Task 1: Deliver DeviceAsset reference validation and concurrency safety

**Files:**
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/DeviceAssetReferenceValidator.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/MasterDataReferenceScopeCoordinator.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/MasterDataPersistenceServiceCollectionExtensions.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/CreateMasterDataCommands.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/MasterDataLifecycleCommands.cs`
- Create or modify focused tests under: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/`
- Modify: `docs/architecture/implementation-readiness.md`

**Interfaces:**
- Consumes: `ApplicationDbContext`, `IUnitOfWork`/`ITransactionUnitOfWork`, `DeviceAssetId`, `BusinessPartner.PartnerRoles`, existing register/update/lifecycle command contracts.
- Produces: an asynchronous service-local reference validator for create/update and a scope coordinator that executes a callback under the PostgreSQL transaction lock.

- [ ] **Step 1: Write focused failing create-path tests**

  Add real EF-backed tests that create actual `BusinessPartner` and `DeviceAsset` rows. Cover a valid supplier-capable partner and active parent, then missing/inactive/wrong-scope/non-supplier partners and missing/inactive/wrong-scope/malformed parents. Assert `KnownException` for invalid input and persisted canonical public GUID for the valid parent.

- [ ] **Step 2: Run the create tests and capture RED**

  Run the new test class only. Confirm failures are assertions showing the current handler accepted invalid references or lacks the validator dependency, not fixture/compile failures.

- [ ] **Step 3: Implement minimal asynchronous create validation**

  Add the EF-backed validator and invoke it from `RegisterDeviceAssetCommandHandler` before adding the aggregate. Normalize whitespace and GUIDs once. Use `SingleOrDefaultAsync`/bounded hierarchy reads scoped by organization/environment, enabled state, and the provided `CancellationToken`. Keep raw/provider APIs out of Web/Application code.

- [ ] **Step 4: Run create tests and capture GREEN**

  Run the same focused class. Confirm all create cases pass with no new warnings.

- [ ] **Step 5: Write focused failing update and hierarchy tests**

  Add update cases for valid supplier/parent changes, invalid supplier/parent changes, empty-value clearing, self-parent, direct cycle, and multi-level descendant cycle. Include a legacy unrelated update case so omitted reference fields do not retroactively block unchanged legacy data.

- [ ] **Step 6: Run update tests and capture RED**

  Run only the update/hierarchy cases. Confirm the tests fail because update currently accepts the invalid references/cycles.

- [ ] **Step 7: Implement minimal update and cycle validation**

  Validate only explicitly supplied reference fields; empty strings clear optional references. For a proposed parent, parse and canonicalize the public GUID, load the active scoped parent, and walk ancestor public IDs until the root. Reject the current device ID, repeated ancestors, and malformed stored ancestry with `KnownException`. Apply the normalized values only after validation succeeds.

- [ ] **Step 8: Run update tests and capture GREEN**

  Run the focused update/hierarchy cases and the complete new test class.

- [ ] **Step 9: Write failing PostgreSQL concurrency tests**

  Under the repository's existing `NERV_IIP_TEST_POSTGRES` gate, use independent DbContexts to race `A.parent=B` against `B.parent=A`, and race assigning a supplier/parent against disabling that target. Assert both opposing operations cannot commit and the final persisted graph/reference targets remain valid.

- [ ] **Step 10: Run concurrency tests and capture RED**

  Run the PostgreSQL-gated tests when `NERV_IIP_TEST_POSTGRES` is available. If the environment is unavailable, retain the gated tests and record the exact skip/blocker; do not represent provider-light execution as PostgreSQL concurrency evidence.

- [ ] **Step 11: Implement the scope coordinator and lock all racing write paths**

  Follow the existing MES PostgreSQL coordinator pattern: use a transaction-scoped advisory lock keyed by normalized organization/environment in Infrastructure; join an existing transaction or own/commit/rollback one; run reference validation, aggregate mutation, and `SaveEntitiesAsync` before release. Register it in MasterData persistence DI. Use the same coordinator for DeviceAsset register/update and for disabling BusinessPartner or DeviceAsset. Direct-construction provider-light tests may retain the outer save boundary but must not claim distributed serialization.

- [ ] **Step 12: Run concurrency and focused tests GREEN**

  Re-run the PostgreSQL test if available, then the complete focused test class and `MasterDataApiContractTests`.

- [ ] **Step 13: Document the delivered behavior**

  Add a concise MAN-424/#772 implementation-readiness entry covering public-GUID parent semantics, supplier role/scope checks, cycle/error/concurrency behavior, and the explicit statement that schema/migration/endpoints/contracts/facade/OpenAPI/generated client are unchanged.

- [ ] **Step 14: Run service and repository gates**

  Run:

  ```bash
  dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --nologo
  dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --nologo
  dotnet test backend/Nerv.IIP.sln --nologo
  git diff --check
  ```

  Report the existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 warning separately if it remains unchanged; no new warning is acceptable.

- [ ] **Step 15: Self-review and commit**

  Review `git diff` for scope, cancellation propagation, exact org/env predicates, supplier role logic, canonical GUID handling, cycle termination, lock lifetime, and friendly error semantics. Commit the implementation and documentation as one focused feature commit after the earlier design/plan commit.
