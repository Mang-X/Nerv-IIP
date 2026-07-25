# MES Capitalization Event Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make MES converge immediately for both `WorkOrderCostCapitalized`-before-receipt and receipt-before-`WorkOrderCostCapitalized` delivery without relying on CAP retry backoff.

**Architecture:** Persist the ERP capitalized unit cost as a nullable fact on the existing scope-bound MES `WorkOrder`. The capitalization consumer records the inbox row, stores that work-order fact, and updates any already-requested receipts in one unit of work; the receipt creation handler uses the persisted fact only when the existing request-level `UnitCost` is absent, leaving #1081 contract tightening out of scope.

**Tech Stack:** .NET 10, EF Core, PostgreSQL migrations, CAP integration-event consumers, xUnit.

---

### Task 1: Prove both event orders and inbox completion semantics

**Files:**
- Modify: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/WorkOrderCostCapitalizedPersistenceTests.cs`
- Modify: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/FinishedGoodsCapitalizationTests.cs`

- [ ] **Step 1: Add the event-first failing persistence test**

Add a test that seeds a completed MES work order plus its output lot, handles one ERP capitalization event before any receipt exists, and asserts:

```csharp
await handler.HandleAsync(integrationEvent, CancellationToken.None);
await handler.HandleAsync(integrationEvent, CancellationToken.None);

Assert.Equal(25m, (await verification.WorkOrders.SingleAsync()).CapitalizedUnitCost);
Assert.Single(await verification.ProcessedIntegrationEvents
    .Where(item => item.ConsumerName == WorkOrderCostCapitalizedIntegrationEventHandler.ConsumerName)
    .ToListAsync());
```

Then create a receipt with `UnitCost: null`, save it, and assert its unit cost is `25m` and one `FinishedGoodsReceiptRequestedDomainEvent` was dispatched. The duplicate handler call proves the early delivery completed its inbox row instead of remaining a CAP failure.

- [ ] **Step 2: Strengthen the receipt-first test**

Seed the work order required by the new durable projection, retain the existing receipt-first assertions, and additionally assert:

```csharp
Assert.Equal(25m, (await verification.WorkOrders.SingleAsync()).CapitalizedUnitCost);
```

- [ ] **Step 3: Add the work-order domain invariant test**

Add a domain test with:

```csharp
workOrder.ApplyCapitalizedUnitCost(25m);
workOrder.ApplyCapitalizedUnitCost(25m);

Assert.Equal(25m, workOrder.CapitalizedUnitCost);
Assert.Throws<InvalidOperationException>(() => workOrder.ApplyCapitalizedUnitCost(26m));
```

This keeps duplicate identical facts idempotent and fails closed if a later distinct event tries to rewrite the capitalized cost.

- [ ] **Step 4: Run the tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --filter "FullyQualifiedName~WorkOrderCostCapitalizedPersistenceTests"
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj --filter "FullyQualifiedName~FinishedGoodsCapitalizationTests"
```

Expected: compilation or assertion failure because `WorkOrder.CapitalizedUnitCost` and `ApplyCapitalizedUnitCost` do not exist and the current consumer throws when no receipt exists.

### Task 2: Persist and consume the capitalized work-order fact

**Files:**
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/WorkOrderAggregate/WorkOrder.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/WorkOrderEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/WorkOrderCostCapitalizedIntegrationEventHandler.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs`

- [ ] **Step 1: Add the work-order capitalization fact**

Add the nullable property and invariant:

```csharp
public decimal? CapitalizedUnitCost { get; private set; }

public void ApplyCapitalizedUnitCost(decimal unitCost)
{
    var normalizedUnitCost = DomainGuard.Positive(unitCost, nameof(unitCost));
    if (CapitalizedUnitCost.HasValue && CapitalizedUnitCost.Value != normalizedUnitCost)
    {
        throw new InvalidOperationException("Work order already has a different capitalized unit cost.");
    }

    CapitalizedUnitCost = normalizedUnitCost;
}
```

Map it as:

```csharp
builder.Property(x => x.CapitalizedUnitCost)
    .HasColumnName("capitalized_unit_cost")
    .HasPrecision(18, 6)
    .HasComment("ERP-authoritative capitalized unit cost retained so receipt creation can converge regardless of event order.");
```

- [ ] **Step 2: Make the consumer gate-and-persist**

