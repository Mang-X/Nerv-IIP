# MES 前端对齐最新后端（前端阶段 0）实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**把 `claude/product-engineering-redesign` 分支与已补全的 `origin/main` 合并，重做 8 个 MES 页的冲突——**采纳 codex 的 facade 显示字段，保留我方 UX 改造**——并让门禁（typecheck/build/E2E）+ 实机走查全绿，作为后续 MES 前端收尾的干净地基。

**架构：**执行一次 `git merge origin/main`。后端/文档/api-client/barrel 无冲突合并或简单合并；冲突集中在 8 个 `src/pages/mes/*.vue` + `reports.vue`（修改/删除冲突）。每个冲突文件按统一**对账规则**三方合并，不取单边。合并后运行门禁与真机验证，并提交到 PR #435 分支。

**技术栈：**Vue 3 `<script setup>` + TS、vite-plus（`vp`）、pnpm、Playwright E2E、`@nerv-iip/ui` FE-2 区块、Aspire 本地编排（前端 5125）。

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

## 文件结构（本计划触及）

| 文件 | 冲突类型 | 职责 / 对账要点 |
|---|---|---|
| `frontend/packages/api-client/src/business-console.ts` | 上次自动合过；本次复核 | 整理后的 barrel 导出；冲突时按并集合并保留两边导出 |
| `frontend/apps/business-console/src/pages/mes/reports.vue` | 修改/删除（我方删除、codex 修改） | 决策：我方已用 production-reports 取代 reports，**保留删除**（`git rm`）；除非 codex 版本包含 production-reports 没有的能力——执行时核对一次 |
| `…/mes/work-orders/index.vue` | 内容冲突 | 工单列表 + 急单；冲突最大（rush 表单 + skuName accessor + 工单号链接） |
| `…/mes/operation-tasks.vue` | 内容冲突 | 工序队列；已知冲突：import、KPI 计数、column（operationTaskNo/workCenterName）、状态筛选 |
| `…/mes/wip.vue` | 内容冲突 | 在制；已知冲突：import、状态筛选 Select、column（workOrderNo/operationTaskId/workCenterName） |
| `…/mes/materials.vue` | 内容冲突 | 领料齐套；materialName accessor 与我方直接显示 materialId 的冲突 |
| `…/mes/production-reports.vue` | 内容冲突 | 报工记录；workOrderNo/reportNo/operationTaskNo accessor + 我方速览按钮 |
| `…/mes/receipts.vue` | 内容冲突 | 完工入库；workOrderNo/requestNo/skuName + 我方速览按钮 + route.query 自动打开弹窗 |
| `…/mes/dispatch.vue` | 内容冲突 | 派工看板；workCenterName/deviceAssetName + 我方派工动作 |

---

## Task 1：启动合并，解决 barrel 与 reports.vue

**文件：**
- 修改：`frontend/packages/api-client/src/business-console.ts`
- 删除/对账：`frontend/apps/business-console/src/pages/mes/reports.vue`

- [ ] **步骤 1：确认工作区干净并启动合并**

运行：
```bash
cd "C:/WorkFile/Focus/项目/数字工厂/Nerv-IIP/.claude/worktrees/unruffled-euler-ec5cf1"
git status --short | grep -vE '^\?\?' || echo clean
git fetch -q origin
git merge --no-edit origin/main
```
预期：合并停在冲突处；`git diff --name-only --diff-filter=U` 列出 8 个 .vue（无 business-console.ts 说明 barrel 已自动合并）。

- [ ] **步骤 2：复核 barrel**

运行：`git diff --diff-filter=U --name-only | grep business-console.ts || echo "barrel clean"`
若有冲突：保留两边的 `export` / `import type`（union），删冲突标记。否则跳过。

- [ ] **步骤 3：决策 reports.vue（修改/删除）**

运行：`git show origin/main:frontend/apps/business-console/src/pages/mes/reports.vue | head -40`
判定：本分支已用 `production-reports.vue` 取代 `reports.vue`。若 codex 的 reports.vue 无 production-reports 缺失的独有能力 → 删除：
```bash
git rm frontend/apps/business-console/src/pages/mes/reports.vue
```
若有独有能力 → 记下，留到 Task 8 评估（先 `git rm`，能力另起任务补）。

