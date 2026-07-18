# Leader Demo Sales-to-Delivery Main-Chain Smoke

This runbook is the reproducible evidence entry for MAN-524 / GitHub #965 and the scope input for MAN-518 / GitHub #959. It deliberately separates three evidence grades:

- **runtime-confirmed**: the same run-scoped sales order was observed through public HTTP facade/API responses on the disposable PostgreSQL + Redis full stack;
- **code-confirmed only**: a public endpoint, converter, consumer, or durable reference exists on the tested commit, but this run did not prove the cross-process transition;
- **not verified**: neither a same-order runtime response nor sufficient code evidence was collected.

Service database reads are never a primary business assertion. Similar numbers or unrelated seed rows must not be used as substitutes for the same order.

## Required command and profile

Run only from a clean worktree based on current `origin/main`:

```powershell
git fetch origin
git merge-base --is-ancestor origin/main HEAD
$env:Messaging__Provider = 'Redis'
.\nerv.ps1 fullstack run -Scenario smoke
```

The run must use the managed `fullstack run` lifecycle. Do not use `fullstack start` as acceptance evidence. The session must end in `Stopped`; its PostgreSQL, Redis, process, container, network, and volume ownership is managed by the full-stack manifest.

After the baseline smoke succeeds, execute the public-facade calls during the same managed session through a dedicated scenario or equivalent scenario callback. Use a run suffix such as `MAN524-<UTC timestamp>` for every business number and retain the following evidence beneath `artifacts/fullstack/<session-id>/leader-demo-main-chain/`:

```text
environment.json
requests/<sequence>-<operation>.json
responses/<sequence>-<operation>.json
hops.json
summary.md
```

`environment.json` records the commit, session ID, `Messaging:Provider=Redis`, PostgreSQL/Redis resource names, organization/environment, correlation ID, and start/end timestamps. It must not contain passwords, bearer tokens, connection strings, or authorization headers.

## Side effects and cleanup

The scenario creates disposable master data and business documents only inside the session-owned PostgreSQL databases and sends integration events through the session-owned Redis Streams transport. It writes redacted artifacts under the session artifact directory. `fullstack run` must remove the exact session-owned runtime resources on success or failure and retain diagnostics. No customer/shared database is allowed.

## Hop evidence contract

Each row in `hops.json` must contain:

```json
{
  "node": "delivery-order",
  "sourceObject": "SO-MAN524-...",
  "downstreamObject": "DO-MAN524-...",
  "stableKey": "salesOrderNo + deliveryOrderNo",
  "automationMode": "automatic|manual|unknown",
  "requestEvidence": "requests/..json",
  "responseOrLogEvidence": "responses/..json",
  "conclusion": "runtime-confirmed|gap|not-verified",
  "demoWording": "...",
  "responsibilityIssue": "MAN-... / #... or null"
}
```

Manual continuation is permitted only to probe later independent hops after an earlier gap. It must preserve the same sales-order reference and be labeled `manual`; it is not evidence of automatic flow.

## Current run: 2026-07-18, commit `f7e718a855d6af0ad776c5fe8b8f0182ec2a3de7`

Baseline proof and attempts:

- `git fetch origin` resolved `origin/main` to the exact commit above.
- `git merge-base --is-ancestor origin/main HEAD` returned exit code `0`.
- Command: `$env:Messaging__Provider='Redis'; .\nerv.ps1 fullstack run -Scenario smoke`.
- Attempt 1, session `nerv-f667-1c484b`: Aspire CLI timed out after 300 seconds while the AppHost build was still producing project DLLs. No compiler error was emitted and no business service/API assertion ran. Primary logs are `C:\Users\hp\.aspire\logs\cli_20260718T125517_4498cbe3.log` and `C:\Users\hp\.aspire\logs\cli_20260718T125524822_detach-child_bf69176dfe0043bfb93783f85c3d9edb.log`.
- Attempt 2, session `nerv-f667-90c369`: the warmed build started Aspire after about four minutes. The governed run subsequently waited IAM, Business Master Data, Gateway, BusinessGateway, Console, and Business Console to healthy. The outer 900-second execution window expired before the `smoke` scenario produced an HTTP assertion, so this is infrastructure-health evidence only, not main-chain evidence.
- Result: **environment-blocked before business HTTP evidence**. Neither attempt created a run-scoped sales order or observed a business transition.
- Cleanup: `nerv-f667-1c484b state=Stopped` was verified. Attempt 2 entered managed cleanup when its execution window expired; `.\nerv.ps1 fullstack stop -SessionId nerv-f667-90c369` completed that cleanup and reported `state=Stopped remaining=0`, including removal of the session-owned PostgreSQL, Redis, MinIO, and VictoriaLogs volumes.

Because the environment did not reach public HTTP, every node below remains **not runtime-verified**. Code facts are recorded only to make the next run efficient and to prevent stale issue text from becoming a false verdict.

