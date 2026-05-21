# Notification MVP Design

> **Status:** Approved for implementation start on 2026-05-21.

## Context

Nerv-IIP already has a platform Notification boundary in `docs/architecture/notification-baseline.md`, but the current mainline has no Notification service, contracts, SDK, Gateway facade, or Console surface. FileStorage MVP is being developed in a separate worktree and is usable only as a weak reference dependency for this phase: Notification can store `fileId` or `resourceRef`, but must not depend on real attachment upload or download behavior.

The platform has stable patterns that Notification should reuse: AppHub/Ops/IAM CleanDDD service layout, PostgreSQL service schema conventions, CAP integration event conventions, Gateway Console facades, IAM-backed permission enforcement, and the Phase 8 Console design system.

## Goal

Build the first Notification vertical slice: submit notification intents, generate in-app user messages and tasks, consume one Ops failure event, query notifications through Gateway, and mark messages as read.

## Non-Goals

1. No email, SMS, enterprise IM, webhook, or provider credentials.
2. No user preference or subscription management.
3. No notification digest, quiet hours, throttling, or merge rules.
4. No attachment upload or binary download integration.
5. No replacement for Ops audit records, Observability alerts, or business alarm rules.
6. No direct Console access to Notification service; Console uses PlatformGateway facade.

## Recommended Approach

Use a dedicated `Notification` CleanDDD service with PostgreSQL persistence and an in-app delivery provider. Accept explicit NotificationIntent requests through a public API and consume `OperationTaskFailedIntegrationEvent` as the first event-driven source. Generate one NotificationMessage per resolved recipient and one NotificationTask when the intent is actionable.

This is preferred over a Gateway-only projection because Notification owns durable delivery, read state, dedupe, and future provider attempts. It is also preferred over waiting for FileStorage completion because the MVP does not need real attachments.

## Architecture

### Service Boundary

Create a new backend service:

```text
backend/services/Notification/
  src/Nerv.IIP.Notification.Domain
  src/Nerv.IIP.Notification.Infrastructure
  src/Nerv.IIP.Notification.Web
  tests/Nerv.IIP.Notification.Domain.Tests
  tests/Nerv.IIP.Notification.Web.Tests
```

Create public contracts and SDK:

```text
backend/common/Contracts/Nerv.IIP.Contracts.Notification
backend/common/Sdk/Nerv.IIP.Sdk.Notification
backend/tests/Nerv.IIP.Contracts.Notification.Tests
```

The service uses `notification` as the PostgreSQL schema and owns its own migrations history table in that schema.

### Domain Model

`NotificationIntent` records a service or external client request to notify people about a platform fact.

Fields:

| Field | Meaning |
| --- | --- |
| `intentId` | Durable intent identifier. |
| `sourceService` | Source boundary such as `ops`, `apphub`, or `business-extension`. |
| `sourceEventType` | Event or intent type, for example `ops.OperationTaskFailed`. |
| `sourceEventId` | Source event or command identifier. |
| `organizationId` | Platform organization context. |
| `environmentId` | Platform environment context. |
| `severity` | `info`, `warning`, `critical`. |
| `intentType` | `message` or `task`. |
| `dedupeKey` | Business idempotency key. |
| `resourceRef` | Optional resource reference for deep links. |
| `title` | User-visible title. |
| `summary` | User-visible summary without secrets. |
| `createdAtUtc` | Creation timestamp. |

`NotificationMessage` is the user-visible in-app message.

Fields:

| Field | Meaning |
| --- | --- |
| `messageId` | Durable message identifier. |
| `intentId` | Parent intent identifier. |
| `recipientRef` | Recipient such as `user:admin` or `role:ops-admin`. |
| `status` | `unread`, `read`, `archived`, `ignored`. |
| `title` | User-visible title. |
| `summary` | User-visible summary. |
| `severity` | Copied from intent for query performance. |
| `resourceRef` | Optional resource reference. |
| `createdAtUtc` | Message creation timestamp. |
| `readAtUtc` | Read timestamp when applicable. |

`NotificationTask` is the actionable entry for approvals, failure handling, or manual confirmation.

Fields:

| Field | Meaning |
| --- | --- |
| `taskId` | Durable task identifier. |
| `messageId` | Related message. |
| `taskType` | `review`, `approve`, `retry`, `acknowledge`. |
| `status` | `open`, `completed`, `cancelled`. |
| `actionRef` | Optional link target or command reference. |

`DeliveryAttempt` records the in-app delivery attempt in this MVP. It exists now so future external providers do not need to remodel message creation.

### Recipient Model

The MVP supports explicit suggested recipient refs only:

```text
user:{userId}
role:{roleCode}
```

Role expansion through IAM can be stubbed behind an interface in this phase. The query path must still treat IAM as the authority: Gateway performs permission checks before forwarding Console requests, and Notification never invents its own user/role truth.

### API Surface

Notification service public API:

```text
POST /api/notifications/v1/intents
GET  /api/notifications/v1/messages
GET  /api/notifications/v1/tasks
POST /api/notifications/v1/messages/{messageId}/read
POST /api/notifications/v1/messages/read-batch
```