- [ ] **步骤 4：不提交**（合并未完成，留到 Task 7 门禁后统一提交）

---

## Task 2：重做 work-orders/index.vue 冲突

**文件：**修改：`frontend/apps/business-console/src/pages/mes/work-orders/index.vue`

- [ ] **步骤 1：查看全部冲突 hunk**

运行：`git diff frontend/apps/business-console/src/pages/mes/work-orders/index.vue`（或读取带标记的文件）

- [ ] **步骤 2：按对账规则逐 hunk 解决**

照「对账规则」1–8：imports union（留 WorkOrderQuickView + useMesReferenceLabels）；列用 codex 的 `skuName/workOrderNo` accessor；工单号单元格保留我方 RouterLink/速览意图；保留我方删段落；保留急单 rush 表单两边逻辑（codex 若改了 rush 字段以 codex 为准，UX 包装留我方）。删尽所有冲突标记。

- [ ] **步骤 3：验证无残留标记**

运行：`grep -nE '^(<<<<<<<|=======|>>>>>>>)' frontend/apps/business-console/src/pages/mes/work-orders/index.vue || echo "clean"`
预期：无残留标记。

- [ ] **步骤 4：对该文件执行 `git add`**（不单独提交）

---

## Task 3–8：重做其余 7 个 MES 页冲突

> 每个文件重复 Task 2 的 4 步（查看冲突 → 按对账规则解决 → grep 验证无标记 → `git add`）。逐文件，不跳。

- [ ] **Task 3：operation-tasks.vue** — 已知冲突点：import（+WorkOrderQuickView/+useMesReferenceLabels）、`statusOptions = mesOperationTaskStatusOptions`、KPI 计数（readyCount/runningCount/blockedCount，**保留**）、column 取 codex accessor（operationTaskNo/workOrderNo/workCenterName/deviceAssetName），但工序号锚点保留我方意图、工单号单元格保留速览按钮；Vue import 按并集合并（computed/ref/watch）。用 grep 验证后执行 `git add`。
- [ ] **Task 4：wip.vue** — import 按并集合并；状态筛选使用 codex 的 `Select + mesOperationTaskStatusOptions`（替换我方 `Input`）；column 取 codex accessor（workOrderNo/operationTaskId/workCenterName）；保留我方 workOrderId 单元格速览按钮 + `<WorkOrderQuickView>`；Vue import 按并集合并（computed/ref/shallowRef/watch）。用 grep 验证后执行 `git add`。
- [ ] **Task 5：materials.vue** — 物料列取 codex 的 `materialName ?? materialCode ?? materialId` accessor（优于我方直接显示 materialId）；保留我方删除段落的结果，SectionCard 取舍按对账规则 5。用 grep 验证后执行 `git add`。
- [ ] **Task 6：production-reports.vue** — column 取 codex accessor（reportNo/workOrderNo/operationTaskNo）；保留我方工单号速览按钮、删除段落的结果和 `<WorkOrderQuickView>`。用 grep 验证后执行 `git add`。
- [ ] **Task 7：receipts.vue** — column 取 codex accessor（requestNo/workOrderNo/skuName）；保留我方速览按钮、删除段落的结果、route.query 自动打开登记弹窗的 watcher、`<WorkOrderQuickView>` 和下拉“查看工单”菜单项。用 grep 验证后执行 `git add`。
- [ ] **Task 8：dispatch.vue** — column 取 codex accessor（workCenterName/deviceAssetName）；保留我方“派工”动作（assignDispatchTask + 操作员 Select）。用 grep 验证后执行 `git add`。

---

## Task 9：门禁 — typecheck + build

**文件：**（无新增，验证全仓）

- [ ] **步骤 1：typecheck**

运行：`cd frontend/apps/business-console && pnpm typecheck 2>&1 | tail -15`
预期：0 错。有错则回到对应文件修复（多为并集合并时遗漏 import、accessor 字段名拼写错误，或删除段落后存在未使用变量）。

