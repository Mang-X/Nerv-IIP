# 交互模式规范（Interaction Patterns）v1

> 状态：v1（前端第二波 W0 立标，2026-07）。
> 适用：`apps/business-console` 全部业务域；§6 适用 `apps/business-pda`。
> 定位：**后续 W2/W3 所有功能 issue 的交互验收依据**——每节 = 规则 + 判定 + 真实正/反例。反例出处均为当前代码，是"为什么立这条"的证据，同时构成各域整改清单（整改随各域 issue 落地，不在本文范围）。
> 关联（本文内以路径引用，不作链接）：列表基线 `frontend/DESIGN/patterns/pages/list-workbench.md`；主数据页型 `frontend/DESIGN/patterns/pages/master-data-templates.md`（**本文 §1 取代其 §5"Dialog 默认"的承载判定**，其余不变）；反馈规范 `frontend/DESIGN/patterns/feedback-and-notifications.md`；确认流 `frontend/DESIGN/patterns/flows/confirm-destroy.md`。

## 0. 怎么用本规范

- 每条判定都是**能回答"是/否"的问句**——答"否"即不合规，评审据此打回，不引申、不辩经。
- 代码片段分两类：**出处片段**（`路径:行号`，逐字摘自现网代码，`…` 为省略行）与**目标骨架**（标注"骨架/目标写法"，尚无现网实现）。
- 现网组件还做不到的能力集中在 §8（能力缺口）：**缺口不作为存量页面的打回理由**；缺口补齐前新页面按各节标注的"过渡下限"执行，补齐后按目标形态。
- 功能 issue 的验收标准直接引用 **§7 验收速查**（可整块粘贴）。

---

## 1. 表单承载分级：Dialog / Sheet / 独立页

现状痛点：**"一切皆 Dialog"**——建档、登记、报工、多段变更全部居中弹窗，字段一多就竖着挤、遮住列表、放不下动态行。

### 规则

| 承载           | 适用                                                                                                                                                 | 组件                                   |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------- |
| **Dialog**     | **≤3 个字段**的快捷操作：轻量登记、重命名、改期、单字段修正。纯确认（0~1 字段）用 `NvAlertDialog`（见 `flows/confirm-destroy.md` 与 §2 破坏性条款） | `NvDialog`                            |
| **Sheet 侧滑** | **4~8 个字段**，或**含动态行**（可增删的 `v-for` 行组），或填写时需对照列表/上文                                                                     | `NvSheet`（`sm:max-w-xl` 起）         |
| **独立页**     | **复杂多段/多步**：≥2 个分段标题或多个 `<form>`、向导步骤、含审批链/附件/子表、跨多聚合                                                              | 路由页（独立表单页或落在对象详情页内） |

### 判定

1. **数字段**：表单内可见输入控件数（`NvInput` / `NvSelect` / `DatePicker` / `Textarea` / `NvCheckbox` / 上传，**含只读回显字段**——只读字段同样占空间与认知）。≤3 → Dialog 可；4~8 → 必须 Sheet；>8 → 独立页。
2. **看结构**：出现动态行 → 至少 Sheet；出现 ≥2 个分段/多个 `<form>`/审批链面板/步骤态 → 独立页。
3. **反问**：「填这个表单需要对照列表或上文吗？」需要 → Sheet（不遮列表），不用居中 Dialog。

### 反例（现状证据，评审按此口径打回）

❌ **6 字段塞 Dialog** — 完工入库登记，`apps/business-console/src/pages/mes/receipts.vue:290`。工单号/成品/入库数量/单位成本/单位/登记时间共 6 个 `NvField`（300–325 行），开关变量名就叫 `receiptSheetOpen`——命名都承认该是 Sheet：

```vue
<NvDialog v-model:open="receiptSheetOpen">
  <NvDialogContent>
    <NvDialogHeader>
      <NvDialogTitle>登记完工入库</NvDialogTitle>
…
    <NvFieldGroup class="grid gap-3">
      <NvField>
        <NvFieldLabel for="receipt-work-order">工单号</NvFieldLabel>
        <NvInput id="receipt-work-order" v-model="form.workOrderId" readonly required />
      </NvField>
…（成品 / 入库数量 / 单位成本 / 单位 / 登记时间，共 6 个字段）
```

