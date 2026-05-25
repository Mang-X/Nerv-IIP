# Business Issue Roadmap Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize the non-Gantt business-platform GitHub issues so open issues match current code facts, architecture decisions and executable slices.

**Architecture:** Keep old broad issues as epics when they still carry useful scope, create missing child issues for executable work, and leave #72 closed. Use `gh` CLI only; generate temporary markdown files under the workspace for issue bodies so remote edits are reviewable before submission.

**Tech Stack:** GitHub CLI, Markdown issue bodies, existing Nerv-IIP docs, PowerShell.

**Execution Result (2026-05-22):** Completed. Existing epics #70, #71 and #73-#77 were rewritten, child issues #131-#143 were created, execution issues #127-#130 were linked by comments, #70/#73-#77 bodies now include actual child issue numbers, architecture docs were updated, and temporary `.codex/tmp/business-issue-roadmap` body files were removed after submission.

---

## Files

- Read: `docs/superpowers/specs/2026-05-22-business-issue-roadmap-design.md`
- Read: `docs/architecture/implementation-readiness.md`
- Read: `docs/architecture/business-platform-domain-architecture.md`
- Create: `.codex/tmp/business-issue-roadmap/*.md`
- Modify: `docs/architecture/business-platform-domain-architecture.md`
- Modify: `docs/architecture/implementation-readiness.md`

## Task 1: Prepare Issue Body Files

- [x] **Step 1: Create temp directory**

Run:

```powershell
New-Item -ItemType Directory -Force .codex/tmp/business-issue-roadmap
```

Expected: directory exists.

- [x] **Step 2: Write replacement bodies for #70, #71, #73, #74, #75, #76 and #77**

Use the templates from `docs/superpowers/specs/2026-05-22-business-issue-roadmap-design.md`. The child issue links will initially use child issue titles; after creating child issues, replace or comment with actual issue numbers.

- [x] **Step 3: Write child issue bodies**

Create one markdown file for each new child issue:

1. `inventory-mvp.md`
2. `quality-inspection-mvp.md`
3. `barcode-label-mvp.md`
4. `business-approval-mvp.md`
5. `mes-cleanddd-persistence.md`
6. `wms-execution-mvp.md`
7. `erp-procurement-mvp.md`
8. `erp-sales-mvp.md`
9. `erp-finance-mvp.md`
10. `business-service-registration-verify-readiness.md`
11. `filestorage-tus-hardening.md`
12. `filestorage-object-storage-integration.md`
13. `frontend-component-gap-closure.md`

Each body must include:

```markdown
## Parent

#73

## Current Facts

Inventory service does not exist yet. BusinessMasterData realignment is available as the Layer 0 reference source.

## Scope

Create the Inventory MVP facts described by the issue title.

## Acceptance

The issue body lists concrete API, persistence, permission and verification expectations.

## References

ADR 0012, the business architecture document and the relevant plan path.
```

Use the actual parent number for each child issue: #73 for Inventory, Quality inspection, BarcodeLabel and BusinessApproval; #74 for MES; #75 for WMS; #76 for ERP Procurement/Sales/Finance; #77 for business service registration and verify readiness; #70 for FileStorage and frontend component follow-ups.

## Task 2: Rewrite Existing Epics

- [x] **Step 1: Update #70**

Run:

```powershell
gh issue edit 70 --body-file .codex/tmp/business-issue-roadmap/issue-70.md
```

Expected: issue #70 body is replaced.

- [x] **Step 2: Update #71**

Run:

```powershell
gh issue edit 71 --body-file .codex/tmp/business-issue-roadmap/issue-71.md
```

Expected: issue #71 body is replaced.

- [x] **Step 3: Update #73 through #77**

Run:

```powershell
gh issue edit 73 --body-file .codex/tmp/business-issue-roadmap/issue-73.md
gh issue edit 74 --body-file .codex/tmp/business-issue-roadmap/issue-74.md
gh issue edit 75 --body-file .codex/tmp/business-issue-roadmap/issue-75.md
gh issue edit 76 --body-file .codex/tmp/business-issue-roadmap/issue-76.md
gh issue edit 77 --body-file .codex/tmp/business-issue-roadmap/issue-77.md
```

Expected: issues #73-#77 are rewritten as epics.

## Task 3: Create Child Issues

- [x] **Step 1: Create #73 child issues**

Run:

```powershell
gh issue create --title "feat: Inventory MVP - stock ledger, movement, availability and counts" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/inventory-mvp.md
gh issue create --title "feat: Quality inspection MVP - inspection plan, record and receiving/operation inspection" --label "enhancement" --label "business-platform" --label "quality" --body-file .codex/tmp/business-issue-roadmap/quality-inspection-mvp.md
gh issue create --title "feat: BarcodeLabel MVP - rules, templates, print batches and scans" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/barcode-label-mvp.md
gh issue create --title "feat: BusinessApproval MVP - templates, approval chains and approval records" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/business-approval-mvp.md
```

Expected: four issue URLs are printed.

- [x] **Step 2: Create #74 and #75 child issues**

Run:

```powershell
gh issue create --title "feat: MES CleanDDD persistence and execution MVP" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/mes-cleanddd-persistence.md
gh issue create --title "feat: WMS execution MVP - inbound, outbound, count and WCS adapter boundary" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/wms-execution-mvp.md
```

Expected: two issue URLs are printed.

- [x] **Step 3: Create #76 child issues**

Run:

