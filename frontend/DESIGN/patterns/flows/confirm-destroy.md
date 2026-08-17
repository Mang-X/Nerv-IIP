# Flow: Confirm Destroy（破坏性动作二次确认）

不可逆动作（删除/停用/关闭/作废/回滚/撤销）的两步确认流。
破坏性条款的交互验收口径以 `../interaction-patterns.md` §2 第 5 条为准，本文是其实现流。

## 规则

1. **一律 `NvAlertDialog`** —— 不用 `NvDialog`、不用 `window.confirm`。
2. **触发只开确认框**：行内按钮 / `NvDropdownMenuItem variant="destructive"` 只负责
   `target = row; confirmOpen = true`，不直接调 API。
3. **API 调用发生在确认动作里**（`NvAlertDialogAction` 的 click handler）；确认按钮
   `variant="destructive"`，`pending` 时 disabled。
4. **原因必填**（2026-07 W0 起对新增破坏性动作强制）：确认框内含原因输入
   （`NvInput`，或原因码 `NvSelect` + 备注），**纯空白不算填**（判定一律 `trim()`），
   为空时确认按钮 `disabled`；原因随请求提交、进审计。存量随各域 issue 补齐。
   `maxlength` 与服务端上限对齐（主数据生命周期原因为 500）。
5. **单实例声明在页面层**：确认框放 `v-for` 外，由 `target` ref 指向当前行；不塞进表格组件。
6. **Cancel 不禁用**：API 调用期间用户可以放弃等待（`NvAlertDialogCancel` 不跟 `pending`）。
7. **`NvAlertDialogDescription` 不可省**：说清后果（「停用后将不能用于新建/计划，
   已有记录不受影响。」），无障碍必需。
8. **结果走 toast**（`notifySuccess`/`notifyError`，见 `../feedback-and-notifications.md`），
   成功后关框、清 `target`。
9. **批量破坏性动作**同样走本流，确认文案**复述条数**（「将停用 12 条计量单位…」，
   见 `../interaction-patterns.md` §5.2）。

## 判定

- 「点触发按钮时发请求了吗？」发了 → 打回（少了确认步）。
- 「确认框里**不填原因能不能点确认**？」能 → 不合规（规则 4）。
- 「确认框是不是在 `v-for` 里？」是 → 打回（N 行 N 个实例）。
- 「操作进行中还能取消吗？」Cancel 被 disabled → 打回。

## 正例

**可整体照抄的现网锚点**：`apps/business-console/src/pages/master-data/workers.vue`
——确认框声明在**页面层**、由 `disableTarget` / `enableTarget` 指向当前行（规则 5），
停用与恢复各一个 `NvAlertDialog`，含后果描述、原因必填、`disabled` 门禁与 toast 结果（#878）。

**只可照抄「原因必填」这一段、不可照抄容器结构**：
`apps/business-console/src/components/masterData/MasterDataRowActions.vue`
——它自身包含 `NvAlertDialog`，又在 `units.vue` / `skus.vue` / `partners.vue` 等
`NvDataTable` 的 `#cell-actions` 里**按行实例化**，实质是 N 行 N 个确认框，**违反规则 5**。
这是 #878 之前就存在的结构，本次只补齐了原因必填，未做承载收敛；作为**存量例外**登记，
收敛到页面层单实例由 #1591 跟踪。现有组件测试用 stub 抹平了弹层，**测不出**这个结构问题——
引用本组件时请自行核对承载层级。

原因必填部分的契约由 `MasterDataRowActions.test.ts`（输入 / 禁用 / 提交 / 重置 / 失败保留）钉住。

骨架（含原因必填；与 `interaction-patterns.md` §2 目标写法一致）：

```vue
<script setup lang="ts">
const target = ref<Entity | null>(null)
const confirmOpen = ref(false)
const reason = ref('')
const pending = ref(false)

function openConfirm(entity: Entity) {
  target.value = entity
  // 每次打开都清空：上一条原因不能被当成这一次的理由带进审计。
  reason.value = ''
  confirmOpen.value = true
}

async function confirmDisable() {
  if (!target.value || !reason.value.trim()) return
  pending.value = true
  try {
    await disableEntity(target.value.id, { reason: reason.value.trim() })
    notifySuccess(`「${target.value.name}」已停用。`)
    confirmOpen.value = false
    target.value = null
  } catch (error) {
    notifyError(error)
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <!-- 触发（行内按钮或菜单项）只开框 -->
  <NvDropdownMenuItem variant="destructive" @click="openConfirm(row)">停用</NvDropdownMenuItem>

  <!-- 单实例，v-for 外 -->
  <NvAlertDialog v-model:open="confirmOpen">
    <NvAlertDialogContent>
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>确认停用「{{ target?.name }}」？</NvAlertDialogTitle>
        <NvAlertDialogDescription
          >停用后将不能用于新建/计划，已有记录不受影响。</NvAlertDialogDescription
        >
      </NvAlertDialogHeader>
      <NvField>
        <NvFieldLabel for="disable-reason"
          >停用原因 <span class="text-destructive">*</span></NvFieldLabel
        >
        <!-- 本库无 Textarea 组件；原因用 NvInput，maxlength 与服务端上限对齐。 -->
        <NvInput id="disable-reason" v-model="reason" required :maxlength="500" />
        <NvFieldDescription>原因会记入审计，可按对象回溯。</NvFieldDescription>
      </NvField>
      <NvAlertDialogFooter>
        <NvAlertDialogCancel>取消</NvAlertDialogCancel>
        <NvAlertDialogAction
          variant="destructive"
          :disabled="!reason.trim() || pending"
          @click="confirmDisable"
        >
          确认停用
        </NvAlertDialogAction>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
```

## 反例

❌ **破坏性确认无原因输入、原因不入审计** —— 历史形态：`/master-data/units` 停用确认框只有
说明文案 + 取消/确认停用，运行时抓取 reasonInputs=0、可直接点确认；质量「关闭不合格品」同。
出处：`frontend/DESIGN/roadmaps/2026-07-11-ux-walkthrough-findings.md` §3.1 P0-2
（实机确认 + 截图 `masterdata-disable-confirm-no-reason.png`）。
**主数据与质量两处均已整改**（质量关单原因已在 NCR 关闭表单必填；主数据停用/启用见 #878），
本条保留为形态反例。

❌ **原因由代码写死、用户填不了** —— 历史形态：`master-data/product-categories.vue` /
`skill-catalog.vue` 的停用把 `'不再使用'` 当原因直接发出去，审计里每条理由都一样，等于没有
理由。**两页均已整改**（#1595）：确认框内含原因必填输入，空或纯空白时确认按钮 `disabled`，
提交用户填写的原因；契约由各自的 `*.test.ts` 钉住。本条保留为形态反例——**"字段填了" 不等于
"有理由"**，代填与写死同样不合规。