❌ **7 字段塞 Dialog** — 工单报工，`apps/business-console/src/pages/mes/work-orders/index.vue:558`（报工对象/工单号/工序任务/良品数/报废数/报工时间/完成当前工序；开关变量同样叫 `reportSheetOpen`）。

❌ **动态行塞 Dialog** — 发布工程变更，`apps/business-console/src/pages/engineering/eco.vue:312`：`sm:max-w-2xl` 的 `NvDialog` 里放受影响版本**动态行组**（`affectedVersions` 可增删，178–182 行）。按本节至少 Sheet；ECO 还带审批引用与多段信息，应独立页（见 §3 反例）。

❌ **主数据建档 5~6 字段全在 Dialog** — `master-data/units.vue:471`（新建计量单位 5 字段）、`:565`（新建换算关系 6 字段）、`quality/reason-codes.vue:241`（新建质量原因 5 字段）等。主数据域整改时以本节取代 `master-data-templates.md` §5 的"Dialog 默认"。

现网容器选型正确的锚点：NCR 处置表单落在 `NvSheet`（`quality/ncrs.vue:267`）——**Sheet 选型对**；但该抽屉同时塞下详情 + 两段表单，属 §3 反例。

### 目标骨架（≤3 字段 Dialog；现网暂无同规模真例）

```vue
<NvDialog v-model:open="rescheduleOpen">
  <NvDialogContent class="sm:max-w-md">
    <NvDialogHeader><NvDialogTitle>调整交期 · {{ row.workOrderId }}</NvDialogTitle></NvDialogHeader>
    <form class="grid gap-4" @submit.prevent="submitReschedule">
      <NvField><NvFieldLabel>新交期</NvFieldLabel><DatePicker v-model="form.dueDate" /></NvField>
      <NvField><NvFieldLabel>调整原因</NvFieldLabel><Textarea v-model="form.reason" required /></NvField>
      <NvDialogFooter>…取消 / 保存…</NvDialogFooter>
    </form>
  </NvDialogContent>
</NvDialog>
```

PDA 表单承载另见 §6.2（单 Sheet ≤3 字段 + 拆步）。

---

## 2. 行操作分级

现状痛点：高频动作藏进下拉菜单——角色每天点几十次的动作要先开菜单再选项。

### 规则

1. 每行**最多 1~2 个高频动作**提为行内按钮（`NvButton size="sm"`，主动作在前）；其余一律收进 `RowActions`（下拉菜单）。
2. **高频以角色主任务为准**：该页目标角色最常执行的动作（报工/处置/确认/收货…），1 次点击必达。
3. **唯一动作禁止进菜单**：整行只有一个可用动作时，它必须是行内按钮（进菜单是徒增一跳）。
4. **查看详情不占行内名额**：ID 列本身即详情入口（`list-workbench.md` 既有规则："ID 即点击目标"）。
5. **破坏性动作**（删除/停用/关闭/作废/回滚）：必须 `NvAlertDialog` 二次确认，且**原因必填**——确认框内含原因输入（`Textarea`，或原因码 `NvSelect` + 备注），为空时确认按钮 `disabled`；确认按钮用 `variant="destructive"`；原因随请求提交、进审计。

### 判定

- 「这页角色最高频的动作，从行上 1 次点击能触发吗？」
- 「整行只有一个动作时，它是不是行内按钮？」
- 「破坏性动作：有二次确认吗？确认框里**不填原因能不能点确认**？」

### 正例 — 高频动作行内直达，其余收菜单

出处 `apps/business-console/src/pages/mes/operation-tasks.vue:331`（黄金标准页）：

```vue
<template #cell-actions="{ row }">
  <div class="flex items-center justify-end gap-1">
    <NvButton
      v-if="showReportButton(row)"
      size="sm"
      type="button"
      @click="openRoute('/mes/work-orders', row)"
    >
      <ClipboardCheckIcon aria-hidden="true" />
      报工
    </NvButton>
    <RowActions :label="`工序任务操作 工序 ${row.operationSequence ?? ''}`">
      <!-- 查看当前SOP / 查看工单 / 呼叫质检 / 记录异常 —— 均收进菜单 -->
      <NvDropdownMenuItem :disabled="!canOpenSops(row)" @click="openSops(row)"
        >查看当前SOP</NvDropdownMenuItem
      >
    </RowActions>
  </div>
</template>
```

