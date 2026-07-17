# Connector observability and tag manifest design

Date: 2026-07-17

Issues: #947, #951

Parent acceptance: #796

## Context

PR #929 delivered the read-only collection-health wall for #796, but two requirements still have no authoritative fact source:

1. the platform cannot list every tag configured for a connector, especially a tag that has never produced a sample;
2. the platform cannot distinguish field-connection loss from Connector Host process loss, collector failure, or quiet/stale sample data within the ten-second UI polling contract.

These are not independent UI gaps. They expose a missing connector identity, configuration inventory, and runtime-state model across Connector Host, AppHub, IndustrialTelemetry, BusinessGateway, and Business Console.

The industry-source analysis in [connector coverage and health industry standards](../../research/2026-07-17-connector-coverage-and-health-industry-standards.md) is normative input to this design. OPC UA separates MonitoredItems, connection/session keep-alive, and DataValue quality; Sparkplug separates Birth metric inventory, node/device lifecycle, and Data messages; MQTT separates Network Connection, Session State, and application payload. Nerv-IIP will preserve the same separation.

## Goals

- Establish one stable collection-connector identity across registration, health, tag manifests, samples, gateway reads, and UI routes.
- Report the current configured tag manifest, including enabled tags that have never sampled and tags whose subscription or activation failed.
- Persist field connection state independently of host heartbeat, collector health, and sample freshness.
- Make a field disconnect observable through BusinessGateway within one complete ten-second UI polling period.
- Preserve protocol and storage compatibility for older Connector Hosts and existing rows.
- Close #947 and #951 in one PR without moving industrial semantics into platform services.

## Non-goals

- A Connector Host configuration CRUD console.
- Editing OPC UA nodes, Modbus registers, or MQTT topic mappings from Business Console.
- Long-term connection-event history or a general-purpose historian for health transitions.
- Replacing AppHub's generic application-instance or heartbeat model.
- Guessing connector identity for historical samples or inventing mappings from device-control bindings.

## Decision 1: canonical collection-connector identity

`collectionConnectorId` is the stable identity of one configured industrial collection connector within an organization and environment.

- Connector Host configuration supplies it explicitly.
- The corresponding AppHub `ApplicationInstance.InstanceKey` equals it.
- `ConnectorCollectionHealth.ConnectorId` equals it.
- Every tag-manifest report and telemetry sample carries it.
- BusinessGateway and Business Console use it as the route/query identity.
- Existing `sourceConnector` remains provenance and backward-compatible diagnostic text; it is not a relational join key.

The tuple `(organizationId, environmentId, collectionConnectorId)` is unique. Protocol adapters may provide the current derived ID as a compatibility default, but a configured ID must not change across restarts. A deliberate ID change creates a new connector identity rather than silently merging history.

## Decision 2: configured coverage is a manifest, not a sample inference

Connector Host reports a replace-style `ConnectorTagManifest` to IndustrialTelemetry through a telemetry-specific ingestion endpoint. AppHub does not own this manifest because tag and protocol-mapping semantics belong to the IndustrialTelemetry boundary.

The manifest contains:

- `collectionConnectorId`;
- `sourceSystem` (`opcua`, `modbus`, or `mqtt`);
- deterministic `manifestRevision` and `reportedAtUtc`;
- one entry per configured mapping with `deviceAssetId`, `tagKey`, `enabled`, optional protocol address metadata, and activation result;
- activation result fields: `pending | active | error | disabled`, `observedAtUtc`, and a sanitized optional error code/message.

The revision is stable for identical configuration, so restart/retry is idempotent. A newer manifest supersedes the prior current manifest. Entries omitted from the newer revision become retired rather than being confused with active coverage.

IndustrialTelemetry persists the current binding projection. The current connector coverage list includes configured entries even when no sample exists. `firstSampleAtUtc` and `lastSampleAtUtc` are nullable facts joined from samples. The UI labels an active entry with no sample as `never sampled`; an activation failure is shown separately from freshness.

If an older Connector Host has never reported a manifest, the API returns `manifestStatus=unavailable`, not an empty list. Existing samples are not parsed or guessed into configured coverage.

## Decision 3: health is four independent axes

The model keeps these facts separate:

