# 反馈与通知规范（Feedback & Notifications）

> 业务前端**操作反馈的单一规则**。所有页面/表单必须遵守；新页面照此做，评审照此卡。
> 配套实现：`apps/business-console/src/utils/notify.ts`
> （`notifySuccess` / `notifyError` / `notifyOperationFailure`）。

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
   1b. **服务端领域消息要透传，通用 HTTP 文案要映射**（分层透传，MAN-691 / #1259）：后端明确
   拒绝这次操作时给的**领域理由**（中文、可行动，如「工单缺少生产版本，无法排程」「方案已被
   后续方案取代」）是用户唯一能据以行动的信息，**必须原样上屏**，不许被兜底文案吞掉；而
   `Internal Server Error` / `502` / 英文 problem title 这类**通用 HTTP 文案**仍按第 1 条
   映射成人话，**原文只进 `console.error`**。写操作用
   `notifyOperationFailure('发布失败', error, '发布失败，请稍后重试')`：它先取服务端消息
   （信封 `message` → RFC7807 `detail`/`title` → 字段校验 `errors`），中文领域消息拼成
   「发布失败：<服务端消息>」，识别得出的技术串走 `friendlyErrorMessage` 映射，都取不到才用
   调用方的领域兜底文案。
   ⚠️ 别只判 `error instanceof Error`：generated client 在 `throwOnError` 下抛出的是**解析后的
   响应体对象**，那样写会把所有 HTTP 失败（含 500）吞成猜测性文案。
   1c. **透传三入口同源，不许各写一套**（MAN-700 / #1289 全量铺开）：
   `notifyOperationFailure(动作, error, 兜底)` 用于**写操作**（带「哪个动作失败了」前缀）；
   `notifyError(error, 兜底)` 用于**没有明确动作名的失败**（读面加载失败等），走同一条透传链、
   只是不加前缀；`inlineErrorMessage(error, 兜底)` 用于**行内错误态**（列表加载失败条、弹窗内
   `submitError`），返回的就是 toast 会说的那句话，杜绝「toast 说人话、行内条却是
   `Internal Server Error`」的两套口径。
   另有两个**不接 error、只收写死中文**的入口：`notifySuccess(文案)` 与
   `notifyWarning(文案)`（后者用于「请求成功但业务结果不是用户想要的那一档」，如
   「转单成功返回但缺少有效价源」）。五个入口都在
   `apps/business-console/src/utils/notify.ts`——**业务页一律经它们，不直接调 `toast.*`**。
   1d. **要按「哪一类失败」分叉时用状态码，不要用消息文本**（MAN-698 批次 A）：页面偶尔需要把
   某一类失败渲染成**语义空态**而非通用失败条（如 403 → 「无权限」空态）。判定一律走
   `isForbiddenError(error)` / `errorStatusCode(error)`（同在 `utils/notify.ts`）——它们从
   拦截器挂在 error 上的 `response.status`、RFC7807 `status`、包装后的 `statusCode` 里取码，
   取不到才退回文本匹配。**别写 `error instanceof Error && error.message.includes('403')`**：
   generated client 抛的是响应体对象，这条判定对真实 403 永远不成立，「无权限」空态会
   静默退化成普通失败态（质检待检任务页就这么错了一版）。其余失败仍交给
   `inlineErrorMessage` / `notifyError` 走同一条透传链，不要在页面里另写一句兜底文案。

   门禁：`apps/business-console/src/pages/errorTransparency.contract.test.ts` 扫全仓源码，
   拦①裸 `instanceof Error` 判错误形状 ②直调 `toast.error/success/warning/info`
   ③各业务域必须有页面走 `notifyOperationFailure`。少数例外（`scheduling.vue` 的写死文案等）
   在该文件的 allowlist 里逐条写明理由。

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
- 「toast 文案里有 `502` / operationId / 英文错误码吗？」有 → 打回（未走 `notifyError` /
  `notifyOperationFailure` 映射）。
- 「后端明确给了中文领域拒绝理由，界面显示的却是『操作失败，请稍后重试』吗？」是 → 打回
  （领域消息被吞，用户不知道到底哪儿不满足）。

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
- ❌ **把写操作的 error ref 并进页面常驻错误条**：需求与计划工作台把
  `runMrpError` / `acceptSuggestionError` 等一起塞进 `errorMessage`，于是 RunMrp 一个 500 就把
  `Internal Server Error` 常驻在页面上；同一页的 `submitMrpRun` 还没有 `try/catch`，异常逃逸后
  **弹框永远关不掉**（MAN-700 / #1289 已修）。写操作失败一律 toast，弹框保持可改可重试，成功才关。

规则同型的通用打回口径（无需逐一举证）：

- ❌ 把 `downstream-invalid-response` / `Error: 502` 直接显示给用户。
- ❌ 必填没填只弹 toast 不标红——用户不知道是哪个字段。