### 反例

❌ **唯一动作还收菜单** — NCR 列表整行只有「打开处置」一个动作，仍要先点开 `RowActions`（`quality/ncrs.vue:259`）：

```vue
<RowActions :label="`NCR 操作 ${row.code ?? ''}`">
  <NvDropdownMenuItem @click="openNcr(row)">打开处置</NvDropdownMenuItem>
</RowActions>
```

❌ **三个动作全收菜单、行内零按钮** — 主数据行操作 `apps/business-console/src/components/masterData/MasterDataRowActions.vue:80`：查看详情/编辑/停用全部在下拉。按本节：查看详情由编码列 ID 链接承担；编辑（或停用）按各页频率提行内。

❌ **破坏性确认无原因** — 停用确认只有说明文案、没有原因输入（`MasterDataRowActions.vue:120`）；关闭不合格品同（`quality/ncrs.vue:341`）。**v1 起新增的破坏性动作必须带原因输入**；存量随 W2/W3 各域 issue 补齐。

### 目标写法（原因必填）

```vue
<NvAlertDialog v-model:open="disableOpen">
  <NvAlertDialogContent>
    <NvAlertDialogHeader>
      <NvAlertDialogTitle>确认停用该物料？</NvAlertDialogTitle>
      <NvAlertDialogDescription>停用后将不能用于新建/计划，已有记录不受影响。</NvAlertDialogDescription>
    </NvAlertDialogHeader>
    <NvField>
      <NvFieldLabel for="disable-reason">停用原因 <span class="text-destructive">*</span></NvFieldLabel>
      <Textarea id="disable-reason" v-model="disableReason" required />
    </NvField>
    <NvAlertDialogFooter>
      <NvAlertDialogCancel>取消</NvAlertDialogCancel>
      <NvAlertDialogAction variant="destructive" :disabled="!disableReason.trim() || pending" @click="confirmDisable">
        确认停用
      </NvAlertDialogAction>
    </NvAlertDialogFooter>
  </NvAlertDialogContent>
</NvAlertDialog>
```

---

## 3. 列表-详情模式

现状痛点：列表-详情承载混用——工单有独立详情页，NCR/ECO 这类同等重量的对象却挤在抽屉里、甚至没有详情载体。

### 规则

| 详情重量   | 判定特征                                                                              | 承载                                                          |
| ---------- | ------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| **轻详情** | 只读、看完即走；无状态机、≤1 类写操作                                                 | 速览 `NvDialog`（只读 `dl`）或 `NvSheet` 抽屉——保列表上下文 |
| **重对象** | 有生命周期状态机、多段信息、≥2 类写操作、带审批/追溯/子表（工单 / NCR / ECO / 设备…） | **独立页** `/域/对象/[id]`，URL 可寻址                        |

1. **重对象详情必须 URL 直达**（可分享、可从通知与跨域链接跳入）。Dialog/抽屉不可寻址，不得作为重对象的唯一详情载体。
2. **分层允许**：重对象可另配轻量速览（列表处快看关键字段），但速览内必须有「进完整详情页」出口。
3. **同域不混用**：同一对象只允许一种详情承载；同域内同重量级对象的承载必须一致。

### 判定

- 「这个对象有生命周期/状态机吗？对它的写操作有 ≥2 类吗？」是 → 独立页。
- 「详情能复制 URL 发给同事直接打开吗？」重对象必须能。
- 「同一个域里两个同级对象，一个抽屉一个独立页？」→ 打回。

### 正例

✓ **工单 = 独立页 + 速览分层**：独立页 `pages/mes/work-orders/[workOrderId].vue`；列表与关联页用 `WorkOrderQuickView` 速览（只读 `NvDialog`），底部按钮跳完整页（`components/mes/WorkOrderQuickView.vue:41`）：

