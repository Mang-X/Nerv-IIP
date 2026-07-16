# Flow: Create Dialog（弹窗建档）

通过模态表单创建新实体的标准流。**仅适用于 ≤3 字段的轻量建档**——承载分级
（Dialog / Sheet / 独立页）以 `../interaction-patterns.md` §1 为准：4~8 字段或含动态行
必须 `NvSheet`，复杂多段/多步走独立页。本文的流程约定（开关/校验/反馈/成功引导）对
Dialog 与 Sheet 同样适用。

## 规则

1. **开关**：`v-model:open` 控制；触发按钮在工具条/页头 `#actions`（`NvDialogTrigger
as-child` 或显式 `open = true`）。
2. **表单在 `NvDialogContent` 内**：`<form @submit.prevent>` 包住字段 + footer；确认按钮
   `type="submit"`，取消 `type="button"`；`pending` 时两个按钮都 disabled。
3. **校验时机（对齐 `../feedback-and-notifications.md`）**：点提交才标红——`showErrors`
   门控 `:data-invalid`；必填空着提交 → `if (!canSubmit) { showErrors = true; return }`，
   点亮内联红框 + 顶部汇总，**不发请求、不弹 toast**。
4. **打开弹窗即重置瞬时态**：`showErrors`、上次报错、表单值全清，避免跨次残留。
5. **结果反馈**：成功 → `notifySuccess('{实体}「{名称}」已创建。')` + **成功才关弹窗 +
   reset**；失败 → `notifyError(error)`（映射成人话）+ **弹窗保持打开**让用户改正；
   不在弹窗内堆常驻错误 `<p>`。
6. **成功态给下一步出路**（`../interaction-patterns.md` §4.1）：产生新单据/新对象的创建，
   成功时至少给两项：［继续创建］/［查看详情］/［返回列表］；高频连录必含［继续创建］。
7. **写后失效**：mutation 的 `onSuccess` 失效本域 + 受影响他域的查询键
   （`../interaction-patterns.md` §4.2），不靠用户手动刷新。
8. 字段/分段/编码只读等表单细则见 `../pages/master-data-templates.md` §5。

## 判定

- 「可见输入控件（含只读回显）超过 3 个吗？」超 → 不许用 Dialog，改 `NvSheet` 或独立页。
- 「必填空着点提交，发请求了吗？弹 toast 了吗？」任一"是" → 打回（应内联标红且不发请求）。
- 「提交失败后弹窗关了吗？错误文字残留在弹窗里吗？」关了 / 残留 → 打回。
- 「创建成功那一刻，屏幕上有可点击的『查看它』或『再录一单』吗？」

## 正例

`apps/business-console/src/pages/master-data/units.vue:535`（新建计量单位，
`NvDialog v-model:open="createOpen"` + `NvDialogTrigger as-child`），成功路径
`:443` `notifySuccess(`计量单位「${body.name}」已创建。`)`；反馈实现
`apps/business-console/src/utils/notify.ts`。
（该弹窗 5 字段超出 Dialog 承载上限，属下方反例的整改对象——流程照抄、承载勿抄。）

最小骨架（≤3 字段）：

```vue
<NvDialog v-model:open="open">
  <NvDialogTrigger as-child>
    <NvButton type="button">新建</NvButton>
  </NvDialogTrigger>
  <NvDialogContent class="sm:max-w-md">
    <NvDialogHeader>
      <NvDialogTitle>新建{实体}</NvDialogTitle>
      <NvDialogDescription>带 * 为必填项。</NvDialogDescription>
    </NvDialogHeader>
    <form class="grid gap-4" @submit.prevent="submit">
      <NvField :data-invalid="(showErrors && !form.name.trim()) || undefined">
        <NvFieldLabel for="entity-name">名称 <span class="text-destructive">*</span></NvFieldLabel>
        <NvInput id="entity-name" v-model="form.name" required />
      </NvField>
      <NvDialogFooter>
        <NvButton variant="outline" type="button" :disabled="pending" @click="open = false">取消</NvButton>
        <NvButton type="submit" :disabled="pending">保存</NvButton>
      </NvDialogFooter>
    </form>
  </NvDialogContent>
</NvDialog>
```

```ts
async function submit() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  } // 校验：内联，不发请求
  try {
    await create(body())
    notifySuccess(`{实体}「${form.name}」已创建。`)
    resetForm()
    open.value = false // 成功才关
  } catch (error) {
    notifyError(error) // 失败：toast 人话，弹窗不关
  }
}
function openCreate() {
  resetForm()
  showErrors.value = false
  open.value = true
}
```

## 反例

❌ **5~6 字段建档塞居中 Dialog** —— `master-data/units.vue`（新建计量单位 5 字段、新建
换算关系 6 字段）、`quality/reason-codes.vue`（新建原因 5 字段）；完工入库 6 字段
（`mes/receipts.vue`，运行时 inputCount=6）、工单报工 7 字段（`mes/work-orders/index.vue`）。
违反承载分级 §1（4~8 字段应 `NvSheet`）。出处：
`frontend/DESIGN/roadmaps/2026-07-11-ux-walkthrough-findings.md` §3.2 P1-1/P1-2、
§3.3 P2-1（截图 `mes-receipts-dialog-6fields.png`、`masterdata-units.png`）。

❌ **成功态只有一行常驻文字、无出路** —— 完工入库表单内
`<p v-if="successMessage" role="status">`（`mes/receipts.vue:337`，现网代码），同时违反
反馈规范「结果用 toast」与 §4.1 成功引导。出处：同上 §3.2 P1-5。
