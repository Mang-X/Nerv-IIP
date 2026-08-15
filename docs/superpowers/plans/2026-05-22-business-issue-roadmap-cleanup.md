# 业务 Issue 路线图清理实施计划

> **面向智能体执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**重新组织业务平台中非甘特图相关的 GitHub Issue，使开放 Issue 与当前代码事实、架构决策和可执行切片保持一致。

**架构：**仍承载有效范围的旧宽泛 Issue 保留为史诗 Issue（Epic），为可执行工作创建缺失的子 Issue，并保持 #72 已关闭。只使用 `gh` 命令行界面（CLI）；在工作区内生成用于 Issue 正文的临时 Markdown 文件，以便提交前审核远程修改内容。

**技术栈：**GitHub CLI、Markdown Issue 正文、现有 Nerv-IIP 文档、PowerShell。

**执行结果（2026-05-22）：**已完成。已重写现有史诗 Issue #70、#71 和 #73-#77，已创建子 Issue #131-#143，已通过评论关联执行 Issue #127-#130，#70/#73-#77 的正文现已包含实际子 Issue 编号，架构文档已更新，并已在提交后删除临时正文文件 `.codex/tmp/business-issue-roadmap`。

---

## 文件

- 读取：`docs/superpowers/specs/2026-05-22-business-issue-roadmap-design.md`
- 读取：`docs/architecture/implementation-readiness.md`
- 读取：`docs/architecture/business-platform-domain-architecture.md`
- 创建：`.codex/tmp/business-issue-roadmap/*.md`
- 修改：`docs/architecture/business-platform-domain-architecture.md`
- 修改：`docs/architecture/implementation-readiness.md`

## 任务 1：准备 Issue 正文文件

- [x] **步骤 1：创建临时目录**

运行：

```powershell
New-Item -ItemType Directory -Force .codex/tmp/business-issue-roadmap
```

预期：目录存在。

- [x] **步骤 2：为 #70、#71、#73、#74、#75、#76 和 #77 编写替换正文**

使用 `docs/superpowers/specs/2026-05-22-business-issue-roadmap-design.md` 中的模板。子 Issue 链接起初使用子 Issue 标题；创建子 Issue 后，替换为实际 Issue 编号或用实际编号添加评论。

- [x] **步骤 3：编写子 Issue 正文**

为每个新子 Issue 创建一个 Markdown 文件：

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

每份正文必须包含：

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

为每个子 Issue 使用实际父 Issue 编号：Inventory、Quality 检验、BarcodeLabel 和 BusinessApproval 使用 #73；MES 使用 #74；WMS 使用 #75；ERP 采购/销售/财务使用 #76；业务服务注册与就绪性验证使用 #77；FileStorage 和前端组件后续工作使用 #70。

## 任务 2：重写现有史诗 Issue

- [x] **步骤 1：更新 #70**

运行：

```powershell
gh issue edit 70 --body-file .codex/tmp/business-issue-roadmap/issue-70.md
```

预期：Issue #70 的正文已替换。

- [x] **步骤 2：更新 #71**

运行：

```powershell
gh issue edit 71 --body-file .codex/tmp/business-issue-roadmap/issue-71.md
```

预期：Issue #71 的正文已替换。

- [x] **步骤 3：更新 #73 至 #77**

运行：

```powershell
gh issue edit 73 --body-file .codex/tmp/business-issue-roadmap/issue-73.md
gh issue edit 74 --body-file .codex/tmp/business-issue-roadmap/issue-74.md
gh issue edit 75 --body-file .codex/tmp/business-issue-roadmap/issue-75.md
gh issue edit 76 --body-file .codex/tmp/business-issue-roadmap/issue-76.md
gh issue edit 77 --body-file .codex/tmp/business-issue-roadmap/issue-77.md
```

预期：Issue #73-#77 已重写为史诗 Issue。

## 任务 3：创建子 Issue

- [x] **步骤 1：创建 #73 的子 Issue**

运行：

```powershell
gh issue create --title "feat: Inventory MVP - stock ledger, movement, availability and counts" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/inventory-mvp.md
gh issue create --title "feat: Quality inspection MVP - inspection plan, record and receiving/operation inspection" --label "enhancement" --label "business-platform" --label "quality" --body-file .codex/tmp/business-issue-roadmap/quality-inspection-mvp.md
gh issue create --title "feat: BarcodeLabel MVP - rules, templates, print batches and scans" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/barcode-label-mvp.md
gh issue create --title "feat: BusinessApproval MVP - templates, approval chains and approval records" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/business-approval-mvp.md
```

预期：输出四个 Issue URL。

