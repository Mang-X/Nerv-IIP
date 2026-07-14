# Production Report Detail and Reversal Audit Design

## Goal

Close issues #881 and #882 in one delivery by exposing a complete production-report detail read path for the reversal dialog and by recording the authenticated reversal actor as an auditable fact.

## Scope

- Add a MES detail endpoint for one production report, including consumed material lots.
- Expose that endpoint through BusinessGateway, the governed OpenAPI snapshot, generated client, and stable client barrel.
- Replace the reversal dialog's consumed-material fallback copy with an on-demand, read-only lot list.
- Inject the authenticated principal at BusinessGateway for reversal and persist it on the negative reversal production report.
- Add the required migration, schema catalog update, facade declaration, contract tests, persistence tests, and frontend tests.

The list endpoint will not carry consumed lots. Variable-length consumption details belong to the detail request and should only be loaded when the destructive-action dialog is opened.

## Backend Design

MES adds `GET /api/business/v1/mes/production-reports/{reportNo}` with `ReportingRead`. Its query filters the report and consumptions by organization, environment, and report number, uses no tracking, and returns the full production-report fact plus stably ordered consumed lots containing material ID, material-lot ID, consumed quantity, UOM, and material-issue request number. A missing report produces the established MES known-domain error.

BusinessGateway adds `GET /api/business-console/v1/mes/production-reports/{reportNo}` with `MesReportingRead`. The endpoint is declared `exposed` in the facade matrix and is carried through governed OpenAPI export and client generation.

For reversal, the public Gateway request continues to omit actor fields. The endpoint obtains `RequireAuthorizedPrincipalActor().ActorRef`, and the downstream client constructs a dedicated MES request containing that actor. MES validates `ActorRef` and includes it in the idempotency fingerprint. The negative reversal `ProductionReport` stores it as nullable `ReversedBy`; historical and normal report rows remain compatible, while all newly created reversals require a nonblank actor.

## Frontend Design

The reversal dialog requests detail only while open and only when organization, environment, and report number are present. It shows explicit loading, failure, empty, and populated states. A failed or incomplete detail load disables confirmation so an operator cannot perform a destructive action without seeing the available consumption facts.

Each consumed lot displays material, lot, quantity with UOM, and material-issue request number. Closing the dialog or selecting another report must not display stale details from the previous report. Existing good, scrap, rework, reason, permission, and idempotency behavior from #893 remains unchanged.

## Data and Migration

Add nullable `mes.production_reports.reversed_by varchar(100)` with a database comment. Update the EF entity mapping, migration snapshot, and database schema catalog. No new table or cross-schema relationship is introduced.

## Testing and Verification

- MES tests cover endpoint registry metadata, complete detail projection, tenant isolation, missing reports, stable consumption facts, actor validation, persistence, and schema mapping/migration.
- Gateway tests cover detail authorization, real proxy forwarding, OpenAPI shape, principal-derived reversal actor, and rejection of request-body actor spoofing.
- Frontend tests cover request-on-open, multi-lot rendering, empty state, failure lockout, and stale-data prevention.
- Run targeted MES, BusinessGateway, facade coverage, frontend unit/typecheck/build checks, then the repository-required backend and frontend gates proportional to the changed areas.

## Delivery

Both issues ship in one branch and one PR. The PR declares the new detail endpoint `exposed`, identifies the reversal endpoint as a changed exposed contract, documents schema and OpenAPI impact, and closes #881 and #882. It stops after PR creation for user review.