| Hop | Stable correlation expected | Mode from current code | Current evidence grade | Factual conclusion / demo wording | Responsibility |
| --- | --- | --- | --- | --- | --- |
| Sales order → DemandSource | `salesOrderNo` → `DemandSource.SourceReference` | intended automatic | code-confirmed gap; runtime not reached | Current contracts contain no `SalesOrderReleased`, and DemandPlanning has no sales-order consumer. Demo must say “订单尚未自动进入计划”. | Existing MAN-517 / #958 (`demo:blocker`); do not duplicate. |
| DemandSource → MRP run/suggestion | demand source ID/source reference → pegging link → suggestion ID | automatic after explicit MRP run | code-confirmed only | Demand source, MRP run, suggestions and pegging APIs exist; this run did not prove the same order. | No new issue without runtime evidence. |
| MRP suggestion → MES work order | suggestion ID + `sourceDemandReference` + accepted downstream document ID | manual accept, automatic downstream creation | code-confirmed only | DemandPlanning's HTTP bridge creates a MES work order and stores `AcceptedDownstreamDocumentId`; MES persists `SourceDemandReference`. | No new issue without runtime evidence. |
| MES work order → APS plan | work order/source demand reference → schedule problem/plan assignment IDs | unknown | not verified | Scheduling owns plans and MES owns execution; a stable same-order bridge must be demonstrated, not inferred from separate endpoints. Demo says “排程关联待真实栈确认”. | Search/create only after runtime-confirmed gap. |
| APS plan → MES released execution | plan ID/version → MES work order/operation task | intended release command | code-confirmed only | ADR 0014 requires only released plans to affect MES. This run did not observe the transition. | Search/create only after runtime-confirmed gap. |
| MES operation task → production report | work order ID + operation task ID + report number | manual reporting | code-confirmed only | Public facade exposes operation execution and report read/write; no same-order response was collected. | No new issue without runtime evidence. |
| Production report → Quality result/hold | report/source document ID → inspection task/record/NCR/hold | event-driven plus manual inspection | code-confirmed only | MES has Quality result/NCR consumers and public related-quality/hold reads; no same-order result was collected. | No new issue without runtime evidence. |
| Production report → finished-goods receipt request | work order ID + produced lot + receipt request number | manual request after production | code-confirmed only | MES exposes finished-goods receipt requests with produced-lot provenance. | No new issue without runtime evidence. |
| Finished-goods receipt → Inventory lot/balance | receipt request number/idempotency key + produced lot → inventory movement | automatic event | code-confirmed only | MES converts `FinishedGoodsReceiptRequested` to Inventory movement and consumes posted/failed results; no PostgreSQL/Redis runtime observation was collected. | No new issue without runtime evidence. |
| Sales order → DeliveryOrder | sales-order number + sales-order line number → delivery-order number | manual release | code-confirmed only | ERP public facade releases a DeliveryOrder; no same-order response was collected. | No new issue without runtime evidence. |
| DeliveryOrder → WMS outbound | delivery-order ID/number + sales-order number → WMS source document | automatic event | code-confirmed only | Current main is newer than the issue description: ERP converts `DeliveryOrderReleased` to `WmsOutboundOrderRequested`, and WMS has a CAP consumer. Demo may say “代码链已存在，真实 Redis 复验未完成”, not “已验收”. | Do not create the stale duplicate gap until runtime says otherwise. |
| WMS outbound complete → AR | WMS outbound order/source DeliveryOrder → `AccountReceivable.SourceDocumentNo` | automatic event | code-confirmed only | ERP has `WmsOutboundOrderCompleted...CreateAccountReceivable` and a by-source AR read. No same-order event was observed. | Search/create only after runtime-confirmed gap. |
| AR → journal voucher | receivable number/source document → generated voucher | automatic in same ERP command | code-confirmed only | `CreateAccountReceivableCommandHandler` adds `FinanceVoucherFactory.ForAccountReceivable(receivable)` in the same unit of work. No public runtime response was collected. | No new issue without runtime evidence. |

## Completion rule

MAN-524 can use `Fixes #965` only after a managed Redis/PostgreSQL run records public request/response or service-log evidence for all rows, creates deduplicated issues for every runtime-confirmed gap, and posts the stable-node/unlinked-node list to MAN-518. Until then the PR must remain Draft and use `Refs #965`.

## MAN-518 input from this blocked run

The only safe first-version scope input is:

1. Treat SalesOrder → DemandSource as unlinked and point to MAN-517 / #958.
2. Do not yet mark any other node runtime-linked merely because endpoints or consumers exist.
3. Preserve explicit “尚未建立关联 / 真实 Redis 复验未完成” wording per node.
4. Re-run this document's command after the AppHost can complete startup within the governed timeout, then replace code-only grades with public-HTTP runtime evidence.