| Axis | Owner and source | Minimum state |
|---|---|---|
| Host liveness | AppHub heartbeat | last heartbeat, reachable/timeout |
| Field connection | Connector adapter and AppHub projection | `unknown | alive | lost`, observed/since timestamps, reason category |
| Collector health | Connector collection loop | reported/health status, counters, last success/error |
| Tag freshness | IndustrialTelemetry samples | `never | current | stale | bad`, first/last sample and quality |

`ConnectorConnectionState` is an optional additive object in the Connector Protocol collection-health payload:

- `status`;
- `observedAtUtc`;
- `connectedSinceUtc` when alive;
- `disconnectedSinceUtc` when lost;
- optional bounded `reasonCategory` and sanitized diagnostic code.

Transition rules:

- `unknown` means no authoritative connection observation yet.
- `alive` is recorded only after the protocol-specific connection is usable: OPC UA SecureChannel/Session establishment, MQTT CONNACK and required subscription acknowledgement, or Modbus TCP establishment plus a successful protocol transaction.
- `lost` is recorded on explicit transport/session/broker loss or expiry of the configured connection-detection budget.
- A downstream IndustrialTelemetry write failure, invalid payload, missing mapping, historical reconnect count, or sample silence must not set `lost`.
- Each transition advances `observedAtUtc`; AppHub rejects an older observation overwriting a newer one.
- Recovery starts a new `connectedSinceUtc` interval. A loss preserves its actual `disconnectedSinceUtc`.

AppHub's collection-health projection stores the latest connection state. Read-model precedence is:

1. explicit field `lost` => `stale/offline`, duration from `disconnectedSinceUtc`;
2. Connector Host heartbeat timeout => `stale/offline`, duration from the host-liveness deadline;
3. fresh connection plus collector terminal failure => `stale/fault`;
4. otherwise expose the independent raw axes without deriving connection state from sample activity.

Older reports without connection state retain the existing conservative fallback and never fabricate an explicit `alive` or `lost` fact.

## Decision 4: reporting is independent and transition-driven

The current single worker serializes collection, status reporting, and Ops, then delays for a configured cycle. That structure cannot provide a ten-second contract because one slow or retrying collector delays every heartbeat and state report.

Connector Host will separate these responsibilities:

- one collection execution task per enabled connector, so adapters do not block each other;
- a serialized reporting worker that sends periodic heartbeat/state refreshes and is awakened immediately by connection or manifest transitions;
- an independent Ops polling worker;
- registration/token refresh remains ordered before heartbeat and state reporting for each target.

Protocol adapters publish connection and manifest changes through bounded in-process signals. Signals coalesce repeated identical state, and the reporting worker remains the single writer to preserve ordering.

The configured timing budget is:

- field connection detection: at most four seconds;
- transition signal, AppHub reporting, and persistence: at most two seconds;
- Gateway query allowance: at most two seconds;
- total backend deadline: at most eight seconds;
- Business Console polls every ten seconds.

Protocol client timeouts/keep-alives must be validated against the detection budget. “Within one polling period” means that a disconnect immediately after a confirmed online poll is observable no later than the poll ten seconds later; it does not claim that a request already in flight before the physical disconnect can observe the future event.

Host-process loss remains a distinct path. Its heartbeat cadence and timeout become configuration-driven and must fit the same product deadline when the Host-loss scenario is tested.

## Data flow

### Startup and configuration

1. Connector Host loads a connector configuration with explicit `collectionConnectorId`.
2. It registers the same identity as the AppHub instance.
3. It computes and reports the current `ConnectorTagManifest` to IndustrialTelemetry.
4. IndustrialTelemetry idempotently replaces the current manifest projection and retains retirement facts for removed entries.
5. Activation attempts update each manifest entry's activation result without removing failed or never-sampled entries.

### Runtime connection transition

1. The adapter detects `alive` or `lost` using protocol-specific connection mechanisms.
2. It records a monotonic observation and wakes the reporting worker.
3. Connector Host sends the additive connection-health payload to AppHub.
4. AppHub persists the latest observation and exposes it through the existing collection-health endpoints.
5. BusinessGateway forwards the explicit facts; the UI does not infer field connectivity from heartbeat, counters, or sample timestamps.

### Samples and coverage query

1. Every telemetry sample carries the canonical `collectionConnectorId` in addition to existing provenance.
2. IndustrialTelemetry records the sample without creating or modifying configured coverage from sample presence alone.
3. The connector-tag query starts from the current manifest and left-joins sample aggregates for first/last sample and quality.
4. Business Console loads the query lazily when a connector card expands.