Gateway Console facade:

```text
POST /api/console/v1/notifications/intents
GET  /api/console/v1/notifications/messages
GET  /api/console/v1/notifications/tasks
POST /api/console/v1/notifications/messages/{messageId}/read
POST /api/console/v1/notifications/messages/read-batch
```

The facade forwards the bearer token, organization/environment context, correlation ID, and idempotency key.

### Contracts

`Nerv.IIP.Contracts.Notification` owns request and response DTOs for:

1. `SubmitNotificationIntentRequest`
2. `NotificationIntentResponse`
3. `NotificationMessageListResponse`
4. `NotificationTaskListResponse`
5. `MarkNotificationMessageReadResponse`

The SDK only wraps these public DTOs and routes. It does not implement recipient resolution, delivery providers, or read-state policy.

### Event Consumption

The first event consumer handles:

```text
Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent
```

Mapping:

| Source | Notification Field |
| --- | --- |
| `EventId` | `sourceEventId` |
| `EventType` | `sourceEventType` |
| `SourceService` | `sourceService` |
| `OrganizationId` | `organizationId` |
| `EnvironmentId` | `environmentId` |
| `IdempotencyKey` | `dedupeKey` |
| `Payload.OperationTaskId` | `resourceRef.resourceId` |
| `Payload.OperationCode` | `title`/`summary` context |
| `Payload.FailureCode` | `summary` context |

Default suggested recipient for the MVP is `role:ops-admin`.

### Console Surface

Add a compact notifications page and shell indicator:

```text
frontend/apps/console/src/pages/notifications/index.vue
frontend/apps/console/src/composables/useNotifications.ts
```

The page shows unread/all filters, severity, title, summary, created time, resource link text, and a mark-read action. Tasks can be displayed as a tab on the same page. The shell indicator can be a simple unread count fetched from the messages query.

The UI uses existing `@nerv-iip/ui` primitives and the Calm Control Plane blue semantic tokens. No new design system decisions are part of this MVP.

## Data Flow

1. An external caller submits an intent or Ops publishes `OperationTaskFailedIntegrationEvent`.
2. Notification validates organization/environment, source, severity, intent type, title, summary, recipients, and dedupe key.
3. Notification inserts `NotificationIntent` once per dedupe key.
4. Notification resolves explicit recipients and creates messages.
5. Notification creates tasks only for `intentType = task`.
6. Notification writes in-app `DeliveryAttempt` rows with success status.
7. Gateway checks IAM permission and forwards Console notification queries/actions.
8. Console renders messages and marks messages as read through Gateway.

## Persistence

Tables:

```text
notification.notification_intents
notification.notification_messages
notification.notification_tasks
notification.delivery_attempts
notification.processed_integration_events
notification.__EFMigrationsHistory
```

Indexes:

1. Unique intent dedupe: `organization_id`, `environment_id`, `source_service`, `source_event_type`, `dedupe_key`.
2. Message list: `recipient_ref`, `status`, `created_at_utc desc`.
3. Task list: `recipient_ref`, `status`, `created_at_utc desc`.
4. Processed event: `consumer_name`, `event_id`.

All business tables must have table comments and column comments. JSON/text compatibility rules follow AppHub/Ops/IAM schema convention tests.

## Error Handling

1. Duplicate intents return the existing intent result instead of creating more messages.
2. Missing organization/environment, title, summary, source, or recipient refs returns a validation error.
3. Unsupported severity or intent type returns a validation error.
4. Querying or mutating a message outside the caller recipient context returns not found or forbidden according to existing Gateway/IAM facade conventions.
5. Integration event duplicates are ignored after `processed_integration_events` records success.
6. Event processing failures are logged with event ID, event type, correlation ID, organization ID, environment ID, and consumer name.

## Testing Strategy

1. Domain tests cover intent creation, dedupe, message generation, task generation, and read-state transitions.
2. Contract JSON tests cover camelCase names and stable DTO shape.
3. Web tests cover intent submission, list messages, list tasks, mark read, and duplicate intent behavior.
4. PostgreSQL profile tests cover migrations and schema conventions.
5. Gateway tests cover IAM-backed permission enforcement and bearer forwarding.
6. Frontend tests cover notifications composable and page rendering with mock API responses.

## Acceptance Criteria

1. `dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj` passes.
2. `dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj` passes.
3. `dotnet test backend/tests/Nerv.IIP.Contracts.Notification.Tests/Nerv.IIP.Contracts.Notification.Tests.csproj` passes.
4. `dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj` passes after Gateway facade changes.
5. `pnpm -C frontend typecheck` and focused notification tests pass after Console changes.
6. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore` passes after AppHost wiring.

## Merge Coordination

Avoid editing FileStorage MVP files in this branch. Delay broad readiness doc edits until implementation is complete to reduce conflicts with the File MVP worktree. The likely conflict files are:

1. `backend/Nerv.IIP.sln`
2. `docs/architecture/implementation-readiness.md`
3. `docs/superpowers/plans/2026-05-21-next-stage-stabilization-and-readiness.md`
4. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
