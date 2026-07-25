# Quality Reinspection History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make same-source Quality reinspection a real, auditable write path that can release a MES quality hold.

**Architecture:** Keep initial inspection creation idempotent, and add a separate predecessor-targeted reinspection command that appends immutable attempt records. Reuse the existing Quality result event contract with record-scoped idempotency, expose the command through BusinessGateway, and prove the chain with real Quality handlers and the real MES consumer.

**Tech Stack:** .NET 10, CleanDDD/MediatR, EF Core/PostgreSQL, FastEndpoints, CAP integration-event converters, xUnit, BusinessGateway OpenAPI, Hey API/pnpm.

---

### Task 1: Model immutable reinspection attempts

**Files:**
- Modify: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/InspectionAggregateTests.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs`

- [ ] **Step 1: Write failing aggregate tests**

Add tests that assert:

```csharp
var initial = NewRejectedInspection();
var reinspection = InspectionRecord.Reinspect(
    initial,
    inspectionPlan: null,
    [InspectionResultLineInput.Pass("appearance", "ok", null, [])],
    dispositionReason: null,
    dispositionAttachmentFileIds: [],
    uomConversions: [],
    measuringDeviceUsage: null);

Assert.Equal(1, initial.AttemptNumber);
Assert.Null(initial.ReinspectionOfInspectionRecordId);
Assert.Equal(2, reinspection.AttemptNumber);
Assert.Equal(initial.Id, reinspection.ReinspectionOfInspectionRecordId);
Assert.Equal(initial.SourceDocumentId, reinspection.SourceDocumentId);
Assert.IsType<InspectionPassedDomainEvent>(Assert.Single(reinspection.GetDomainEvents()));
```

Also assert that a passed predecessor cannot be reinspected and that a planned
reinspection reuses the original plan version even after it is superseded.

- [ ] **Step 2: Run the aggregate tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~Reinspection"
```

Expected: compilation fails because `AttemptNumber`,
`ReinspectionOfInspectionRecordId`, and `InspectionRecord.Reinspect` do not
exist.

- [ ] **Step 3: Implement the minimal aggregate model**

Add:

```csharp
public int AttemptNumber { get; private set; } = 1;
public InspectionRecordId? ReinspectionOfInspectionRecordId { get; private set; }
```

Implement `InspectionRecord.Reinspect(...)` to validate a non-passed
predecessor, inherit its immutable source/scope/quantity/stock facts, evaluate
against its exact plan when present, set attempt and predecessor before returning
the new record, and emit the normal result event through the constructor.

- [ ] **Step 4: Run the aggregate tests and verify GREEN**

Run the command from Step 2. Expected: all `Reinspection` tests pass.

### Task 2: Add the reinspection command and service endpoint

**Files:**
- Modify: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityInspectionEndpointContractTests.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Repositories/InspectionRepositories.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionRecords/CreateReinspectionCommand.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/InspectionRecords/InspectionRecordEndpoints.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/InspectionPlans/InspectionPlanEndpoints.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/InspectionRecords/ListInspectionRecordsQuery.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/InspectionRecords/GetInspectionRecordQuery.cs`

- [ ] **Step 1: Write failing command, projection, and endpoint-contract tests**

Cover:

```csharp
var first = await create.Handle(rejectedCommand, CancellationToken.None);
await db.SaveChangesAsync();

var second = await reinspect.Handle(
    new CreateReinspectionCommand(
        first, "org-001", "env-dev",
        [PassLine()], null, [], null),
    CancellationToken.None);
await db.SaveChangesAsync();

var replay = await reinspect.Handle(sameCommand, CancellationToken.None);

Assert.Equal(second.InspectionRecordId, replay.InspectionRecordId);
Assert.Equal(2, second.AttemptNumber);
Assert.Equal(2, await db.InspectionRecords.CountAsync());
```

Assert the list/detail response contains attempt `2` and predecessor ID. Extend
the live contract registry expectation to:

```csharp
Assert.Contains(QualityInspectionEndpointContracts.All, x =>
    x.HttpMethod == "POST"
    && x.Route == "/api/business/v1/quality/inspection-records/{inspectionRecordId}/reinspections"
    && x.PermissionCode == BusinessPermissionCodes.QualityInspectionRecordsCreate
    && x.OperationId == "createBusinessQualityReinspection");
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Reinspection|FullyQualifiedName~Inspection_endpoints"
```

Expected: compilation/contract failures because the command, route, and response
fields are missing.

- [ ] **Step 3: Implement repository, handler, endpoint, and read projections**

Add repository lookup by predecessor. Implement:

```csharp
public sealed record CreateReinspectionCommand(
    InspectionRecordId ReinspectionOfInspectionRecordId,
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<InspectionResultLineCommandInput> ResultLines,
    string? DispositionReason,
    IReadOnlyCollection<string> DispositionAttachmentFileIds,
    MeasuringDeviceId? MeasuringDeviceId = null)
    : ICommand<CreateReinspectionResult>;