```ts
function openFull() {
  const id = workOrderId.value
  workOrderId.value = null
  if (id) void router.push(`/mes/work-orders/${encodeURIComponent(id)}`)
}
```

✓ **设备 = 独立页** `pages/equipment/[deviceAssetId].vue`。
✓ **轻详情 Dialog**：主数据只读详情（`MasterDataRowActions.vue:97`，只读 `dl` + 关闭按钮）——字段少、即看即关，合规。

### 反例

❌ **重对象塞抽屉** — NCR（状态机 + 处置 + 关闭 + 审批链 + 附件）整个塞进 `sm:max-w-xl` 的 `NvSheet`（`quality/ncrs.vue:267–364`）：详情、「提交处置」、「关闭不合格品」三合一挤在一条侧滑里，无 URL、不可分享、不可从通知直达。应为独立页 `/quality/ncrs/[ncrId]`（处置/关闭作为页内动作区）。

❌ **重对象没有详情载体** — ECO 只有创建 Dialog（`engineering/eco.vue:312`）和受影响版本抽屉（`:509`），既无独立页也无完整详情视图。

❌ **同类重对象承载不一致** — 同为重对象：工单已是独立页，NCR/ECO 仍是抽屉/无详情。平台内重对象承载应一致。

---

## 4. 操作后引导与查询失效

现状痛点：操作成功只有一个 toast（甚至一行常驻文字），用户不知道"刚创建的单在哪、下一步干什么"；跨域写操作后关联列表不刷新，要手动刷新才见结果。

### 4.1 成功态必答「下一步去哪」

**规则**

1. **产生新单据/新对象**的写操作（创建、登记、发布），成功态必须给出下一步出路，三选中**至少提供两项**：**［继续创建］**（重置表单留在原地）／**［查看详情］**（打开或跳转到刚创建的对象）／**［返回列表］**（关闭容器回列表）。
2. **高频连录**场景（收货、盘点、登记、连续建档）**必须含［继续创建］**。
3. 呈现形态按容器：Dialog/Sheet 表单 → 成功后容器内切成功态（对象编号 + 出路按钮），或关容器 + toast；独立页向导 → 成功步（结果面板 + 出路按钮）。toast 文案含对象编号（口径见 `feedback-and-notifications.md`）。
4. **豁免**：行内轻操作（确认报警、启停、搁置、解除）toast 即可，不要求三选。
5. 过渡下限：`notifySuccess` 当前不支持附带动作按钮（§8 能力项）——支持前，成功引导用**容器内成功态**或**成功后自动打开速览**实现，不等 toast。

**判定**

- 「创建成功那一刻，屏幕上有没有**可点击**的『查看它』或『再录一单』？」
- 「连续录 5 单，用户要重复点几次『新建』？」（有［继续创建］= 0 次）

**反例**

❌ 成功只有一行常驻文字、无任何出路（同时违反反馈规范"结果用 toast"）——完工入库表单内：

```vue
<p v-if="successMessage" class="text-sm text-success" role="status">{{ successMessage }}</p>
```

出处 `apps/business-console/src/pages/mes/receipts.vue:298`。创建类操作沿用「toast/文字即止 + 列表自刷新」即不合规——用户找不到刚才那单。

### 4.2 跨域写操作后的查询失效（Pinia Colada 键约定）

**事实基础（生成层）**：查询键由 `@nerv-iip/api-client` 生成（`packages/api-client/src/generated/@pinia/colada.gen.ts`）。`xxxQueryOptions` 的 `key` = `createQueryKey('<operationId>', options)`，形状为：

```ts
[{ _id: '<operationId>', baseUrl, query?, tags? }]
```

**键身份 = OpenAPI operationId（即 `_id`）**。全部约定围绕它展开：

1. **失效以 operationId 为单位**，用谓词匹配 `key[0]._id`。现行形态（保留为标准）——出处 `apps/business-console/src/composables/useBusinessQuality.ts:127`：

```ts
queryCache.invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleQualityNcrs') })
```

2. **写后必失效（同域）**：每个 `useMutation` 的 `onSuccess` 必须失效**本域所有读到该聚合的键**——list + detail + overview/summary。范例（`useBusinessMes.ts:375`）：