- [ ] **步骤 2：build**

运行：`pnpm -C frontend/apps/business-console build 2>&1 | tail -15`
预期：构建成功。

- [ ] **步骤 3：完成合并提交**

运行：
```bash
cd "C:/WorkFile/Focus/项目/数字工厂/Nerv-IIP/.claude/worktrees/unruffled-euler-ec5cf1"
git add -A
git commit --no-edit
```
预期：合并提交完成（merge commit）。

---

## Task 10：E2E — 断言对账与运行

**文件：**按需修改：`frontend/apps/business-console/e2e/business-console.spec.ts`

- [ ] **步骤 1：运行 E2E**

运行：`cd frontend/apps/business-console && pnpm exec playwright test --project=desktop 2>&1 | grep -E "passed|failed|Error:" | grep -v ResizeObserver | tail -20`
预期：全部通过。

- [ ] **步骤 2：修复因 accessor/单号变化而失效的断言**

合并后工单号显示可能从 `WO-001`(我方) 变成 codex mock 的 `workOrderNo`。若失败：把 `getByRole('button', { name: 'WO-001' })` 等改成合并后 mock 实际渲染的文本（先看 spec mock 数据的 `workOrderNo`/`workOrderId` 值），保持「点工单号→就地弹速览、URL 不变」语义不变。

- [ ] **步骤 3：重跑至全绿并提交**

运行：`pnpm exec playwright test --project=desktop 2>&1 | tail -3`
```bash
git add frontend/apps/business-console/e2e/business-console.spec.ts && git commit -m "test(mes): 合并后 E2E 断言对账"
```

---

## Task 11：实机走查（seed + Chrome）

**文件：**（无）

- [ ] **步骤 1：确保前端在运行**

运行：`curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5125/`（非 200 → `.\nerv.ps1 dev` 或 Aspire `resource-start business-console`，等待 10 秒）

- [ ] **步骤 2：为 MES 数据执行 seed**

运行：`python tmp_seed_mes.py 2>&1 | tail -5`（已存在；创建 rush 工单，点亮工单/工序/在制）

- [ ] **步骤 3：在 Chrome 中逐页走查关键 MES 页面**

走查 `/mes/work-orders`、`/mes/operation-tasks`、`/mes/wip`：确认①显真实单号/名称(workOrderNo/workCenterName 或回退码)②点工单号就地弹 `WorkOrderQuickView`、URL 不变③无说明书文案④无冲突残留乱码。截图留证。

- [ ] **步骤 4：如发现问题**：回到对应页面修复 → 重跑 Task 9 门禁 → 重新走查。

---

## Task 12：推送

- [ ] **步骤 1：推送到 PR #435 分支**

运行：
```bash
cd "C:/WorkFile/Focus/项目/数字工厂/Nerv-IIP/.claude/worktrees/unruffled-euler-ec5cf1"
git push 2>&1 | tail -5
```
预期：推送成功；PR #435 自动更新为“已合并最新 main + 对账完成”。

---

## 自我审核（写完计划后的自检）

- **规格覆盖**：8 个冲突 .vue + barrel + reports.vue 各有任务（T1–T8）；门禁（T9）、E2E（T10）、实机（T11）、推送（T12）齐全。✓
- **占位扫描**：对账步骤给出的是“对账规则 + 每文件已知冲突点”，不是 TBD；合并冲突的精确 hunk 解决依赖执行时的三方内容，规则本身即规格。
- **类型一致**：accessor 字段名（workOrderNo/operationTaskNo/workCenterName/skuName/materialName/deviceAssetName/requestNo/reportNo）与审计在 `BusinessConsoleModels.cs` 确认的 row 字段一致；`mesOperationTaskStatusOptions` 来自 `@/composables/mes/useMesReferenceLabels`（codex 新增）。
- **后续计划**：显示名前端兜底(F2，因 `*Name` 服务端为 null)、主线页收尾(F1)、planning→MES 真闭环(F3，待 #461 后端)另起独立 plan。
