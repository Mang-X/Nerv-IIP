# Business Wave 3 ERP Registration Verify Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Coordinate shared integration for ERP after #137, #138 and #139 service slices are ready.

**Architecture:** ERP service slices own domain behavior. This plan owns shared solution/AppHost/IAM/schema/readiness/script integration and prepares Full-chain acceptance #77.

**Tech Stack:** .NET 10, Aspire AppHost, IAM seed, governed PowerShell scripts, Markdown architecture docs.

---

## Specification

Use:

1. `docs/superpowers/specs/2026-05-23-business-wave-3-agent-session-design.md`
2. `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`

## Files

- Modify: `backend/Nerv.IIP.sln`
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- Modify: `docs/architecture/authorization-matrix.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`
- Create: `scripts/verify-business-erp-procurement-mvp.ps1`
- Create: `scripts/verify-business-erp-sales-mvp.ps1`
- Create: `scripts/verify-business-erp-finance-mvp.ps1`
- Create: `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`

## Task 1: Confirm ERP Slice Readiness

- [ ] **Step 1: Inspect slice handoffs**

Read the final summaries for:

1. #137 ERP Procurement
2. #138 ERP Sales
3. #139 ERP Finance

Copy each `Shared Changes Needed` section into the integration summary.

- [ ] **Step 2: Verify local project presence**

Run:

```powershell
rg --files backend/services/Business/Erp
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

Expected: ERP projects exist and focused tests pass.

## Task 2: Add Solution And AppHost Registration

- [ ] **Step 1: Add solution entries**

Add ERP Domain, Infrastructure, Web, Domain.Tests and Web.Tests projects to `backend/Nerv.IIP.sln`.

- [ ] **Step 2: Build backend solution**

Run:

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

Expected: solution builds. If ERP fails, return the blocker to the owning slice instead of hiding it in integration.

- [ ] **Step 3: Register ERP in AppHost**

Add PostgreSQL database:

```csharp
var businessErpDatabase = postgres.AddDatabase("business-erp-db", "nerv_iip_erp");
```

Register the service as `business-erp`, using local port `5118` unless the port matrix changed:

```csharp
var businessErp = builder.AddProject<Projects.Nerv_IIP_Business_Erp_Web>("business-erp")
    .WithHttpEndpoint(port: 5118, name: "http")
    .WithEnvironment("Persistence__Provider", "PostgreSQL")
    .WithEnvironment("Messaging__Provider", messagingProvider)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(businessErpDatabase, "PostgreSQL")
    .WaitFor(businessErpDatabase)
    .WaitFor(otelCollector);
```

Add RabbitMQ reference under the existing `rabbitmq is not null` pattern, add Gateway reference if needed, and include `businessErp` in Gateway references.

- [ ] **Step 4: Build AppHost**

Run:

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: AppHost builds.

## Task 3: Add IAM, Schema And Readiness Docs

- [ ] **Step 1: Seed permissions**

Add:

1. `business.erp.procurement.read`
2. `business.erp.procurement.manage`
3. `business.erp.sales.read`
4. `business.erp.sales.manage`
5. `business.erp.finance.read`
6. `business.erp.finance.manage`

- [ ] **Step 2: Update authorization matrix**

Document each permission and owner area.

- [ ] **Step 3: Update schema catalog**

Add `erp` tables from Procurement, Sales and Finance. Confirm comments and JSON/text column guidance match schema convention tests.

- [ ] **Step 4: Update readiness and README**

Update current facts:

1. ERP is implemented only when all three slice verify scripts pass.
2. ERP uses port 5118.
3. Full-chain acceptance is unblocked only after ERP final verify passes.

## Task 4: Create Verify Scripts

- [ ] **Step 1: Create focused scripts**

Each script must dot-source `scripts/lib/ScriptAutomation.ps1` and use helper functions rather than direct native command calls.

Focused scripts:

1. `scripts/verify-business-erp-procurement-mvp.ps1`
2. `scripts/verify-business-erp-sales-mvp.ps1`
3. `scripts/verify-business-erp-finance-mvp.ps1`

Each script runs ERP Domain/Web tests with filters appropriate to its slice plus schema convention tests when mappings are touched.

- [ ] **Step 2: Create final ERP aggregate script**

`scripts/verify-business-erp-procurement-sales-finance-mvp.ps1` should run:

1. ERP procurement verify.
2. ERP sales verify.
3. ERP finance verify.
4. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`.

- [ ] **Step 3: Run governance**

Run:

```powershell
scripts/check-script-governance.ps1
```

Expected: script governance passes.

## Task 5: Run Final ERP Integration Verification

- [ ] **Step 1: Run focused and aggregate checks**

Run:

```powershell
scripts/verify-business-erp-procurement-mvp.ps1
scripts/verify-business-erp-sales-mvp.ps1
scripts/verify-business-erp-finance-mvp.ps1
scripts/verify-business-erp-procurement-sales-finance-mvp.ps1
git diff --check
```

Expected: all checks pass.

- [ ] **Step 2: Record Wave 3 integration state**

In the PR/session summary, include:

```markdown
## Wave 3 ERP Integration State

- ERP Procurement: registered | skipped | blocked - reason and verification command
- ERP Sales: registered | skipped | blocked - reason and verification command
- ERP Finance: registered | skipped | blocked - reason and verification command
- AppHost: registered | skipped | blocked - reason and verification command
- Full-chain acceptance: unblocked | blocked - reason
```

## Self-Review Checklist

1. ERP is the only new Wave 3 service.
2. Local port 5118 is documented everywhere it is used.
3. IAM seed, authorization matrix, schema catalog and readiness agree.
4. Full-chain acceptance is not marked unblocked until final ERP verify passes.
