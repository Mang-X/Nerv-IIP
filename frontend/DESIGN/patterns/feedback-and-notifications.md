# 反馈与通知规范（Feedback & Notifications）

> 业务前端**操作反馈的单一规则**。所有页面/表单必须遵守；新页面照此做，评审照此卡。
> 配套实现：`apps/business-console/src/utils/notify.ts`（`notifySuccess` / `notifyError`）。

## 规则

**一句话原则：「操作结果」用 toast（瞬时、不残留、不占布局）；「字段校验」用内联
（红框+汇总，与字段共存）。两者不混用。**

三类反馈，各归各位：

| 反馈类型         | 例子                                                         | 用什么                                                                                      | 不能用什么                                              |
| ---------------- | ------------------------------------------------------------ | ------------------------------------------------------------------------------------------- | ------------------------------------------------------- |
| **操作结果**     | 创建成功、更新成功、停用成功；保存失败、网络错误、服务器 5xx | **toast**（`notifySuccess` / `notifyError`）                                                | ❌ 页面/弹窗内常驻 `<p>` 文字（会残留、不显眼、占布局） |
| **字段级校验**   | 必填未填、格式不对                                           | **内联**：字段红框（`:data-invalid`）+ 表单顶部汇总「请完整填写带 \* 的必填项（已标红）。」 | ❌ toast（校验要和字段持续共存，指向具体位置）          |
| **列表加载失败** | 表格数据拉取失败                                             | 表格区**内联**一条（紧邻表格，属"区域状态"非"操作结果"）                                    | —                                                       |

必守细则：

1. **请求失败 → `notifyError(error)`**：在 submit/action 的 `try/catch` 里调用；它把
   `downstream-invalid-response`、`502`、`Failed to fetch` 等**开发术语映射成人话**
   （「服务暂时不可用，请稍后重试。」），绝不把原始技术串甩给用户。
2. **请求成功 → `notifySuccess('xxx 已创建/已更新')`**。
3. **弹窗内提交失败**：toast 报错 + **弹窗保持打开**让用户改正重试；**不在弹窗里堆常驻
   错误文字**（这是本规范要根除的「残留」反例）。
4. **打开弹窗即重置瞬时态**：`showErrors`、上一次的报错等都清掉，避免跨次残留。
5. **校验不通过不发请求**：`if (!canCreate) { showErrors = true; return }`——只点亮内联
   红框+汇总，不弹 toast（toast 留给"请求结果"）。
6. **文案说人话**：业务语言、动宾短句、含对象名（「物料「智能网关」已更新。」），不出现
   operationId/字段名/`#`号/英文错误码。
7. 模板里**只保留**：字段 `:data-invalid` + 顶部校验汇总 + 列表加载失败条。**删除**
   弹窗内/页面内的「创建/更新结果」常驻 `<p>`。

## 判定

- 「这条反馈说的是**请求结果**还是**字段问题**？」结果 → toast；字段 → 内联。混用即打回。
- 「提交失败后，弹窗里有没有留下一段错误文字？」有 → 打回（应 toast + 弹窗保持打开）。
- 「必填空着点提交，发请求了吗？弹 toast 了吗？」任一"是" → 打回。
- 「toast 文案里有 `502` / operationId / 英文错误码吗？」有 → 打回（未走 `notifyError` 映射）。

## 正例

现网实现与用法：映射逻辑 `apps/business-console/src/utils/notify.ts`
（`friendlyErrorMessage` 把 502/503/network error 归一成中文）；调用示范
`apps/business-console/src/pages/master-data/units.vue:432/443`
（`notifySuccess(`计量单位「…」已创建/已更新。`)`，成功才关弹窗）。

标准范式（照抄）：

```ts
import { notifyError, notifySuccess } from '@/utils/notify'

async function submit() {
  if (!canCreate.value) {
    showErrors.value = true
    return
  } // 字段校验：内联，不发请求
  try {
    editingCode.value ? await actions.update(editingCode.value, patch()) : await create(body())
    notifySuccess(`${entityName}「${form.name}」已${editingCode.value ? '更新' : '创建'}。`)
    resetForm()
    open.value = false // 成功才关弹窗
  } catch (error) {
    notifyError(error) // 失败：toast 人话，弹窗不关、无残留
  }
}
function openCreate() {
  editingCode.value = null
  resetForm()
  showErrors.value = false
  open.value = true
}
```

## 反例（评审打回）

有现网证据的：

- ❌ **成功提示做成表单内常驻文字**：`apps/business-console/src/pages/mes/receipts.vue:337`
  `<p v-if="successMessage" class="text-sm text-success" role="status">`——文字残留、用户
  找不到刚创建的单（出处：现网代码 + 走查记录
  `frontend/DESIGN/roadmaps/2026-07-11-ux-walkthrough-findings.md` §3.2 P1-5）。
- ❌ **请求失败落成页面常驻错误条 + 原始 error.message 直出**：WMS 入库页把
  创建/完成 mutation 的错误合进 `errorMessage`，渲染为常驻
  `<p role="alert">`（`apps/business-console/src/pages/wms/inbound.vue:311`，现网代码），
  且 `formatError`（`:252`）直接返回 `error.message` 原文——操作结果应走 `notifyError`
  toast（人话映射），页面内联条只留给「列表加载失败」这一类区域状态。

规则同型的通用打回口径（无需逐一举证）：

- ❌ 把 `downstream-invalid-response` / `Error: 502` 直接显示给用户。
- ❌ 必填没填只弹 toast 不标红——用户不知道是哪个字段。
