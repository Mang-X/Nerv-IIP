# Quality Reinspection History Design

## Scope

GitHub #954 / Linear MAN-516 requires the real Quality write path to produce a
later passed inspection for the same MES source after an earlier rejection. The
change is limited to Quality reinspection modeling, its BusinessGateway facade,
the Quality-to-MES hold lifecycle proof, schema/API governance, and the
team-leader acceptance statement. It does not change MES hold rules, add manual
hold bypasses, or absorb adjacent Quality UI/CAPA work.

## Decision

Add an explicit reinspection command:

`POST /api/business/v1/quality/inspection-records/{inspectionRecordId}/reinspections`

The route target is the immediately preceding inspection record. The first
inspection remains attempt `1`; a reinspection is a new immutable record with
`attempt_number = predecessor.attempt_number + 1` and
`reinspection_of_inspection_record_id = predecessor.id`.

Only rejected or conditionally released records can be reinspected. A passed
record is terminal. To perform another reinspection after a second rejection,
the caller targets that second record. A predecessor may have at most one
direct successor. Replaying the same command against the same predecessor
returns the existing successor, while a deliberate later attempt targets the
latest rejected successor. This preserves the existing initial-create
idempotency without confusing a network retry with a new business attempt.

## Domain and Persistence

`InspectionRecord` remains the immutable result aggregate. Initial creation sets
attempt `1` and no predecessor. Reinspection:

- inherits organization, environment, source type/service/document, SKU,
  inspected quantity, batch/serial, stock-release dimensions, and the exact
  inspection-plan version;
- records fresh result lines, disposition evidence, measuring-device snapshot,
  result, timestamps, and its own result domain event;
- evaluates planned measurements against the original plan version, including
  when that version has since been superseded, because this is continuation of
  the original inspection rather than a new plan selection;
- keeps every predecessor record and NCR link unchanged.

The existing source uniqueness becomes source plus `attempt_number`. A separate
unique index on non-null predecessor ID enforces one direct successor, and a
same-table restrictive foreign key preserves the audit chain. Existing rows
migrate as attempt `1`.

List and detail projections expose `attemptNumber` and
`reinspectionOfInspectionRecordId`, so the history is readable without deriving
business meaning from timestamps.

## Event Semantics and MES Flow

Every reinspection creates a normal `InspectionPassed`,
`InspectionConditionalReleased`, or `InspectionRejected` domain event. The
existing public `InspectionResultIntegrationEvent` wire contract is reused.
Its idempotency key adds `inspectionRecordId`: duplicate delivery of one record
still deduplicates, while distinct attempts from the same source can no longer
collide.

MES keeps its existing fail-closed consumer and `QualityHoldContext` rules:

1. A real Quality initial-create command produces a rejected record and event.
2. The MES consumer resolves the real MES source and applies the hold.
3. A real Quality reinspection command produces a passed record and event.
4. The same MES consumer releases the hold and appends an
   `inspection-released` timeline transition.

The acceptance test must construct neither result event manually nor seed a MES
hold. It may invoke the existing domain-event converters directly to cross the
in-process acceptance seam, as other cross-service acceptance tests do.

## HTTP and Facade Governance

The new service endpoint requires
`business.quality.inspection-records.create` and is classified `exposed`.
BusinessGateway adds the matching
`POST /api/business-console/v1/quality/inspection-records/{inspectionRecordId}/reinspections`
facade with the same permission, forwards organization/environment scope, and
returns the new record ID plus attempt number.

The BusinessGateway OpenAPI snapshot and generated `@nerv-iip/api-client` are
regenerated. No frontend page is added in this issue.

## Failure Behavior

- Missing or cross-scope predecessor: not found.
- Passed predecessor: reject as a known business error.
- Missing/mismatched original plan: fail closed.
- Invalid result lines, missing disposition evidence, or blocked measuring
  device: reuse existing Quality validation.
- Command replay for an already-linked predecessor: return the existing
  successor and do not publish another event.
- MES cannot resolve the source or sees malformed/divergent facts: retain the
  existing persistent dead-letter behavior.

## Verification

Verification covers:

- domain attempt/lineage inheritance and terminal-pass rejection;
- command replay and read projections;
- relational migration/index/foreign-key model;
- event idempotency across distinct attempts;
- service endpoint contract, authorization, facade proxy/authorization/OpenAPI,
  facade coverage, and generated-client drift;
- Quality-to-MES rejected-to-passed apply/release acceptance using real Quality
  commands and real MES consumer;
- Quality/MES targeted suites, BusinessGateway tests, facade coverage, backend
  solution tests when practicable, schema docs, and touched-file formatting.

## Self-review

- No placeholder or deferred implementation remains in this design.
- Initial inspection idempotency and MES fail-closed behavior are preserved.
- The same predecessor cannot ambiguously mean both a retry and a new attempt.
- The event idempotency key is consistent with the new record-per-attempt model.
- The scope is one issue and one ready PR; merge is explicitly excluded.
