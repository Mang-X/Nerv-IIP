# ProductEngineering Release Version Identity Design

## Context

The public MBOM and routing release endpoints currently return only an aggregate code in
`data.id`. Production-version creation immediately expects `mbomVersionId` and
`routingVersionId` in the repository's canonical `code:revision` form. A caller therefore
cannot chain release into production-version creation without guessing how to construct a
version identity. The governed leader-demo run exposed this mismatch as GitHub #1024 /
Linear MAN-564.

## Decision

MBOM and routing release operations receive a dedicated response contract:

```json
{
  "data": {
    "id": "MB-M524-20260720161215",
    "versionId": "MB-M524-20260720161215:A"
  }
}
```

`id` remains the aggregate code for backward compatibility. `versionId` is the stable,
revision-qualified identity accepted directly by production-version creation. Other
ProductEngineering writes keep the existing generic entity response because documents,
items, engineering changes, and similar operations do not share this version-reference
meaning.

## Architecture and data flow

1. The ProductEngineering release command result carries both the allocated aggregate code
   and the canonical released-version identity. Both fresh and idempotent paths produce the
   same contract.
2. The MBOM and routing FastEndpoints return the dedicated versioned response without
   changing their routes, authorization policies, or operation IDs.
3. BusinessGateway uses a matching response model for only these two facade operations and
   forwards the ProductEngineering response unchanged.
4. The BusinessGateway OpenAPI snapshot and generated TypeScript client expose `versionId`.
5. The leader-demo scenario passes `mbom.versionId` and `routing.versionId` directly into
   production-version creation. Missing version identities fail locally with an explicit
   contract error instead of falling back to object stringification.

## Compatibility and scope

The JSON change is additive for the two release endpoints. Existing consumers that read
`data.id` continue to work. No route, permission, database schema, migration, domain event,
or facade-coverage classification changes. This issue does not repair any later
sales-to-delivery failure exposed after production-version creation.

## Verification

- ProductEngineering contract coverage proves MBOM and routing release results contain the
  exact `code:revision` identities and that those returned values create a production
  version without caller-side construction.
- BusinessGateway contract coverage proves the two version identities survive the facade
  and are used verbatim in the next production-version request.
- OpenAPI/codegen verification proves the public generated client exposes `versionId`.
- `./nerv.ps1 fullstack run -Scenario leader-demo-main-chain` must pass the
  production-version checkpoint. Any next earliest failure is recorded separately, with
  the governed session cleaned up and no fix bundled into this PR.