```ts
void invalidateMesQueries(queryCache, [
  'getBusinessConsoleMesOverview',
  'listBusinessConsoleMesOperationTasks',
  'getBusinessConsoleMesWipSummary',
  'listBusinessConsoleMesWorkOrders',
]).catch(ignoreBackgroundError)
```

3. **跨域失效**：写操作改动他域读模型时，在**同一个 `onSuccess`** 里一并失效他域 operationId；键清单写在发起域 composable，逐键注释目标域。**反例** —— 完工入库只失效 MES 两个键、不失效库存（`useBusinessMes.ts:856`）：

```ts
onSuccess() {
  void invalidateMesQueries(queryCache, [
    'listBusinessConsoleMesFinishedGoodsReceiptRequests',
    'getBusinessConsoleMesOverview',
  ]).catch(ignoreBackgroundError)
}
```

入库改变库存，但 `getBusinessConsoleInventoryAvailability` 等库存键不失效——用户切到库存页看到的是旧数据，要手动刷新才见货。

4. **手动 `refreshXxx()` 只服务用户显式刷新按钮**，不得替代 `onSuccess` 失效链。
5. **收口**：`isBusinessQuery` 目前在 7 个域 composable 各复制一份（useBusinessMasterData / Inventory / Mes / Quality / Planning / Scheduling / useProductEngineering）——上提共享工具、支持 `id | ids[]`（§8 能力项）。

**判定**

- 「这个 mutation 的 `onSuccess`，把**所有展示该数据的页面**的 operationId 都列了吗——包括别的域？」
- 「PR 描述里能说清这个写端点的失效清单吗？」

---

## 5. 空态 / 批量 / 筛选状态

### 5.1 空态必须带出路（CTA）

**规则**（文案口径继承 `master-data-templates.md` §0 第 5 条，本节把"出路"从文案升级为**可点击控件**）：

- 首次无数据：「还没有 {X}」+ **［+ 新建 {X}］按钮**（有创建能力）或「去 {上游页} 维护 →」链接（只读页）。
- 筛选无结果：「没有符合条件的 {X}」+ **［清空筛选］按钮**。

**现状与过渡下限**：`NvDataTable` 只有 `emptyMessage` 文案位、无 `#empty` 插槽（`packages/ui/src/components/pc/data-table/NvDataTable.vue:48`），按钮进不了空态 → §8 能力项（补 `#empty` 插槽，接 `@nerv-iip/ui` 已有的 `Empty` 组件族）。缺口补齐前，**文案式出路是过渡下限**——如 `master-data/units.vue:540`：

```vue
empty-message="还没有计量单位，点击「新建计量单位」创建第一条。"
```

补齐后，新页面必须用按钮 CTA。

**判定**：「空态界面上有没有一个**可点击**的下一步？（缺口期：文案有没有指名下一步在哪？）」

### 5.2 批量模式统一形态

**规则**：批量 = `NvDataTable` 内建选择系统，**禁止自造勾选列/悬浮动作条**：

- `selectable` 开勾选列（含全选/半选），`v-model:selected` 持有选中键，**`#bulk-actions` 插槽**放批量动作（上下文动作栏随选中数出现）。组件已实现——`NvDataTable.vue:53`（"Leading checkbox column + bulk bar"）、动作栏 `:568`、插槽 `:583`。
- 批量破坏性动作：同 §2（AlertDialog + 原因必填），确认文案**复述条数**（「将停用 12 条计量单位…」）。
- 批量结果分开汇总：「成功 8 条，失败 2 条」，失败行可定位（列表内标记或明细列表）。

**用法（组件真实 API）**：

```vue
<NvDataTable :rows="rows" row-key="workOrderId" selectable v-model:selected="selectedIds">
  <template #bulk-actions="{ selected }">
    <NvButton size="sm" variant="outline" @click="bulkRelease(selected)">批量下达</NvButton>
  </template>
</NvDataTable>
```

**现状**：组件能力完备，业务页**零使用**。需要批量的页（工单批量下达、报警批量确认、批量停用…）一律按本形态接入。