public sealed record CreateReinspectionResult(
    InspectionRecordId InspectionRecordId,
    int AttemptNumber);
```

The handler must scope the predecessor by organization/environment, return an
existing direct successor on replay, load the predecessor's exact plan when
needed, reuse measuring-device policy and UOM conversion checks, create the
record through `InspectionRecord.Reinspect`, and add it through the repository.

Add request/response endpoint DTOs and register the service route. Add
`AttemptNumber` and `ReinspectionOfInspectionRecordId` to list/detail responses.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the command from Step 2. Expected: focused tests pass.

### Task 3: Persist and document reinspection lineage

**Files:**
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionRecordEntityTypeConfiguration.cs`
- Create: EF-generated `AddQualityReinspectionHistory` migration pair under `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Write failing relational model assertions**

In the Quality web test project, assert the EF model has:

```csharp
Assert.Equal("attempt_number", entity.FindProperty(nameof(InspectionRecord.AttemptNumber))!.GetColumnName());
Assert.Equal(
    "reinspection_of_inspection_record_id",
    entity.FindProperty(nameof(InspectionRecord.ReinspectionOfInspectionRecordId))!.GetColumnName());
Assert.Contains(entity.GetIndexes(), index =>
    index.IsUnique
    && index.Properties.Select(x => x.Name).SequenceEqual(
        [nameof(InspectionRecord.ReinspectionOfInspectionRecordId)]));
```

- [ ] **Step 2: Run the model assertion and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Reinspection_relational_model"
```

Expected: the mapped properties/index do not exist.

- [ ] **Step 3: Configure persistence and generate the EF migration**

Map both columns with comments. Replace the old six-column source unique index
with the same source columns plus `AttemptNumber`. Add a filtered unique index
and restrictive self foreign key for non-null predecessor.

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddQualityReinspectionHistory `
  --project backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure `
  --startup-project backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web
```

Inspect the migration: existing rows receive attempt `1`; the old unique index
is dropped before the new source-plus-attempt index is created; no raw
cross-schema SQL or data deletion is present.

- [ ] **Step 4: Update the schema catalog and verify GREEN**

Update the Quality migration source list and `inspection_records` row with
attempt/predecessor semantics and both unique-index intents. Run the focused
model test; expected: pass.

### Task 4: Make result-event idempotency attempt-safe

**Files:**
- Modify: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityInspectionIntegrationEventTests.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventConverters/InspectionIntegrationEventConverters.cs`

- [ ] **Step 1: Write a failing distinct-attempt idempotency test**

Create two rejected records with the same source facts but different record IDs,
convert both, and assert:

```csharp
Assert.NotEqual(firstEvent.IdempotencyKey, secondEvent.IdempotencyKey);
Assert.Contains(first.Id.ToString(), firstEvent.IdempotencyKey);
Assert.Contains(second.Id.ToString(), secondEvent.IdempotencyKey);
```

Keep the existing same-record deterministic assertion.

- [ ] **Step 2: Run the event tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Inspection_result_event_idempotency"
```

Expected: distinct attempts currently have the same idempotency key.

- [ ] **Step 3: Add record identity to the event idempotency key**

Change event creation to:

```csharp
EventIds.Idempotency(
    idempotencyPrefix,
    record.OrganizationId,
    record.EnvironmentId,
    record.SourceService,
    record.SourceDocumentId,
    record.Id.ToString())
