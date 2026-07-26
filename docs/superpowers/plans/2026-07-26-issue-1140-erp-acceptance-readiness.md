# Issue 1140 ERP Acceptance Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the ERP sales-order DemandPlanning acceptance wait for the committed ERP source row before its first mutation and reject HTTP 200 business-error envelopes immediately.

**Architecture:** Keep ERP seed, Unit of Work, services, endpoints, and production messaging unchanged. Add a bounded read-only PostgreSQL readiness probe to the governed acceptance harness, then make each state-changing HTTP call execute exactly once through a fail-closed response-envelope validator. Preserve hop-specific diagnostics so source visibility, Redis transport, and DemandPlanning projection failures remain distinguishable.

**Tech Stack:** PowerShell 7, repository script-contract tests, PostgreSQL 18 through the existing governed Docker helper path, Redis 8, GitHub Actions.

## Global Constraints

- Start from `origin/main` on `codex/issue-1140-erp-acceptance-readiness`.
- Do not modify ERP seed, Unit of Work, service code, endpoint code, schema, OpenAPI, generated clients, or facade declarations.
- Production changes are limited to `scripts/verify-erp-sales-order-demand-planning.ps1`, its existing script test, and the focused architecture note.
- The readiness probe is read-only, bounded, and is the only operation allowed to retry.
- Every change-line or cancellation POST is executed once; no state-changing POST retry is permitted.
- HTTP 200 with `success=false`, malformed/missing envelope fields, or missing/unexpected `data` fails immediately with a bounded redacted response diagnostic.
- Diagnostics explicitly distinguish ERP source readiness, Redis transport, and DemandPlanning projection state.
- Script code continues to dot-source `scripts/lib/ScriptAutomation.ps1`; it must not directly invoke `dotnet`, `docker`, `pnpm`, or `pwsh`, and must not define `Write-Error`.
- Do not run `dotnet test backend/Nerv.IIP.sln`.
- The longer-term fix—making service readiness wait for committed seed/UoW completion—remains out of scope and is documented as follow-up governance.

---

### Task 1: Close the committed-source and business-envelope gaps

**Files:**
- Modify: `scripts/tests/erp-sales-order-demand-planning-verify-script.Tests.ps1`
- Modify: `scripts/verify-erp-sales-order-demand-planning.ps1`
- Modify: `docs/architecture/sales-order-to-demand-planning.md`

**Interfaces:**
- Consumes: the existing disposable database name, compose file, `Invoke-NativeCommandOutput`, `Protect-Man517DiagnosticText`, ERP headers, and existing MAN-517 cleanup/diagnostic flow.
- Produces: `Wait-ErpSalesOrderSource` for bounded read-only source readiness and a fail-closed `Invoke-JsonPost` that accepts an expected response data string and returns the validated envelope.

- [ ] **Step 1: Add RED script-contract assertions for ordering and single-shot writes**

  Extend the existing test so it requires:

  ```powershell
  Assert-Contract ($content.Contains('function Wait-ErpSalesOrderSource')) 'Acceptance must poll the committed ERP source row.'
  Assert-Contract ($content.Contains('erp.sales_orders')) 'Source readiness must inspect the ERP-owned source table.'
  Assert-Contract ($content.Contains('erp.sales_order_lines')) 'Source readiness must verify line 10.'
  Assert-Contract ($content.Contains('sourceStage')) 'Failure diagnostics must identify the ERP source stage.'

  $sourceReadyCall = $content.IndexOf('Wait-ErpSalesOrderSource -ComposeFile $composeFile -DatabaseName $databaseName', [StringComparison]::Ordinal)
  $firstChangePost = $content.IndexOf('Invoke-JsonPost -Uri "$erpUrl/api/business/v1/erp/sales-orders/SO-DEMO-001/lines/10"', [StringComparison]::Ordinal)
  Assert-Contract ($sourceReadyCall -ge 0 -and $firstChangePost -gt $sourceReadyCall) 'Committed ERP source readiness must complete before the first change POST.'

  Assert-Contract ($content.Contains('[string]$ExpectedData')) 'State-changing POST validation must require expected business data.'
  Assert-Contract ($content.Contains('SkipHttpErrorCheck')) 'POST validation must inspect bounded HTTP responses itself.'
  Assert-Contract ($content.Contains('$responseEnvelope.success')) 'POST validation must reject a business-error envelope.'
  Assert-Contract ($content.Contains('postResponse')) 'Failure diagnostics must identify the bounded POST response.'
  Assert-Contract (-not $content.Contains('Wait-ErpSalesOrderSource -ComposeFile $composeFile -DatabaseName $databaseName -RetryPost')) 'Readiness must never introduce POST retry.'
  ```

  Also assert all three mutation calls provide their exact expected data:

  ```powershell
  Assert-Contract (($content.Split('-ExpectedData ''changed''').Count - 1) -eq 2) 'Both change POSTs must require data=changed.'
  Assert-Contract (($content.Split('-ExpectedData ''cancelled''').Count - 1) -eq 1) 'The cancellation POST must require data=cancelled.'
  ```