**判定**：「对多行做同一动作，是不是 `selectable` + `#bulk-actions` 做的？」「批量破坏性动作有没有条数复述 + 原因？」

### 5.3 筛选状态进 URL query

**规则**：列表页的筛选 / 搜索关键字 / 页码 / tab **双向同步** URL query：

- **进**：进入页面读 `route.query` 初始化筛选状态。
- **出**：筛选变更（防抖后）`router.replace({ query })` 写回——用 replace，不压历史栈。
- 键名 = facade filter 字段名（`status` / `keyword` / `workCenterId` / `page`…）；**默认值不写入**（`all`、空串、第 1 页时删除该键）。
- 跨页引导链接一律带业务上下文 query（既有做法上升为约定）：`router.push({ path: '/mes/downtime', query: { deviceAssetId } })`（`equipment/alarms.vue:130`）；目标页读 query 接住（读入示范：`barcode/scans.vue:84`）。

**正例（写回）** — `apps/business-console/src/pages/engineering/bom-analysis.vue:199`：

```ts
await router.replace({
  query: {
    ...route.query,
    kind: form.kind,
    view: form.view,
    effectiveDate: form.view === 'diff' ? undefined : effectiveDate,
…
```

**反例** — 工序执行页筛选全存 `ref`，离开返回即丢（`mes/operation-tasks.vue:64`）：

```ts
// --- Filters (live) ---
const keyword = ref('')
const statusFilter = ref('all')
const workCenterFilter = ref('all')
const shiftFilter = ref('all')
```

**判定（两问）**：「筛选后复制 URL 到新标签，看到同样的结果吗？」「点进详情再返回，筛选还在吗？」

---

## 6. PDA 移动条款（apps/business-pda）

### 6.1 数量/测量值必用 NumberKeyboard

- PDA 上**数量、测量值、计数**类录入必须用 `@nerv-iip/ui-mobile` 的 `NumberKeyboard`（`v-model` + `v-model:show` + `extraKey`（小数点），源码 `packages/ui-mobile/src/components/number-keyboard/NumberKeyboard.vue`）；触发字段设只读，防系统键盘弹出。
- **豁免**：扫码回填的只读字段；文本类录入（单号手输兜底）用普通输入。
- **现状**：组件已导出（`ui-mobile/src/index.ts:44`）、页面**零使用**——数量字段全是原生 `<input type="number">`。反例（`business-pda/src/pages/wms/count.vue:211`）：

```vue
<input
  v-model="countedQuantityText"
  data-testid="counted-quantity"
  type="number"
  inputmode="numeric"
…
```

良品数同（`mes/report.vue:365`）。

- **判定**：「PDA 上点数量字段，弹的是大按键数字键盘还是系统键盘？」

### 6.2 单 Sheet ≤3 输入字段，超出拆步

- 一个 `BottomSheet` 步骤内**可交互输入控件 ≤3**（含 `Picker` / `Stepper`，不含按钮与只读展示行）；超出必须拆步（配 §6.3 步骤条），或用扫码回填削减手输。
- **反例**：新建完工入库第 2 步一屏 4 个输入——SKU / 数量 / 单位成本 / 单位（`mes/receipt.vue:316` 注释即写明"录 SKU/数量/单位成本/单位"，字段区 366–437）。应拆步或扫码带出 SKU。
- **判定**：「这个 Sheet 步骤里用户要手输几个字段？>3 就拆。」

### 6.3 步骤提示必须全流程覆盖

- 多步流程必须挂**步骤条 `Steps`**（`steps: StepItem[]` + `current`，导出见 `ui-mobile/src/index.ts:21`）；每步 label 用业务话（选工单 → 录数量 → 确认）。**任一步骤界面上可见全部步骤名 + 当前位置**。
- 裸文本「第 x/y 步」不合规——只报位置、不报去向。反例（`mes/receipt.vue:323`、`mes/report.vue:245`）：

```vue
<!-- 只报位置、不报去向，用户不知道每步叫什么、下一步是什么 -->
<p class="text-xs text-muted-foreground">第 {{ currentStep }}/{{ totalSteps }} 步</p>
```

