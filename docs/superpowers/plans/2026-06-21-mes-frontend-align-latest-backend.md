# MES 前端对齐最新后端（前端阶段 0）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 `claude/product-engineering-redesign` 分支与已补全的 `origin/main` 合并，重做 8 个 MES 页的冲突——**采纳 codex 的 facade 显示字段，保留我方 UX 改造**——并让门禁（typecheck/build/E2E）+ 实机走查全绿，作为后续 MES 前端收尾的干净地基。

**Architecture:** 一次 `git merge origin/main`。后端/文档/api-client/barrel 净合或简单合；冲突集中在 8 个 `src/pages/mes/*.vue` + `reports.vue`(modify/delete)。每个冲突文件按统一**对账规则**三方合并，不取单边。合并后跑门禁与真机验证，提交到 PR #435 分支。

**Tech Stack:** Vue 3 `<script setup>` + TS、vite-plus(`vp`)、pnpm、Playwright E2E、`@nerv-iip/ui` FE-2 区块、Aspire 本地编排（前端 5125）。

---

## 对账规则（本计划的核心 spec，每个冲突文件都按它做）

`<<<<<<< HEAD`（我方/ours）= UI/UX 改造：`WorkOrderQuickView` 速览模态框、删说明书文案、显真实编码、导航图标（图标在 navigation.ts，本批不冲突）。
`>>>>>>> origin/main`（codex/theirs）= facade **显示字段** + `useMesReferenceLabels`：`workOrderNo / operationTaskNo / workCenterName / workCenterCode / deviceAssetName / skuName …` 的 `accessor`，状态筛选选项 `mesOperationTaskStatusOptions`，部分页加了状态计数卡。

**逐类决策（冲突解决时一律照此）：**
1. **imports**：两边**都留**（union）。保留我方 `import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'` + codex 的 `import { mesOperationTaskStatusOptions, … } from '@/composables/mes/useMesReferenceLabels'`、`SectionCard/SectionCards/Select*` 等。
2. **列 columns**：**取 codex 的 accessor**（用 `*No`/`*Name` 显示字段，回退 `*Code`→id），因为它现在有真名字/真单号，优于我方"直显 id"。**保留我方的列裁剪意图**：我方删过的纯占位列若 codex 用 accessor 显示了真值就**保留显示**（不再是占位）；我方加的 `cellClass: 'font-medium'` 锚点列若与 codex 不冲突则保留。
3. **单元格 `#cell-*` slot**：**保留我方 UX**——工单号单元格仍是点击打开 `WorkOrderQuickView` 的按钮（按钮文字用 `{{ row.workOrderNo ?? row.workOrderId }}`），不要退回 codex 的纯文本/旧"查看工单"。
4. **状态筛选**：用 codex 的 `mesOperationTaskStatusOptions`（统一来源）。
5. **KPI 计数卡（codex 新增 readyCount/runningCount/blockedCount 等）**：**保留**——这些是**按状态的可驱动动作的语义计数**（就绪/执行中/受阻），不是被禁的"本页 N 行/后端分页总数"机械计数，符合 DESIGN `list-workbench.md`「语义指标」口径。用 `SectionCards` 呈现。
6. **说明书文案**：我方删掉的顶部"用途说明"段落**保持删除**（不要被 codex 版本带回）。
7. **`WorkOrderQuickView` 组件**：保留我方在模板尾部的 `<WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />` 与 `openWorkOrder` 设 ref 的逻辑。
8. **vue imports**（computed/ref/shallowRef/watch）：按合并后实际用到的 union。

**冲突解决后每个文件都要：无残留 `<<<<<<</=======/>>>>>>>` 标记、TS 自洽、FE-2 区块、无说明书文案、显真实编号、保留速览模态框。**

---

## File Structure（本计划触及）

| 文件 | 冲突类型 | 责任 / 对账要点 |
|---|---|---|
| `frontend/packages/api-client/src/business-console.ts` | 上次自动合过；本次复核 | curated barrel；冲突按 union 留两边导出 |
| `frontend/apps/business-console/src/pages/mes/reports.vue` | modify/delete（我删、codex 改） | 决策：我方已用 production-reports 取代 reports，**保留删除**（`git rm`）；除非 codex 版本含 production-reports 没有的能力——执行时核对一次 |
| `…/mes/work-orders/index.vue` | content | 工单列表 + 急单；冲突最大（rush 表单 + skuName accessor + 工单号链接） |
| `…/mes/operation-tasks.vue` | content | 工序队列；已知冲突：imports、KPI 计数、columns（operationTaskNo/workCenterName）、状态筛选 |
| `…/mes/wip.vue` | content | 在制；已知冲突：imports、状态筛选 Select、columns（workOrderNo/operationTaskId/workCenterName） |
| `…/mes/materials.vue` | content | 领料齐套；materialName accessor vs 我方 materialId 直显 |
| `…/mes/production-reports.vue` | content | 报工记录；workOrderNo/reportNo/operationTaskNo accessor + 我方速览按钮 |
| `…/mes/receipts.vue` | content | 完工入库；workOrderNo/requestNo/skuName + 我方速览按钮 + route.query 自动开弹窗 |
| `…/mes/dispatch.vue` | content | 派工看板；workCenterName/deviceAssetName + 我方派工动作 |

