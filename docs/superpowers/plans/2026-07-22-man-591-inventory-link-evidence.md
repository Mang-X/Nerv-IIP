# MAN-591 Inventory Link Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the MAN-524 leader-demo main-chain acceptance wait for asynchronous finished-goods Inventory posting and prove the exact MES receipt-to-Inventory association through the public `inventory-link` facade.

**Architecture:** Keep all assertions at the authenticated public BusinessGateway HTTP boundary. Reuse the scenario's bounded polling helper for both exact Inventory availability and the receipt-scoped `inventory-link`, then record final request summaries, correlation IDs, polling metadata, movement IDs, balance keys, request number, work order, SKU, site, location, and produced lot in the evidence ledger.

**Tech Stack:** TypeScript 6, Playwright, Vitest, Vite Plus, PowerShell full-stack harness, Aspire, PostgreSQL, Redis Streams.

## Global Constraints

- Only GitHub #1063 / Linear MAN-591 is in scope.
- Do not change MES, Inventory, BusinessGateway, OpenAPI, generated clients, schemas, or business behavior.
- Do not read service databases as business evidence and do not treat HTTP 200 alone as posting success.
- If the real run exposes a new business defect, create a separate GitHub/Linear issue and do not fix it in this branch.
- The PR must be ready for review, include `Fixes #1063`, link MAN-591, and must not be merged by the implementing agent.

---

### Task 1: Lock the evidence contract with failing tests

**Files:**
- Modify: `frontend/apps/business-console/src/leaderDemoMainChain.contract.test.ts`
- Test: `frontend/apps/business-console/src/leaderDemoMainChain.contract.test.ts`

**Interfaces:**
- Consumes: `sourceBetween(start, end)` and `expectScopedQuery(source, endpoint)` from the existing source-level contract test.
- Produces: contract coverage requiring `pollData` for exact finished-goods availability, the public `inventory-link` route with `workOrderId`, source/movement/balance validation, and removal of the accepted #972 gap exception.

- [ ] **Step 1: Add the bounded Inventory polling contract**

Add a test that slices the finished-goods receipt flow and asserts it calls:

```ts
const availability = await pollData(
  '/api/business-console/v1/inventory/availability',
  {
    organizationId,
    environmentId,
    skuCode: finishedSku,
    uomCode,
    siteCode: finishedGoodsSiteCode,
    locationCode: finishedGoodsLocationCode,
    lotNo: producedLotNo,
  },
  (data) => Number(data.onHandQuantity ?? 0) > 0,
)
```

The same test must reject the old `pollRows(..., () => false, 1)` plus immediate fallback shape and require final evidence to include polling metadata.

- [ ] **Step 2: Add the public inventory-link contract**

Add a test requiring the receipt flow to call:

```ts
`/api/business-console/v1/mes/finished-goods-receipt-requests/${encodeURIComponent(receiptRequestNo)}/inventory-link`
```

through `pollData`, with `{ organizationId, environmentId, workOrderId }`, and to validate `linkStatus`, `isInventoryLinkEstablished`, exact request/work-order/lot/source keys, a positive source movement, and a positive source balance. Require the lookup node to be `runtime-confirmed`, and require the final acceptance assertion to reject every non-runtime-confirmed node without a special #972 exception.

- [ ] **Step 3: Run the focused test and verify RED**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console exec vitest run src/leaderDemoMainChain.contract.test.ts
```

Expected: the new tests fail because the scenario still uses the one-shot availability fallback, never calls `inventory-link`, and accepts the hard-coded #972 gap.

### Task 2: Implement bounded public evidence collection

**Files:**
- Modify: `frontend/apps/business-console/e2e/leader-demo-main-chain.spec.ts`
- Test: `frontend/apps/business-console/src/leaderDemoMainChain.contract.test.ts`

**Interfaces:**
- Consumes: `call`, `queryPath`, `pollData`, `publicJson`, `EvidenceEntry`, the `getBusinessConsoleMesFinishedGoodsReceiptInventoryLink` public wire shape, and canonical MES finished-goods Inventory coordinates `finished-goods` / `receiving`.
- Produces: `finished-goods-receipt-inventory-posting` and `inventory-produced-lot-fulfillment-lookup` evidence entries whose successful conclusion is based on public payload facts rather than status code alone.

- [ ] **Step 1: Make polling evidence auditable**

Extend `pollData` with an attempt counter and elapsed time. Its successful return must include:

```ts
poll: {
  attempts,
  elapsedMs: Date.now() - startedAt,
  timeoutMs,
}
```

and its timeout error must include attempts and timeout. Existing callers may ignore the extra `poll` field.

- [ ] **Step 2: Replace one-shot finished-goods availability with bounded polling**

Define:

```ts
const finishedGoodsSiteCode = 'finished-goods'
const finishedGoodsLocationCode = 'receiving'
```

Use `pollData` with the exact run-scoped SKU, UOM, canonical site/location, and produced lot. Accept only positive on-hand. Record the final public request summary with correlation ID plus `{ poll, availability }` in `finished-goods-receipt-inventory-posting`.

- [ ] **Step 3: Poll and validate the real inventory-link facade**

Call the receipt-scoped public endpoint with the same `receiptRequestNo` and `workOrderId`. Poll until `linkStatus` is no longer `notPosted`, then fail closed unless all of these hold:

```ts
linkStatus === 'posted'
isInventoryLinkEstablished === true
requestNo === receiptRequestNo
workOrderId === workOrderId
producedLotNo === producedLotNo
sourceService === 'business-mes'
sourceDocumentId === receiptRequestNo
sourceDocumentLineId === workOrderId
```

Require at least one movement and balance matching the same SKU, site, location, and lot with positive quantity/on-hand. Record the final public request summary, correlation ID, poll metadata, movement ID, balance ledger version, and sanitized link payload in `inventory-produced-lot-fulfillment-lookup`.

- [ ] **Step 4: Remove the legacy accepted gap**

Delete the hard-coded `#972 / MAN-528` gap entry and the final exception that allowed it. The final assertion must require all fifteen nodes to be `runtime-confirmed`.