- [x] **步骤 2：创建 #74 和 #75 的子 Issue**

运行：

```powershell
gh issue create --title "feat: MES CleanDDD persistence and execution MVP" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/mes-cleanddd-persistence.md
gh issue create --title "feat: WMS execution MVP - inbound, outbound, count and WCS adapter boundary" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/wms-execution-mvp.md
```

预期：输出两个 Issue URL。

- [x] **步骤 3：创建 #76 的子 Issue**

运行：

```powershell
gh issue create --title "feat: ERP Procurement MVP - requisitions, RFQ, purchase orders and receipts" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/erp-procurement-mvp.md
gh issue create --title "feat: ERP Sales MVP - opportunity, quotation, sales order and delivery request" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/erp-sales-mvp.md
gh issue create --title "feat: ERP Finance MVP - receivables, payables, vouchers and cost candidates" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/erp-finance-mvp.md
```

预期：输出三个 Issue URL。

- [x] **步骤 4：创建跨领域和基础设施子 Issue**

运行：

```powershell
gh issue create --title "chore: Business service registration, verify script pattern and readiness tracking" --label "enhancement" --label "business-platform" --body-file .codex/tmp/business-issue-roadmap/business-service-registration-verify-readiness.md
gh issue create --title "feat: FileStorage tus hardening - size, checksum, expiration and protocol compatibility" --label "enhancement" --body-file .codex/tmp/business-issue-roadmap/filestorage-tus-hardening.md
gh issue create --title "feat: FileStorage object storage integration - MinIO/S3 multipart post-MVP" --label "enhancement" --body-file .codex/tmp/business-issue-roadmap/filestorage-object-storage-integration.md
gh issue create --title "feat: Frontend component gap closure for business console readiness" --label "enhancement" --label "area:frontend" --body-file .codex/tmp/business-issue-roadmap/frontend-component-gap-closure.md
```

预期：输出四个 Issue URL。

## 任务 4：关联现有执行 Issue

- [x] **步骤 1：评论 #127 至 #130**

为每个 Issue 添加一条评论：

```powershell
gh issue comment 127 --body "Roadmap alignment: this issue is the executable ProductEngineering completion slice. Parent domain context: ADR 0012 Slice 2 and docs/superpowers/plans/2026-05-20-business-product-engineering-mvp.md. Current code fact: ProductionVersion exists; EngineeringDocument, EngineeringItem, EBOM, MBOM, Routing and ECO/ECN remain in scope."
gh issue comment 128 --body "Roadmap alignment: this issue is the executable DemandPlanning slice. It remains blocked on ProductEngineering published BOM/routing contracts and Inventory availability/movement minimum APIs."
gh issue comment 129 --body "Roadmap alignment: this issue is the executable IndustrialTelemetry slice. It depends on MasterData device asset references and must keep PLC/DCS/SCADA as external Connector sources."
gh issue comment 130 --body "Roadmap alignment: this issue is the executable Maintenance slice. Existing facts: Contracts.Maintenance already defines AssetUnavailable/AssetRestored events, and MES has a consumer-side handler. Alarm-triggered work order creation depends on IndustrialTelemetry."
```

预期：每个 Issue 都收到一条路线图对齐评论。

## 任务 5：更新架构文档

- [x] **步骤 1：更新业务架构 Issue 映射**

修改 `docs/architecture/business-platform-domain-architecture.md`，新增“路线图 Issue”章节并包含以下映射：

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

- [x] **步骤 2：更新实施就绪状态**

修改 `docs/architecture/implementation-readiness.md` 的当前结论或当前使用说明章节，加入简明的业务服务代码事实表：

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

## 任务 6：验证清理结果

- [x] **步骤 1：验证 Issue 列表**

运行：

```powershell
gh issue list --state open --limit 200 --json number,title,labels,url,updatedAt
```

预期：列表包含重写后的史诗 Issue、现有 #127-#130 以及新创建的子 Issue。

- [x] **步骤 2：验证本地文档**

运行：

```powershell
rg -n "Issue Roadmap|BusinessMasterData|ProductEngineering|MES CleanDDD|Inventory MVP" docs/architecture/business-platform-domain-architecture.md docs/architecture/implementation-readiness.md
git diff --check
```

预期：两个命令的退出码均为 0；`rg` 显示新的路线图和就绪状态事实。

## 自审清单

1. #78 保持未修改。
2. #72 保持已关闭且未修改。
3. 旧宽泛 Issue 仅以史诗 Issue 形式保持开放。
4. 新子 Issue 具有明确的父 Issue 引用和验收标准。
5. 本地文档陈述代码事实，而不是假设。