```

Do not change the event schema or MES gate.

- [ ] **Step 4: Run the event tests and verify GREEN**

Run the command from Step 2. Expected: both deterministic-replay and
distinct-attempt tests pass.

### Task 5: Expose the command through BusinessGateway and generated API

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Quality/BusinessConsoleQualityEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Modify: `docs/architecture/facade-coverage-matrix.json`
- Modify: `docs/architecture/facade-coverage-matrix.md`
- Modify: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Modify: `frontend/packages/api-client/src/generated/business-console/**`

- [ ] **Step 1: Write failing proxy, authorization, OpenAPI, and facade tests**

Require operation ID `createBusinessConsoleQualityReinspection`, permission
`business.quality.inspection-records.create`, route/path ID forwarding,
organization/environment forwarding, and response ID/attempt parsing.

- [ ] **Step 2: Run the focused Gateway/facade tests and verify RED**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Quality|FullyQualifiedName~Authorization|FullyQualifiedName~OpenApi"
dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj --no-restore
```

Expected: the new route/operation and facade row are absent.

- [ ] **Step 3: Implement the facade and governance declaration**

Add the request/response models, `IBusinessQualityClient` method, HTTP client
forwarding, and authorized FastEndpoint. Register the new service route as
`exposed` with gateway operation ID
`createBusinessConsoleQualityReinspection`. Update generated summary counts.

- [ ] **Step 4: Export OpenAPI and regenerate the client**

Run:

```powershell
scripts/export-gateway-openapi.ps1
pnpm -C frontend generate:api
```

Do not hand-edit snapshots or generated client code.

- [ ] **Step 5: Run Gateway/facade tests and verify GREEN**

Run the commands from Step 2 plus:

```powershell
scripts/verify-openapi-client-drift.ps1
```

Expected: all pass and no OpenAPI/client drift remains.

### Task 6: Prove the real Quality-to-MES lifecycle

**Files:**
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/QualityMesReinspectionHoldAcceptanceTests.cs`
- Modify: `frontend/apps/docs/docs/roles/team-leader.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Write the failing cross-service acceptance test**

The test must:

1. create a real MES work order;
2. execute `CreateInspectionRecordCommandHandler` for a rejected `mes` source;
3. convert its real `InspectionRejectedDomainEvent`;
4. invoke `QualityInspectionResultIntegrationEventHandlerForUpdateMesHoldContext`;
5. assert an active hold and `hold-applied` transition;
6. execute `CreateReinspectionCommandHandler` for a passed successor;
7. convert its real `InspectionPassedDomainEvent`;
8. invoke the same MES consumer;
9. assert two Quality records with attempts `1/2`, inactive MES hold, release
   record ID equal to the second Quality record, and timeline kinds
   `hold-applied`, `inspection-released`;
10. replay the reinspection command and assert no third record/event.

- [ ] **Step 2: Run the acceptance test and verify RED**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~QualityMesReinspectionHoldAcceptanceTests"
```

Expected: compilation fails before the reinspection model exists, or behavioral
assertions fail before the implementation is complete.

- [ ] **Step 3: Complete the acceptance seam and docs**

Use the real Quality command handlers, converters, MES consumer, and separate
Quality/MES DbContexts. Do not manually construct result events or seed the
hold. Update readiness with the model, migration, facade declaration, and test
evidence. Restore team-leader step 5 to available while accurately describing
the automated cross-service evidence (not claiming a browser/full-stack run).

- [ ] **Step 4: Run the acceptance test and verify GREEN**

Run the command from Step 2. Expected: the rejected-to-passed chain passes.

### Task 7: Full verification and ready PR handoff

**Files:**
- Review all files changed by Tasks 1-6.

- [ ] **Step 1: Run scoped and governed verification**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~QualityMesReinspectionHoldAcceptanceTests"
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj --no-restore
scripts/check-script-governance.ps1
scripts/verify-openapi-client-drift.ps1
git diff --check
```

Run `dotnet test backend/Nerv.IIP.sln --no-restore` if the scoped runs leave
enough execution time; report any established baseline/environment failure
without describing it as introduced by this change.

- [ ] **Step 2: Review scope and generated artifacts**

Check:

```powershell
git status --short
git diff --stat 92c47dd119191c533d27d5d42fa401faf5bb5f6a
git diff --name-only 92c47dd119191c533d27d5d42fa401faf5bb5f6a
```

Confirm every file serves #954, migration/snapshot/generated outputs are
machine-generated, no credentials/artifacts are present, and no adjacent issue
was implemented.

- [ ] **Step 3: Commit, push, and create one ready PR**

Commit with issue-scoped messages, push
`codex/man-516-quality-reinspection`, and create a non-draft PR whose body:

- uses `Closes #954`;
- states the new Quality endpoint is `exposed`;
- confirms `facade-coverage-matrix.json`, OpenAPI, generated client, schema
  catalog, and product docs impact;
- lists exact verification results and any environment-limited checks;
- does not claim merge or ask GitHub to auto-merge.

- [ ] **Step 4: Verify live PR state and stop**

Use `gh pr view` to confirm ready state, head SHA, base `main`, linked issue,
checks visibility, and URL. Do not merge. Return the ready PR for user review.

## Plan self-review

- Spec coverage: explicit write entry, source/attempt history, replay semantics,
  event delivery, MES apply/release, readable timeline, schema/API governance,
  docs, and ready-PR handoff each map to a task.
- Placeholder scan: the migration pair is explicitly tool-generated under the
  named migrations directory; no placeholder text or deferred implementation
  remains.
- Type consistency: service and facade both use predecessor route ID plus
  organization/environment, result lines, disposition evidence, optional
  measuring device, and return record ID plus attempt number.
- Scope: no MES gate weakening, frontend feature, CAPA workflow, or #799 changes
  are included.