- **现状**：`Steps` 组件零使用。
- **判定**：「站在流程任意一步，屏幕能看出一共几步、现在哪步、下一步叫什么吗？」

### 6.4 触点 ≥44px

- 一切可点交互元素高度 **≥44px**。仓库标准 token `min-h-touch` = **48px**（`packages/ui-mobile/src/styles/mobile.css:16`），用它即达标；组件默认高度：`Cell` 48 / `ListRow` 56 / `TabBar` 48 / `MobileButton lg` 48。
- **主操作禁用** `MobileButton` 的 `sm`（32px）与 `md`（40px）档（`components/button/MobileButton.vue:35`）。
- `Stepper` 加减触点当前 32px（`components/stepper/Stepper.vue:48`）——组件整改见 §8；整改前数量增减优先走 NumberKeyboard。
- **判定**：「DevTools 量 computed height ≥44？或 class 带 `min-h-touch` / `h-12` 及以上？」

---

## 7. 验收速查（功能 issue 直接引用本节）

**表单承载（§1）**

- [ ] 字段数与承载匹配：≤3 → Dialog；4~8 或含动态行 → Sheet；多段/多步/审批链 → 独立页。

**行操作（§2）**

- [ ] 高频动作行内 ≤2、其余收 `RowActions`；唯一动作不进菜单；ID 列即详情入口。
- [ ] 破坏性动作有 `NvAlertDialog` 二次确认，且**原因必填**、随请求提交。

**列表-详情（§3）**

- [ ] 重对象详情是独立页、URL 可寻址（速览可另配，须带"进完整页"出口）；轻详情用速览/抽屉；同域同级承载一致。

**操作后引导（§4）**

- [ ] 创建类成功态给出 继续创建/查看详情/返回列表 中至少两项；高频连录含「继续创建」。
- [ ] 每个 mutation 的 `onSuccess` 列全失效 operationId：本域 list/detail/overview + 受影响他域；手动 refresh 不替代失效。

**空态/批量/筛选（§5）**

- [ ] 两种空态都有出路（新建 / 清空筛选；缺口期文案至少指名下一步）。
- [ ] 批量用 `selectable` + `#bulk-actions`；批量破坏性有条数复述 + 原因。
- [ ] 筛选/搜索/页码进 URL query：新标签同结果、返回不丢；跨页链接带上下文 query。

**PDA（§6）**

- [ ] 数量/测量值用 `NumberKeyboard`；单 Sheet 步骤 ≤3 输入；多步流程挂 `Steps` 且全程可见；触点 ≥44px（用 `min-h-touch`）。

---

## 8. 能力缺口（W2 能力项；不作为存量打回理由）

| 缺口                                                            | 现状出处                                                        | 目标                                                    |
| --------------------------------------------------------------- | --------------------------------------------------------------- | ------------------------------------------------------- |
| `NvDataTable` 空态只有 `emptyMessage` 文案位，无 `#empty` 插槽 | `packages/ui/src/components/pc/data-table/NvDataTable.vue:48` | 增 `#empty` 插槽，接 `Empty` 组件族渲染按钮 CTA（§5.1） |
| `notifySuccess` 不支持附带动作按钮（「查看 X」）                | `apps/business-console/src/utils/notify.ts`                     | 评估 toast action；支持前用容器内成功态（§4.1）         |
| `isBusinessQuery` 在 7 个域 composable 重复定义                 | `useBusinessQuality.ts:72` 等                                   | 上提共享模块，签名支持 `id \| ids[]`（§4.2）            |
| `Stepper` 触点 32px 不达标                                      | `packages/ui-mobile/src/components/stepper/Stepper.vue:48`      | 触点提到 ≥44px，或数量录入改走 `NumberKeyboard`（§6.4） |
| `Steps` / `NumberKeyboard` 已导出零接入                         | `ui-mobile/src/index.ts:21,44`                                  | 随 PDA W2/W3 issue 按 §6 接入                           |

---

> 新页面开发顺序：先按 `master-data-templates.md` §6 判定 IA 载体 → 本文 §1–§5 定交互形态 →（PDA 页）§6 → 提交前对照 §7 逐条自检。