## Storage and contracts

### AppHub

Add nullable columns to `connector_collection_health`:

- `connection_status`;
- `connection_observed_at_utc`;
- `connected_since_utc`;
- `disconnected_since_utc`;
- bounded optional reason fields.

Null means an older protocol report or no authoritative observation.

### IndustrialTelemetry

Add a connector manifest root and current binding projection scoped by organization, environment, and collection connector. Binding uniqueness is `(organizationId, environmentId, collectionConnectorId, deviceAssetId, tagKey)`.

Add nullable `collection_connector_id` to raw/sample facts that support the latest-sample join and add an index beginning with organization, environment, and connector identity. Existing rows remain null and are not backfilled from `source_connector`.

The existing tags endpoint remains the generic tag catalog. A dedicated connector-coverage endpoint exposes manifest semantics so “configured coverage” is not confused with “all tags on a device”. It is declared `exposed` in `facade-coverage-matrix.json` and delivered through BusinessGateway, OpenAPI export, generated client, and the stable barrel in the same PR.

## Business Console behavior

The collection-health card keeps the four axes visible:

- connection badge and connected/disconnected duration;
- collector health and counters;
- configured/active/ever-sampled/fresh/error counts;
- last connector-level sample time.

Expanding a card loads the current manifest and shows each configured tag with device, tag key, enabled state, activation result, and nullable last-sample time. Distinct states are rendered for:

- manifest unavailable because the Host uses the old protocol;
- valid manifest with no configured entries;
- configured but disabled;
- activation error;
- active but never sampled;
- sampled and current/stale/bad.

The page does not parse connector IDs, fall back to device-control bindings, or derive connection status from sample silence.

## Compatibility and migration

- All Connector Protocol additions are optional and appended compatibly within protocol major version 1.
- Old Hosts continue to report legacy collection health; UI renders connection fact as unknown/fallback.
- New Hosts report manifests on startup, configuration change, activation-result change, and explicit platform rebirth/request.
- Existing sample and collection-health rows remain valid with null new fields.
- No historical mapping or connection timestamp is fabricated.
- Deployment documentation identifies the minimum Connector Host version required for full manifest and connection-state support.

## Verification

### Contract and domain tests

- Canonical identity is identical in registration, health, manifest, samples, and queries.
- Manifest retries with the same revision are idempotent; a newer revision retires removed bindings.
- An enabled configured tag with no sample is returned as `never sampled`.
- Activation failure remains visible and is not treated as connection loss.
- Multiple connectors may cover the same tag without merging their bindings or latest-sample facts.
- Organization/environment isolation is enforced in every write and read.
- Older reports and rows preserve conservative fallback behavior.
- Out-of-order connection observations cannot revive stale state.

### Protocol adapter tests

- OPC UA, Modbus, and MQTT each cover initial connection, explicit loss, recovery, and non-connection errors.
- Protocol-specific timeout/keep-alive configuration cannot exceed the product detection budget in the governed profile.
- One connector's retry or slow call cannot delay another connector's heartbeat/state report.

### End-to-end timing test

The stable CI proof uses a real loopback Modbus TCP socket without cross-solution implementation references:

1. start the platform services, Connector Host, and simulator;
2. wait for an explicit `alive` Gateway response and a current manifest;
3. close the accepted socket while leaving Connector Host running;
4. poll the BusinessGateway facade until the ten-second deadline;
5. assert explicit `lost`, `stale/offline`, a bounded `disconnectedSinceUtc`, and a still-fresh Host heartbeat;
6. restore the simulator and assert a new alive interval;
7. separately prove a configured-but-never-sampled tag remains in the expanded coverage list.

OPC UA simulator and MQTT broker disconnect tests remain protocol-specific integration evidence. Business Console fake-timer tests verify the ten-second refresh and every manifest/health display state.

## Documentation and PR scope

The implementation PR updates the connector protocol, database schema catalog, readiness, facade coverage, API/codegen documentation, frontend navigation description, and the standards note. The PR closes #947 and #951 and references #796. Product documentation is affected because the collection-health page gains authoritative connection and configured-tag details.

No unrelated connector configuration CRUD, control command, or general monitoring redesign is included.