---

## Task 1: 启动合并，解决 barrel 与 reports.vue

**Files:**
- Modify: `frontend/packages/api-client/src/business-console.ts`
- Delete/Reconcile: `frontend/apps/business-console/src/pages/mes/reports.vue`

- [ ] **Step 1: 确认工作区干净并启动合并**

Run:
```bash
cd "C:/WorkFile/Focus/项目/数字工厂/Nerv-IIP/.claude/worktrees/unruffled-euler-ec5cf1"
git status --short | grep -vE '^\?\?' || echo clean
git fetch -q origin
git merge --no-edit origin/main
```
Expected: 合并停在冲突；`git diff --name-only --diff-filter=U` 列出 8 个 .vue（无 business-console.ts 说明 barrel 已自动合）。

- [ ] **Step 2: 复核 barrel**

Run: `git diff --diff-filter=U --name-only | grep business-console.ts || echo "barrel clean"`
若有冲突：保留两边的 `export` / `import type`（union），删冲突标记。否则跳过。

- [ ] **Step 3: 决策 reports.vue（modify/delete）**

Run: `git show origin/main:frontend/apps/business-console/src/pages/mes/reports.vue | head -40`
判定：本分支已用 `production-reports.vue` 取代 `reports.vue`。若 codex 的 reports.vue 无 production-reports 缺失的独有能力 → 删除：
```bash
git rm frontend/apps/business-console/src/pages/mes/reports.vue
```
若有独有能力 → 记下，留到 Task 8 评估（先 `git rm`，能力另起任务补）。

- [ ] **Step 4: 不提交**（合并未完，留到 Task 7 门禁后统一提交）

---

## Task 2: 重做 work-orders/index.vue 冲突

**Files:** Modify: `frontend/apps/business-console/src/pages/mes/work-orders/index.vue`

- [ ] **Step 1: 查看全部冲突 hunk**

Run: `git diff frontend/apps/business-console/src/pages/mes/work-orders/index.vue`（或读带标记的文件）

- [ ] **Step 2: 按对账规则逐 hunk 解决**

照「对账规则」1–8：imports union（留 WorkOrderQuickView + useMesReferenceLabels）；列用 codex 的 `skuName/workOrderNo` accessor；工单号单元格保留我方 RouterLink/速览意图；保留我方删段落；保留急单 rush 表单两边逻辑（codex 若改了 rush 字段以 codex 为准，UX 包装留我方）。删尽所有冲突标记。

- [ ] **Step 3: 验证无残留标记**

Run: `grep -nE '^(<<<<<<<|=======|>>>>>>>)' frontend/apps/business-console/src/pages/mes/work-orders/index.vue || echo "clean"`
Expected: clean

- [ ] **Step 4: `git add` 该文件**（不单独提交）

---

## Task 3–8: 重做其余 7 个 MES 页冲突

> 每个文件重复 Task 2 的 4 步（查看冲突 → 按对账规则解决 → grep 验证无标记 → `git add`）。逐文件，不跳。

- [ ] **Task 3: operation-tasks.vue** — 已知冲突点：imports（+WorkOrderQuickView/+useMesReferenceLabels）、`statusOptions = mesOperationTaskStatusOptions`、KPI 计数（readyCount/runningCount/blockedCount，**保留**）、columns 取 codex accessor（operationTaskNo/workOrderNo/workCenterName/deviceAssetName）但工序号锚点保留我方意图、工单号单元格保留速览按钮、vue imports union（computed,ref,watch）。grep 验证。`git add`。
- [ ] **Task 4: wip.vue** — imports union；状态筛选用 codex 的 `Select + mesOperationTaskStatusOptions`（替换我方 `Input`）；columns 取 codex accessor（workOrderNo/operationTaskId/workCenterName）；保留我方 workOrderId 单元格速览按钮 + `<WorkOrderQuickView>`；vue imports union（computed,ref,shallowRef,watch）。grep 验证。`git add`。
- [ ] **Task 5: materials.vue** — 物料列取 codex 的 `materialName ?? materialCode ?? materialId` accessor（优于我方 materialId 直显）；保留我方删段落、SectionCard 取舍按对账规则 5。grep 验证。`git add`。
- [ ] **Task 6: production-reports.vue** — columns 取 codex accessor（reportNo/workOrderNo/operationTaskNo）；保留我方工单号速览按钮 + 删段落 + `<WorkOrderQuickView>`。grep 验证。`git add`。
- [ ] **Task 7: receipts.vue** — columns 取 codex accessor（requestNo/workOrderNo/skuName）；保留我方速览按钮、删段落、route.query 自动开登记弹窗 watcher、`<WorkOrderQuickView>`、下拉「查看工单」菜单项。grep 验证。`git add`。
- [ ] **Task 8: dispatch.vue** — columns 取 codex accessor（workCenterName/deviceAssetName）；保留我方「派工」动作（assignDispatchTask + 操作员 Select）。grep 验证。`git add`。

