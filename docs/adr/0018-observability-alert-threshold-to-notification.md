# ADR 0018: Observability Threshold Alerts to Notification

- Status: Accepted
- Date: 2026-07-05

## Context

Nerv-IIP already emits OpenTelemetry metrics and has a logs-only VictoriaLogs backend. VictoriaLogs does not provide the platform's metrics alert rule engine, and ADR 0016 explicitly kept metrics and traces outside that slice. Operators still need a default private-deployment path for service health failures, CAP/DLQ backlog, Connector Host heartbeat staleness and PostgreSQL resource watermarks to reach Notification instead of relying on dashboard watching.

The platform also already has Notification external delivery providers, preferences, subscriptions, dedupe and delivery attempts. Notification should deliver alert notifications, but it must not become the owner of observability rule semantics.

## Decision

Use a built-in lightweight threshold scanner for the first alerting slice. Do not introduce vmalert yet.

The scanner runs in the Notification process for this slice because there is no separate Observability service yet. Its rule namespace and source events remain `observability.*`, and its only side effect is submitting Notification intents. Rule ownership stays with Observability configuration and deployment artifacts; Notification continues to own delivery, preferences, dedupe, silent-window suppression and resolved notification delivery.

The first baseline rules are:

1. service health failure via configured `/health` endpoints;
2. CAP/DLQ actionable backlog via the existing Notification dead-letter store metrics;
3. Connector Host heartbeat staleness via AppHub internal instance queries;
4. PostgreSQL connection usage and database-size watermarks via PostgreSQL system views/functions.

Rules are configured under `Observability:Alerts`. AppHost and Compose carry a default baseline rule set for single-machine private deployments. The scanner submits `observability.AlertFiring` task intents and `observability.AlertResolved` message intents through the existing Notification intent pipeline, using per-rule dedupe windows and silent windows.

## Rationale

vmalert remains the preferred candidate once Nerv-IIP adds a Prometheus-compatible metrics store such as VictoriaMetrics. It is operationally familiar in the VictoriaMetrics ecosystem and has strong rule semantics. In this slice, adding vmalert would also require adding and supporting a metrics backend, scrape topology, alertmanager-compatible routing or a custom webhook bridge, which is too much infrastructure for the current single-machine private baseline.

The lightweight scanner reuses already-owned platform facts and keeps the closure path small:

1. health endpoints are already exposed by platform services;
2. Notification already has DLQ metrics and intent submission;
3. AppHub already owns Connector Host heartbeat facts;
4. PostgreSQL is already the default persistence dependency.

## Consequences

Nerv-IIP now has a default alert-to-Notification closure path without introducing another required runtime component. Operators can receive both firing and resolved notifications through the existing Notification channels.

The scanner is intentionally simple. It is not a full Prometheus rule engine, does not evaluate arbitrary PromQL and does not replace future VictoriaMetrics/vmalert adoption. If metrics retention, PromQL-style expressions, multi-target rule groups or Alertmanager-compatible routing become required, this ADR should be superseded by a VictoriaMetrics metrics + vmalert ADR.

Because the scanner is temporarily hosted by Notification, new rule probes must not mutate Notification domain state directly. They must only collect Observability samples and submit Notification intents through the same public application path used by other producers.

## Alternatives Considered

1. **vmalert now**: rejected for this slice because there is no metrics backend in the current default topology, and adding one would expand #735 into a broader observability platform change.
2. **External customer monitoring only**: rejected because private single-machine deployments need a built-in baseline that works before customers integrate their own monitoring stack.
3. **Notification-specific DLQ worker only**: already exists for Notification DLQ and is too narrow for service health, Connector Host heartbeat and PostgreSQL watermarks.