- [ ] **Step 2: Run the existing test and verify RED**

  Run:

  ```powershell
  pwsh -NoProfile -File scripts/tests/erp-sales-order-demand-planning-verify-script.Tests.ps1
  ```

  Expected: non-zero exit on the first missing committed-source or envelope-validation contract, proving current code does not meet #1140.

- [ ] **Step 3: Add the minimal bounded committed-source readiness probe**

  Implement `Wait-ErpSalesOrderSource` in the acceptance script. It must repeatedly execute one read-only PostgreSQL query through `Invoke-NativeCommandOutput` until exactly one committed row matches:

  ```sql
  SELECT so.sales_order_no, so.version, so.status, sol.line_no, sol.ordered_quantity
  FROM erp.sales_orders so
  JOIN erp.sales_order_lines sol ON sol.sales_order_id = so.id
  WHERE so.organization_id = 'org-001'
    AND so.environment_id = 'env-dev'
    AND so.sales_order_no = 'SO-DEMO-001'
    AND so.version = 1
    AND so.status = 'released'
    AND sol.line_no = '10'
    AND sol.ordered_quantity = 2;
  ```

  Use a 90-second deadline and 500 ms polling interval. Retain only a bounded last source observation and last request exception. On timeout, throw a redacted message whose JSON contains `sourceStage = 'erp-source-readiness'`, expected order/version/line/quantity, and the last observation. Call it after DP v1 is observed and before the first change POST.

- [ ] **Step 4: Make state-changing HTTP POSTs fail closed without retry**

  Replace the current transport-only `Invoke-JsonPost` behavior with a single `Invoke-WebRequest -SkipHttpErrorCheck` call. Bound the response body to 8192 characters, parse it once, and reject:

  ```powershell
  if ($httpStatus -lt 200 -or $httpStatus -ge 300 -or
      $null -eq $responseEnvelope -or
      $responseEnvelope.success -ne $true -or
      [string]::IsNullOrWhiteSpace("$($responseEnvelope.data)") -or
      "$($responseEnvelope.data)" -cne $ExpectedData) {
      # Throw one redacted, bounded diagnostic with sourceStage='erp-state-changing-post'
      # and postResponse={ uri, httpStatus, body, parseError }.
  }
  ```

  Do not add a loop, retry count, sleep, recursive call, or caller retry around `Invoke-JsonPost`. Pass `-ExpectedData 'changed'` to the two line changes and `-ExpectedData 'cancelled'` to cancellation.

- [ ] **Step 5: Run the focused test and verify GREEN**

  Run:

  ```powershell
  pwsh -NoProfile -File scripts/tests/erp-sales-order-demand-planning-verify-script.Tests.ps1
  ```

  Expected: exit 0 with `ERP sales-order DemandPlanning cross-process verify script contract tests passed.`

- [ ] **Step 6: Document the narrow fix and long-term follow-up**

  Update `docs/architecture/sales-order-to-demand-planning.md` to state:

  - ERP health can become available before the post-start seed transaction is committed.
  - Acceptance therefore waits for the committed ERP order header version 1 and line 10 before any mutation.
  - Mutation responses must satisfy both HTTP and `ResponseData` business contracts and are never blindly retried.
  - Source readiness timeout, POST business rejection, Redis state, and DemandPlanning convergence are distinct diagnostic stages.
  - A future tracked design should make application readiness represent completion of required startup seed/UoW work; #1140 intentionally does not alter service lifecycle or seed semantics.

- [ ] **Step 7: Run the required verification set**

  Run:

  ```powershell
  pwsh -NoProfile -File scripts/tests/erp-sales-order-demand-planning-verify-script.Tests.ps1
  pwsh -NoProfile -File scripts/check-script-governance.ps1
  pwsh -NoProfile -File scripts/tests/check-script-governance.Tests.ps1
  ```

  Then run:

  ```bash
  git diff --check
  ```

  If Docker plus the required test connection variables are available, run the exact affected acceptance once:

  ```powershell
  pwsh -NoProfile -File scripts/verify-erp-sales-order-demand-planning.ps1
  ```

  Do not substitute `dotnet test backend/Nerv.IIP.sln`.

- [ ] **Step 8: Self-review and commit**

  Confirm the diff changes only the three production files named by this task plus this implementation plan, contains no state-changing retry, and reports no endpoint/facade/schema/OpenAPI/product-doc impact. Commit with:

  ```bash
  git add scripts/tests/erp-sales-order-demand-planning-verify-script.Tests.ps1 scripts/verify-erp-sales-order-demand-planning.ps1 docs/architecture/sales-order-to-demand-planning.md docs/superpowers/plans/2026-07-26-issue-1140-erp-acceptance-readiness.md
  git commit -m "fix(ci): wait for committed ERP acceptance source"
  ```
