# Task 3 Report — BusinessGateway maintenance-plan update facade

## RED evidence

- `dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Maintenance_plan_update_facade|FullyQualifiedName~Maintenance_http_client_exposes_plan_update_proxy|FullyQualifiedName~BusinessGatewayOpenApiTests|FullyQualifiedName~Every_exposed_business_route_requires_expected_permission"`
  - Expected RED: 4 failures of 6 selected tests. The PUT route returned `404`, `UpdatePlanAsync` was absent, and the Gateway OpenAPI did not expose `updateBusinessConsoleMaintenancePlan`.
- `dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj --no-restore --filter "FullyQualifiedName~FacadeCoverage"`
  - Expected RED: 1 failure of 9 tests. The new exposed matrix row correctly failed because Task 4 had not yet exported `updateBusinessConsoleMaintenancePlan` into the BusinessGateway OpenAPI snapshot.

## GREEN evidence

- The focused Gateway command above passed `7/7` after implementation.
- `dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~BusinessGatewayMaintenanceTelemetryTests"` passed `25/25`.
- `git diff --check` passed. Matrix counts were recomputed from JSON: Maintenance `21/16/5/0`; total `357/286/51/20`.

## Changes

- Added the typed update request/response, Maintenance client contract and PUT proxy to `/api/business/v1/maintenance/plans/{planId}`.
- Added an authorized PUT facade at `/api/business-console/v1/maintenance/plans/{planId}`, with operation ID `updateBusinessConsoleMaintenancePlan`, `MaintenancePlansManage`, and `maintenance-plan/{planId}` resource metadata.
- Added create-equivalent trigger validation: at least one non-empty calendar interval or a positive runtime-hour interval.
- Added route, client forwarding, authorization, OpenAPI, validation, and coverage assertions; registered `updateMaintenancePlan` as `exposed`.

## Self-review

- The facade forwards only org/environment and trigger values, preserving Maintenance as the business-rule owner.
- Plan IDs are route-bound and URI-escaped for the downstream request; no OpenAPI snapshot or generated client was edited.
- Coverage remains intentionally RED until Task 4 exports the Gateway snapshot. Its sole failure is the absent operation ID; no other matrix gate failed.

## Commit

`feat(business-gateway): expose maintenance plan updates`

## Review follow-up

- Added full facade-to-downstream HTTP assertions for both trigger shapes:
  - `interval: null` with `runtimeHourInterval: 500`.
  - `interval: "P30D"` with `runtimeHourInterval: null`.
- Added an authorization assertion that the PUT route checks `ResourceType=maintenance-plan` and `ResourceId=plan-001`.
- Mutation RED command:
  - `dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Maintenance_plan_update_facade_preserves_explicit_null_triggers_in_downstream_json|FullyQualifiedName~Maintenance_plan_update_facade_authorizes_the_route_plan_resource"`
  - With temporary local regressions that omitted null JSON properties and cleared route resource metadata, the new tests failed `2/2` for the intended reasons. The production mutations were then fully reverted.
- GREEN evidence:
  - The same focused command passed `2/2` against the committed production implementation.
  - `dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~BusinessGatewayMaintenanceTelemetryTests"` passed `25/25`.
  - `git diff --check` passed.
- Production conclusion: no production bug was found; the existing client preserves explicit nulls and the endpoint already supplies the correct resource metadata.
- Fix commit: `efa02c6 test(business-gateway): cover maintenance plan update forwarding`.
