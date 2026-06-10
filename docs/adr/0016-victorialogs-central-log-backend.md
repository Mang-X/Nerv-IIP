# ADR 0016: VictoriaLogs Central Log Backend

- Status: Accepted
- Date: 2026-06-10

## Context

Nerv-IIP already emits structured JSON logs through Serilog Console, local rolling files and OpenTelemetry OTLP. Aspire Dashboard remains the local short-term diagnostics UI, but it is not a persistent log store. The platform needs a logs-only backend for centralized storage and controlled retrieval without introducing a metrics or traces backend in this slice.

VictoriaLogs is the selected backend for the first centralized log storage path. Official VictoriaMetrics documentation states that VictoriaLogs is provided under Apache License 2.0, supports OTLP log ingestion at `/insert/opentelemetry/v1/logs`, exposes LogsQL queries through `/select/logsql/query`, and uses `-retentionPeriod` for retention configuration. The current official VictoriaLogs release verified for this decision is `v1.50.0`.

## Decision

1. Add VictoriaLogs as the default logs-only centralized backend for local AppHost and Compose-based deployments.
2. Pin the runtime image to `victoriametrics/victoria-logs:v1.50.0`; do not use `latest`.
3. Configure persistent storage through a dedicated volume and pass `-storageDataPath` and `-retentionPeriod` explicitly.
4. Route service OTLP logs to VictoriaLogs with the OTLP/HTTP logs endpoint path `/insert/opentelemetry/v1/logs`.
5. Keep metrics and traces on the existing OpenTelemetry/Aspire Dashboard path. Do not add a metrics or traces backend as part of this ADR.
6. Expose log search only through PlatformGateway facade APIs. Frontend code must not call VictoriaLogs, LogsQL, Collector or Aspire Dashboard directly.
7. Provide a small `Nerv.IIP.Observability` VictoriaLogs client and safe query builder that maps platform filters to LogsQL. The query facade supports service, correlationId, traceId, time range and level filters.
8. Do not store log message bodies in AppHub, IAM, Ops, FileStorage, Notification or business PostgreSQL schemas. PostgreSQL may later hold observability indexes or metadata in a separate `observability` schema, but this slice stores searchable log bodies in VictoriaLogs.

## Consequences

- AppHost is the topology source for the VictoriaLogs container. Legacy Compose files may include the same service for migration and release rehearsals, but must not become a second full platform topology.
- `VictoriaLogs:BaseUrl` configures Gateway log query access; `OpenTelemetry:Logs:Endpoint` and `OpenTelemetry:Logs:Path` configure service log ingestion.
- The first Gateway API is `POST /api/console/v1/logs/query` with `operationId=queryConsoleLogs` and permission `observability.logs.read`.
- The backend API is available for UI integration. This ADR does not require a Console log viewer UI in the same slice.
- Offline and air-gapped deployment preparation must include the pinned VictoriaLogs image, its Apache License 2.0 notice, the configured persistent volume and the selected retention period.