```powershell
gh issue create --title "feat: ERP Procurement MVP - requisitions, RFQ, purchase orders and receipts" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/erp-procurement-mvp.md
gh issue create --title "feat: ERP Sales MVP - opportunity, quotation, sales order and delivery request" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/erp-sales-mvp.md
gh issue create --title "feat: ERP Finance MVP - receivables, payables, vouchers and cost candidates" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/erp-finance-mvp.md
```

Expected: three issue URLs are printed.

- [x] **Step 4: Create cross-cutting and infrastructure child issues**

Run:

```powershell
gh issue create --title "chore: Business service registration, verify script pattern and readiness tracking" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/business-service-registration-verify-readiness.md
gh issue create --title "feat: FileStorage tus hardening - size, checksum, expiration and protocol compatibility" --label "enhancement" --body-file .codex/tmp/business-issue-roadmap/filestorage-tus-hardening.md
gh issue create --title "feat: FileStorage object storage integration - MinIO/S3 multipart post-MVP" --label "enhancement" --body-file .codex/tmp/business-issue-roadmap/filestorage-object-storage-integration.md
gh issue create --title "feat: Frontend component gap closure for business console readiness" --label "enhancement" --label "area:frontend" --body-file .codex/tmp/business-issue-roadmap/frontend-component-gap-closure.md
```

Expected: four issue URLs are printed.

## Task 4: Link Existing Execution Issues

- [x] **Step 1: Comment on #127 through #130**

Run one comment per issue:

```powershell
gh issue comment 127 --body "Roadmap alignment: this issue is the executable ProductEngineering completion slice. Parent domain context: ADR 0012 Slice 2 and docs/superpowers/plans/2026-05-20-business-product-engineering-mvp.md. Current code fact: ProductionVersion exists; EngineeringDocument, EngineeringItem, EBOM, MBOM, Routing and ECO/ECN remain in scope."
gh issue comment 128 --body "Roadmap alignment: this issue is the executable DemandPlanning slice. It remains blocked on ProductEngineering published BOM/routing contracts and Inventory availability/movement minimum APIs."
gh issue comment 129 --body "Roadmap alignment: this issue is the executable IndustrialTelemetry slice. It depends on MasterData device asset references and must keep PLC/DCS/SCADA as external Connector sources."
gh issue comment 130 --body "Roadmap alignment: this issue is the executable Maintenance slice. Existing facts: Contracts.Maintenance already defines AssetUnavailable/AssetRestored events, and MES has a consumer-side handler. Alarm-triggered work order creation depends on IndustrialTelemetry."
```

Expected: each issue receives one roadmap alignment comment.

## Task 5: Update Architecture Docs

- [x] **Step 1: Update business architecture issue mapping**

Modify `docs/architecture/business-platform-domain-architecture.md` by adding an "Issue Roadmap" section that maps:

```markdown
| Slice | GitHub Tracking |
| --- | --- |
| Infrastructure completion | #70, #71 and child issues |
| Layer 0 MasterData | #72 closed; follow-up via downstream issues |
| ProductEngineering | #127 |
| Layer 1 common capabilities | #73 plus Inventory, Quality inspection, BarcodeLabel and BusinessApproval child issues |
| DemandPlanning | #128 |
| ERP | #76 plus Procurement/Sales/Finance child issues |
| WMS | #75 plus WMS execution child issue |
| MES | #74 plus MES CleanDDD persistence child issue |
| IndustrialTelemetry | #129 |
| Maintenance | #130 |
| Full-chain acceptance | #77 |
```

- [x] **Step 2: Update implementation readiness**

Modify `docs/architecture/implementation-readiness.md` current conclusion or current usage section with a concise business-service code fact table:

```markdown
| Service | Current code fact | Tracking |
| --- | --- | --- |
| BusinessMasterData | Domain/Infrastructure/Web + migrations + tests; realignment verification script exists | #72 closed |
| ProductEngineering | Domain/Infrastructure/Web + ProductionVersion only | #127 |
| Quality | Domain/Infrastructure/Web + NCR only | #73 child: Quality inspection |
| MES | Web-only in-memory scheduling/reschedule | #74 child: MES CleanDDD persistence |
| Inventory | no service directory | #73 child |
| BarcodeLabel | no service directory | #73 child |
| BusinessApproval | no service directory | #73 child |
| DemandPlanning | no service directory | #128 |
| WMS | no service directory | #75 child |
| ERP | no service directory | #76 children |
| IndustrialTelemetry | no service directory | #129 |
| Maintenance | no service directory | #130 |
```

## Task 6: Verify Cleanup

- [x] **Step 1: Verify issue list**

Run:

```powershell
gh issue list --state open --limit 200 --json number,title,labels,url,updatedAt
```

Expected: the list includes rewritten epics, existing #127-#130, and newly created child issues.

- [x] **Step 2: Verify local docs**

Run:

```powershell
rg -n "Issue Roadmap|BusinessMasterData|ProductEngineering|MES CleanDDD|Inventory MVP" docs/architecture/business-platform-domain-architecture.md docs/architecture/implementation-readiness.md
git diff --check
```

Expected: both commands exit 0; `rg` shows the new roadmap/readiness facts.

## Self-Review Checklist

1. #78 remains outside the business-service roadmap; current RFC archival status is tracked in `docs/architecture/gantt-scheduling-visualization-rfc.md`.
2. #72 remains closed and untouched.
3. Old broad issues remain open only as epics.
4. New child issues have clear parent issue references and acceptance criteria.
5. Local docs state code facts, not assumptions.