---

## Task 9: 门禁 — typecheck + build

**Files:** （无新增，验证全仓）

- [ ] **Step 1: typecheck**

Run: `cd frontend/apps/business-console && pnpm typecheck 2>&1 | tail -15`
Expected: 0 错。有错→回到对应文件修（多为 union import 漏项 / accessor 字段名拼写 / 删段落后未用变量）。

- [ ] **Step 2: build**

Run: `pnpm -C frontend/apps/business-console build 2>&1 | tail -15`
Expected: 构建成功。

- [ ] **Step 3: 完成合并提交**

Run:
```bash
cd "C:/WorkFile/Focus/项目/数字工厂/Nerv-IIP/.claude/worktrees/unruffled-euler-ec5cf1"
git add -A
git commit --no-edit
```
Expected: 合并提交完成（merge commit）。

---

## Task 10: E2E — 断言对账 + 跑

**Files:** Modify(若需): `frontend/apps/business-console/e2e/business-console.spec.ts`

- [ ] **Step 1: 跑 E2E**

Run: `cd frontend/apps/business-console && pnpm exec playwright test --project=desktop 2>&1 | grep -E "passed|failed|Error:" | grep -v ResizeObserver | tail -20`
Expected: 全过。

- [ ] **Step 2: 修因 accessor/单号变化而失效的断言**

合并后工单号显示可能从 `WO-001`(我方) 变成 codex mock 的 `workOrderNo`。若失败：把 `getByRole('button', { name: 'WO-001' })` 等改成合并后 mock 实际渲染的文本（先看 spec mock 数据的 `workOrderNo`/`workOrderId` 值），保持「点工单号→就地弹速览、URL 不变」语义不变。

- [ ] **Step 3: 重跑至全绿，提交**

Run: `pnpm exec playwright test --project=desktop 2>&1 | tail -3`
```bash
git add frontend/apps/business-console/e2e/business-console.spec.ts && git commit -m "test(mes): 合并后 E2E 断言对账"
```

---

## Task 11: 实机走查（seed + Chrome）

**Files:** （无）

- [ ] **Step 1: 确保前端在跑**

Run: `curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5125/`（非 200 → `.\nerv.ps1 dev` 或 aspire `resource-start business-console`，等 10s）

- [ ] **Step 2: seed MES 数据**

Run: `python tmp_seed_mes.py 2>&1 | tail -5`（已存在；建 rush 工单，点亮工单/工序/在制）

- [ ] **Step 3: Chrome 逐页走查关键 MES 页**

走查 `/mes/work-orders`、`/mes/operation-tasks`、`/mes/wip`：确认①显真实单号/名称(workOrderNo/workCenterName 或回退码)②点工单号就地弹 `WorkOrderQuickView`、URL 不变③无说明书文案④无冲突残留乱码。截图留证。

- [ ] **Step 4: 如发现问题**：回对应页修 → 重跑 Task 9 门禁 → 重新走查。

---

## Task 12: 推送

- [ ] **Step 1: 推到 PR #435 分支**

Run:
```bash
cd "C:/WorkFile/Focus/项目/数字工厂/Nerv-IIP/.claude/worktrees/unruffled-euler-ec5cf1"
git push 2>&1 | tail -5
```
Expected: 推送成功；PR #435 自动更新为"已合并最新 main + 对账完成"。

---

## Self-Review（写完计划的自检）

- **Spec 覆盖**：8 个冲突 .vue + barrel + reports.vue 各有任务（T1–T8）；门禁(T9)、E2E(T10)、实机(T11)、推送(T12)齐。✓
- **占位扫描**：reconciliation 步骤给的是"对账规则 + 每文件已知冲突点"，非 TBD；合并冲突的精确 hunk 解决依赖执行时三方内容，规则即 spec。
- **类型一致**：accessor 字段名（workOrderNo/operationTaskNo/workCenterName/skuName/materialName/deviceAssetName/requestNo/reportNo）与审计在 `BusinessConsoleModels.cs` 确认的 row 字段一致；`mesOperationTaskStatusOptions` 来自 `@/composables/mes/useMesReferenceLabels`（codex 新增）。
- **后续计划**：显示名前端兜底(F2，因 `*Name` 服务端为 null)、主线页收尾(F1)、planning→MES 真闭环(F3，待 #461 后端)另起独立 plan。