- [ ] **Step 5: Run the focused test and verify GREEN**

Run the Task 1 command again. Expected: all `leaderDemoMainChain.contract.test.ts` tests pass.

- [ ] **Step 6: Run the affected frontend gate**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
pnpm -C frontend exec vp fmt --check apps/business-console/e2e/leader-demo-main-chain.spec.ts apps/business-console/src/leaderDemoMainChain.contract.test.ts
```

Expected: all commands exit 0. If the repository-wide formatter reports unrelated baseline failures, the two touched files must still pass this individual check.

### Task 3: Real-stack final verification and ready PR

**Files:**
- Inspect: `artifacts/fullstack/<session>/leader-demo-main-chain-evidence.json`
- Inspect: the exact full-stack session manifest and cleanup result emitted by the harness
- Modify only if contract facts require correction: `frontend/apps/business-console/e2e/leader-demo-main-chain.spec.ts`

**Interfaces:**
- Consumes: `nerv.ps1 fullstack run -Scenario leader-demo-main-chain`, PostgreSQL persistence profile, Redis cross-process messaging, public BusinessGateway routes, and the generated evidence ledger.
- Produces: a run-scoped evidence artifact, exact cleanup proof, and a ready GitHub PR linked to MAN-591 and fixing #1063.

- [ ] **Step 1: Run the complete real stack**

Run from the repository root:

```powershell
.\nerv.ps1 fullstack run -Scenario leader-demo-main-chain
```

Expected profile facts: PostgreSQL persistence and Redis transport. Expected business result: all fifteen evidence nodes are `runtime-confirmed`, including the receipt posting and inventory-link lookup.

- [ ] **Step 2: Audit evidence and cleanup**

Inspect the exact run's evidence JSON and manifest. Verify the receipt, work order, produced lot, movement ID, balance key, request correlation IDs, polling attempts/elapsed time, and link status are present and contain no credential. Verify final session state `Stopped`, `remaining=[]`, `errors=[]`, and no session-labeled Docker containers, networks, or volumes remain.

- [ ] **Step 3: Register any newly exposed business defect without fixing it**

If the first non-runtime-confirmed node is caused by a service/business contract rather than the evidence orchestrator, search GitHub and Linear for an existing owner. Reuse it if found; otherwise create one narrow GitHub issue and its synced/reused Linear issue with the run ID, baseline SHA, first failing public request, sanitized response/correlation ID, and stable keys. Do not change service code in this branch.

- [ ] **Step 4: Review the final diff and rerun deterministic verification**

Run `git diff --check`, the focused contract test, business-console typecheck/test/build, and individual formatting again. Confirm the diff contains only the plan and the two leader-demo evidence files, with no generated or backend changes.

- [ ] **Step 5: Commit and publish a ready PR**

Commit with a narrow message such as `test(demo): prove finished-goods inventory linkage`, push `codex/man-591-inventory-link-evidence`, and create a non-draft PR targeting `main`. The body must include the root cause, bounded-polling and inventory-link changes, deterministic and real-stack verification, exact session/evidence paths, cleanup state, product docs impact (`文档：无影响`), contract/schema/facade impact (`无；消费既有 exposed facade`), `Fixes #1063`, and the MAN-591 URL. Stop after PR creation and wait for review.