Load the exact organization/environment/work-order row. If it does not exist, retain fail-closed behavior because capitalization cannot be safely attached to an unknown MES work order. Otherwise call:

```csharp
workOrder.ApplyCapitalizedUnitCost(integrationEvent.Payload.UnitCost);
```

Then apply the same unit cost to all matching receipts still in `Requested` state. Remove the `receipts.Count == 0` exception and retain the existing `SaveEntitiesAsync` transaction so work-order projection, receipt changes, domain events, and inbox row commit atomically.

- [ ] **Step 3: Use the stored fact on later receipt creation**

Pass the existing client value when present, otherwise the durable ERP fact:

```csharp
request.UnitCost ?? workOrder.CapitalizedUnitCost
```

Do not remove or reject the request-level field in this issue; that public contract change belongs to #1081.

- [ ] **Step 4: Run the targeted tests and verify GREEN**

Run the same two commands from Task 1.

Expected: both projects pass, including both event orders and duplicate early delivery.

- [ ] **Step 5: Commit the behavioral slice**

```powershell
git add backend/services/Business/Mes/src backend/services/Business/Mes/tests
git commit -m "fix(mes): handle capitalization before receipt"
```

### Task 3: Generate and document the schema change

**Files:**
- Create via EF tooling: migration `AddMesWorkOrderCapitalizedUnitCost` under `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Generate the EF migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddMesWorkOrderCapitalizedUnitCost `
  --project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure `
  --startup-project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web
```

Expected: one nullable `numeric(18,6)` `mes.work_orders.capitalized_unit_cost` column with its comment; no unrelated model changes.

- [ ] **Step 2: Update the schema catalog**

Extend the `work_orders` row to name `capitalized_unit_cost` as the ERP-authoritative durable fact used to bridge event ordering. Clarify the lifecycle: early events commit the projection plus inbox, later receipt creation consumes it, and receipt-first delivery is backfilled by the consumer.

- [ ] **Step 3: Update implementation readiness**

Add a `MES 资本化事件乱序收敛（MAN-600 / #1084）` section recording:

```text
WorkOrderCostCapitalized first -> work-order cost projection + inbox commit -> later receipt immediately emits Inventory request
receipt first -> no Inventory request until capitalization -> consumer backfills cost and emits request
```

State explicitly that no HTTP endpoint, public contract, facade declaration, generated client, or #965 main-chain evidence script changed.

- [ ] **Step 4: Commit migration and docs**

```powershell
git add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations docs
git commit -m "docs(mes): record capitalization ordering semantics"
```

### Task 4: Verify scope and repository gates

**Files:**
- Verify only; no expected new files.

- [ ] **Step 1: Run MES domain and web test projects**

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj
```

Expected: zero failed tests and no new warnings.

- [ ] **Step 2: Run the backend solution gate**

```powershell
dotnet test backend/Nerv.IIP.sln
```

Expected: zero failed tests, including schema conventions and facade coverage.

- [ ] **Step 3: Inspect generated migration and exact diff**

```powershell
git diff 92c47dd119191c533d27d5d42fa401faf5bb5f6a --stat
git diff 92c47dd119191c533d27d5d42fa401faf5bb5f6a -- scripts nerv.ps1
git status --short
```

Expected: only MES domain/infrastructure/web/tests plus the plan, schema catalog, and readiness are changed; the scripts diff is empty.

- [ ] **Step 4: Push and create the ready PR**

```powershell
$prBody = @"
## Summary
- persist ERP-authoritative capitalized unit cost on the MES work order
- converge both capitalization-before-receipt and receipt-before-capitalization delivery
- commit early-delivery inbox state without CAP retry backoff

## Validation
- `dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj`
- `dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj`
- `dotnet test backend/Nerv.IIP.sln`

文档：无产品文档影响；已更新 implementation readiness 与 database schema catalog。

Facade：未新增或修改 HTTP endpoint；无需更新 facade-coverage-matrix.json。

Closes #1084
"@
git push -u origin codex/man-600-mes-capitalization-ordering
gh pr create --base main --head codex/man-600-mes-capitalization-ordering --title "fix(mes): converge capitalization event ordering" --body $prBody
```

The PR body must include `Closes #1084`, root cause, both verified event orders, validation commands, `文档：无产品文档影响`, and `Facade：未新增或修改 HTTP endpoint；无需更新 facade-coverage-matrix.json`。Create it ready for review, not draft, and do not merge.
