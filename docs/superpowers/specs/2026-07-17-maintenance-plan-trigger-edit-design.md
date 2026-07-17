# Maintenance Plan Trigger Edit Design

## Context

Issue #945 closes the edit-flow gap left by #794 and PR #932. Maintenance plans can already be created and listed with calendar, runtime-hour, or combined triggers, but an existing plan cannot change that trigger configuration through BusinessMaintenance, BusinessGateway, or Business Console.

The issue and the user's execution request are the approved product specification for this design.

## Scope

- Add a narrow BusinessMaintenance update operation for an existing plan's `interval` and `runtimeHourInterval`.
- Expose the operation through BusinessGateway with `business.maintenance.plans.manage`.
- Export the Gateway OpenAPI snapshot and regenerate the business-console API client.
- Add a row-level edit action to the maintenance-plan list and reuse one plan-form dialog for create and edit modes.
- Keep device, plan code, start date, owner, and availability-window facts unchanged by this operation.
- Update facade governance and maintenance product documentation.

## Domain Semantics

`MaintenancePlan.UpdateTriggerConfiguration(interval, runtimeHourInterval)` validates the complete candidate configuration before mutating state. At least one trigger is required; a calendar interval must be a positive ISO-8601 day interval, and a runtime interval must be positive.

Calendar and runtime-hour cursors are independent:

- An unchanged normalized trigger preserves its existing next-due cursor.
- Removing the calendar trigger sets `NextDueOn` to `null` and preserves `LastGeneratedOn`.
- Adding or changing the calendar trigger sets `NextDueOn` to `StartsOn` when no calendar occurrence has been generated; otherwise it sets `NextDueOn` to `LastGeneratedOn + new interval`.
- Removing the runtime trigger sets `NextDueRuntimeHours` to `null` and preserves `LastGeneratedRuntimeHours`.
- Adding or changing the runtime trigger sets `NextDueRuntimeHours` to `LastGeneratedRuntimeHours + new interval`.

This keeps historical generation watermarks intact, prevents already-generated work orders from changing, and prevents an idempotent edit from postponing a due occurrence.

## API and Governance

BusinessMaintenance adds:

```text
PUT /api/business/v1/maintenance/plans/{planId}
operationId: updateMaintenancePlan
permission: business.maintenance.plans.manage
```

BusinessGateway adds:

```text
PUT /api/business-console/v1/maintenance/plans/{planId}
operationId: updateBusinessConsoleMaintenancePlan
permission: business.maintenance.plans.manage
```

The service endpoint is classified `exposed` in `facade-coverage-matrix.json`, with the Gateway operation ID as machine-verifiable evidence. No database migration is required because all trigger and cursor columns are already nullable and governed by the existing constraint.

## Frontend Design

The plans page remains the query and mutation composition surface. A focused `MaintenancePlanFormDialog.vue` owns the shared create/edit form, trigger-mode derivation, field-level validation, and typed submit payload.

Create mode keeps the existing editable device, optional plan code, start date, owner, and trigger fields. Edit mode displays plan identity and start date as read-only context and edits only the trigger mode and its parameters. Mode changes explicitly submit `null` for a removed trigger, so runtime-to-calendar and calendar-to-runtime transitions cannot silently retain the old trigger.

The list gains an `NvRowActions` edit action. A successful update refetches the current scoped plan query before the dialog closes and shows a success toast; a failure keeps the dialog open, shows a human-readable toast, and preserves the user's values.

## Validation and Testing

- Domain tests cover every mode transition, cursor recalculation, unchanged-configuration preservation, and atomic validation failure.
- Command, lock, endpoint-contract, Gateway proxy/auth/OpenAPI, and facade-coverage tests prove both hops and tenant scoping.
- Vue tests drive the visible edit flow, assert three-state prefill, explicit `null` clearing, field-level invalid states, mutation selection, refresh, dialog lifecycle, and toast feedback.
- Required backend solution, frontend typecheck/test/build, touched-file formatting, OpenAPI drift, and facade-coverage gates run before PR creation.

## UX and IA Check

The target user is a maintenance planner or engineer adjusting a stable plan's future trigger policy. A row action is the shortest and most familiar CMMS path; no navigation or tab change is needed. The UI distinguishes immutable plan identity from editable trigger policy and explains that saving recalculates future due points without changing generated work orders.
