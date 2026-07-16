# Page Prototype: List Workbench（列表工作台页）

Business Console 列表页的规范原型——**所有** stage-B 列表/工作台页的照抄基线。
黄金标准页：`apps/business-console/src/pages/mes/operation-tasks.vue`（工序执行）。
全部用 FE-2 区块拼装，绝不手搓这些区域。

## 规则

### 结构（自上而下）

| 区域        | 区块                                     | 约束                                                                                                                                                                                                       |
| ----------- | ---------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Layout      | `BusinessLayout`（T 形 `AppShellT`）     | 页面只填内容槽——不内联 shell chrome。                                                                                                                                                                      |
| 页头        | `NvPageHeader`                           | 面包屑即标题 + 结果 `count` + `#actions`（如 刷新）。不做大标题/描述块。                                                                                                                                   |
| KPI（可选） | `NvSectionCards` + `NvSectionCard`       | 仅当存在能帮操作者行动的**语义**指标（如 待收料 3 单）。1–4 卡，`tabular-nums` + 短 hint。绝不放**机械**计数（本页 X 行 / 后端分页总数）——会误导。绝不手搓 metric `<div>`。没有真指标就整块不要。          |
| 工具条      | `NvToolbar`                              | `v-model:search`（live）+ `#filters`（状态/范围 `NvSelect`）+ `#actions`（重置 / 次操作）。单行。                                                                                                          |
| 错误        | inline `text-destructive` `role="alert"` | 仅 facade 报错时出现；其余情况由空态说话。                                                                                                                                                                 |
| 表格 + 分页 | `NvDataTable`                            | 列配置 + `#cell-<key>` 槽。`:loading` 骨架、empty message、点列排序。状态 → `NvStatusBadge`；行菜单 → `NvRowActions`。服务端分页走 `manual` 模式（分页已内建，独立分页块不再需要）。绝不手搓裸 `<Table>`。 |

### 数据 / 排序 / 分页契约

- 页面的数据 composable（如 `useMesOperationTasks`）不动。
- 过滤 + 排序 + 分页是**页面所有**，顺序固定：`visible → sorted → paged`。给
  `NvDataTable` 传 `:rows="paged"` 与 `:client-sort="false"`（表格只渲染 + 派发
  `update:sort`；页面对全量重排，分页才正确）。`manual` 模式下 `:total-items` 用
  **排序/过滤后的总量**（分页前）或服务端 total。
- 跨域导航用链接 / `NvRowActions` 菜单项，不加额外菜单层级。

### 文案与元数据（来自 frontend-navigation-map.md）

- 可见文案是业务中文。**禁**开发/平台词：organization/environment/context、
  `operationId`、`sourceSystem`、demo/seed/mock/样例。
- 对象详情、动作表单、页内 tab 永不提升进导航。
- **用 UI 引导，不用解说——页面不是说明书。** 工具条上方不放「用途说明」浮段
  （`这里是… 先… 再…`）。页头（标题+count）、列头、`NvStatusBadge`、主按钮与空态
  已经回答了「这是什么/我该干嘛」。确需副标题时**只允许一个短句**。不做冗余的
  「本页 N 行 / N 单」计数行（count 已在页头与分页页脚）。
- **展示真实编号，绝不用占位藏它。** 直接渲染 facade 返回的人读编码——
  `workOrderId=WO-20260608-000015`、`workCenterId=WC-ASSY`、`skuId=SKU-…`、
  `operationTaskId=WO-…-OP-10`。让 **ID 本身**成为打开详情/速览的点击目标；**不**换成
  笼统的「查看X」按钮，**不**在有值的地方显示「待接入 / 名称待接入」。业务编码是操作者
  词汇，**不是**开发语言——只有真技术内脏（raw GUID、`resourceType`、org/env、`#hash`）
  才禁。真 null 才显示 `—`。（MES facade 返回的是编码不是 GUID——断言字段不可用前先
  实探。）

## 判定（迁移页逐条验收清单）

- [ ] 用 `BusinessLayout` + `NvPageHeader` + `NvToolbar` + `NvDataTable`
      （`NvSectionCards` 仅当有真语义 KPI）。
- [ ] 无遗留 per-app 区块（`BusinessPageHeader`/`BusinessContextBar`/`BusinessMetricCell`/
      `BusinessTablePagination`/`BusinessRowActions`/`BusinessStatusBadge`/
      `BusinessEmptyState`/`BusinessFormStatus`）。
- [ ] 页内无裸 `<Table>`/`<TableHeader>` 拼装——表格走 `NvDataTable`。
- [ ] 无手搓 metric `<div>`——KPI 走 `NvSectionCard`。
- [ ] 状态走 `NvStatusBadge`；行操作走 `NvRowActions`。
- [ ] 只从 `@nerv-iip/ui` / `@nerv-iip/app-shell` 导入——无深导入
      （`@nerv-iip/ui/...`、`reka-ui`、`shadcn-vue`）。
- [ ] 无 org/environment/debug/source 元数据、无开发语言文案。
- [ ] **本页所属域的每个侧栏项都有 `icon`**——rail 里无首字回退（见 `blocks/app-shell.md`）。
- [ ] **无「用途说明」浮段、无冗余「本页 N」计数**——引导来自页头/列头/徽标/按钮/空态。
- [ ] **编号显示真实编码**（WO-…/WC-…/SKU-…），ID 本身即详情/速览点击目标——不是
      「查看X」按钮或「待接入」占位。
- [ ] `pnpm -C frontend --filter @nerv-iip/business-console typecheck && test && build`。

## 正例

`apps/business-console/src/pages/mes/operation-tasks.vue`（黄金标准页）：
`NvPageHeader:300` / `NvToolbar:319` / `NvDataTable:380`（`manual` 服务端分页 +
`:client-sort="false"` 页面重排 + 行内「报工」直达 + `NvRowActions` 收次动作）。
2026-07-11 实机走查以该页为 §2 行操作正例对照组（截图
`mes-operation-tasks-goldstandard.png`）。

## 反例

❌ **筛选状态不进 URL**（黄金标准页也有的遗留）：`/mes/operation-tasks` 选「状态=待开工」
URL 不变，进详情返回即丢——违反 `interaction-patterns.md` §5.3。出处：
`frontend/DESIGN/roadmaps/2026-07-11-ux-walkthrough-findings.md` §3.2 P1-6
（运行时 `URL_CHANGED=false`；`mes/operation-tasks.vue` 筛选全存本地 `ref`）。

❌ **高频动作全收下拉、行内零按钮**：维护工单列表 25 张真实工单，操作列每行只有
`NvRowActions` 下拉，一线高频「派工/开始/完成」要两跳——违反 `interaction-patterns.md`
§2。出处：同上 §3.4 P2-6（截图 `maintenance-work-orders.png`）。

## Enforcement（机器强制）

`apps/business-console/src/pages/goldStandardPages.contract.test.ts` 对已迁移页
allowlist 逐页跑上述清单的可机检项：必备区块 `NvPageHeader` + `NvDataTable`
（分页已集成进 `NvDataTable` manual 模式，不再要求独立分页块；`NvSectionCards`
非必备——按页判定）、禁遗留区块、禁裸 `<Table>`、禁深导入、禁开发语言文案。
stage-B 每迁一页就把它加进 allowlist，测试从此防漂移（含 codex 改动）。树-详情/矩阵页
（facilities/organization/skills）不含主表格，不在此清单，各有专属契约测试
（`facilities.test.ts` 等）。
