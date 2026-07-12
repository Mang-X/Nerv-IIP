# Task 1 Report: MES Production Report Detail

## Status

Implemented `GetProductionReportQuery` and the internal MES detail endpoint `GET /api/business/v1/mes/production-reports/{reportNo}` with operation ID `getBusinessMesProductionReport`.

The detail response preserves the existing `ProductionReportFact` projection and adds all consumed material lots with material ID, material-lot ID, consumed quantity, UOM code, and material-issue request number.

## Files changed

- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Production/MesProductionQueries.cs`
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesEndpointContractTests.cs`

## TDD evidence

### RED

Command:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --filter FullyQualifiedName~MesEndpointContractTests --no-restore
```

Result: exit code 1. Compilation failed for the expected missing capability: `GetProductionReportQueryHandler`, `GetProductionReportQuery`, and the detail DTO did not exist.

### GREEN (focused)

Command:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --filter FullyQualifiedName~MesEndpointContractTests --no-restore
```

Result: exit code 0; 78 passed, 0 failed, 0 skipped.

### GREEN (MES Web project)

Command:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

Result: exit code 0; 230 passed, 0 failed, 2 skipped (PostgreSQL-dependent baseline tests).

`git diff --check` also completed with exit code 0.

## Coverage and review

- Registry: count and exact route/permission/operation ID asserted.
- Projection: existing report fields and consumed-lot fields asserted.
- Tenant isolation: duplicate report numbers in another organization cannot leak report or consumption facts.
- Missing record: missing and cross-tenant report numbers both produce `KnownException`.
- Multi-lot: two lots are returned independently and deterministically ordered.
- List regression: existing list handler was not modified; the complete MES Web test project passed.
- Provider neutrality: queries use only standard LINQ/EF Core operators; no raw SQL or provider-specific APIs were introduced.

## Concern / handoff

The facade coverage matrix was intentionally not changed because the exact Task 1 file scope lists only the MES query, endpoint, and contract test; the downstream BusinessGateway facade/classification must register this endpoint when that hop is implemented. Until then, the repository-wide facade coverage gate may report this new service endpoint as undeclared.

The pre-existing `skills-lock.json` modification was preserved and not staged.
