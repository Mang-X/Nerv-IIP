# MAN-507 MES CAP PostgreSQL Timeout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the existing asset-unavailable CAP consumer atomically persist its idempotency inbox record, work-center unavailability, and optional scheduling result when MES uses real PostgreSQL with CAP InMemory messaging.

**Architecture:** Preserve the existing CAP subscription, event envelope validation, idempotency key, planning-store abstractions, and 30-second condition-based assertion. The defect is the missing explicit EF Core save boundary after the subscriber has successfully staged all three changes in one scoped `ApplicationDbContext`; add that boundary only to the asset-unavailable path covered by #920. Do not audit or modify the remaining MES CAP handlers, including the symmetric asset-restored path; MAN-421/#754 owns that broader inventory.

**Tech Stack:** .NET 10, xUnit, EF Core, PostgreSQL 18, CAP InMemory messaging, `WebApplicationFactory`.

---

### Task 1: Prove and fix the asset-unavailable transaction boundary

**Files:**
- Modify: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesCapSubscriptionTests.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/MaintenanceAssetEventHandlers.cs`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Lock the real PostgreSQL regression to all staged facts**

Keep `PostgreSQL_cap_with_inmemory_messaging_delivers_asset_unavailable_event_to_mes_consumer` on `[PostgreSqlFact]`, CAP InMemory messaging, and the independent-scope condition-based assertion. Extend the successful observation so it requires all facts created by one delivery: the `business-mes.asset-unavailable` processed-event inbox row, the open work-center unavailability, and the scheduling result. Do not lengthen the timeout, replace PostgreSQL, silently skip, or inspect source paths.

- [ ] **Step 2: Run the exact test against PostgreSQL and verify RED**

Run with a live disposable PostgreSQL connection:

```powershell
$env:NERV_IIP_TEST_POSTGRES = "<live PostgreSQL connection string>"
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj `
  --no-restore `
  --filter "FullyQualifiedName=Nerv.IIP.Business.Mes.Web.Tests.MesCapSubscriptionTests.PostgreSQL_cap_with_inmemory_messaging_delivers_asset_unavailable_event_to_mes_consumer" `
  --logger "console;verbosity=detailed"
```

Expected: CAP logs show `AssetUnavailableIntegrationEventHandlerForReschedule.HandleCapAsync` executing and returning successfully, while an independent `ApplicationDbContext` never observes the three PostgreSQL facts before the existing 30-second deadline.

- [ ] **Step 3: Add the narrow explicit save boundary**

After `AssetUnavailableIntegrationEventHandlerForReschedule` has staged the inbox, unavailability, and optional schedule-result changes, call:

```csharp
await dbContext.SaveChangesAsync(cancellationToken);
```

Keep the save after all mutations so one EF Core transaction commits the delivery atomically. Do not add a timeout workaround, change CAP transport, alter the database schema, or modify other consumers.

- [ ] **Step 4: Run the exact test against PostgreSQL and verify GREEN**

Run the command from Step 2 against the same disposable PostgreSQL instance. Expected: one test passes, with all three facts visible from an independent scope.

- [ ] **Step 5: Record the implementation truth and bounded follow-up**

Update `docs/architecture/implementation-readiness.md` with MAN-507/#920 evidence: the subscriber was invoked and returned successfully but lacked the explicit `SaveChangesAsync` boundary; the real PostgreSQL + CAP InMemory regression now observes the atomic facts. State that this PR changes no endpoint, facade, OpenAPI contract, schema, or migration, and that MAN-421/#754 remains responsible for auditing other MES handlers.

- [ ] **Step 6: Run focused MES verification and commit**

Run:

```powershell
$env:NERV_IIP_TEST_POSTGRES = "<live PostgreSQL connection string>"
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj `
  --no-restore `
  --filter "Category=cap-inmemory"
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj `
  --no-restore
git diff --check
```

Expected: the CAP InMemory real-PostgreSQL regressions and full MES Web test project pass. Commit only the three listed files with a focused MAN-507 message. The primary agent will independently run the backend solution gate after review.
