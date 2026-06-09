# 反馈与通知规范（Feedback & Notifications）

> 业务前端**操作反馈的单一规则**。所有页面/表单必须遵守；新页面照此做，评审照此卡。
> 配套实现：`apps/business-console/src/utils/notify.ts`（`notifySuccess` / `notifyError`）。

## 1. 一句话原则

**「操作结果」用 toast（瞬时、不残留、不占布局）；「字段校验」用内联（红框+汇总，与字段共存）。两者不混用。**

## 2. 三类反馈，各归各位

| 反馈类型 | 例子 | 用什么 | 不能用什么 |
|---|---|---|---|
| **操作结果** | 创建成功、更新成功、停用成功；保存失败、网络错误、服务器 5xx | **toast**（`notifySuccess` / `notifyError`） | ❌ 页面/弹窗内常驻 `<p>` 文字（会残留、不显眼、占布局） |
| **字段级校验** | 必填未填、格式不对 | **内联**：字段红框（`:data-invalid`）+ 表单顶部汇总「请完整填写带 * 的必填项（已标红）。」 | ❌ toast（校验要和字段持续共存，指向具体位置） |
| **列表加载失败** | 表格数据拉取失败 | 表格区**内联**一条（紧邻表格，属"区域状态"非"操作结果"） | — |

## 3. 必守细则

1. **请求失败 → `notifyError(error)`**：在 submit/action 的 `try/catch` 里调用；它把 `downstream-invalid-response`、`502`、`Failed to fetch` 等**开发术语映射成人话**（「服务暂时不可用，请稍后重试。」），绝不把原始技术串甩给用户。
2. **请求成功 → `notifySuccess('xxx 已创建/已更新')`**。
3. **弹窗内提交失败**：toast 报错 + **弹窗保持打开**让用户改正重试；**不在弹窗里堆常驻错误文字**（这是本次要根除的「残留」反例）。
4. **打开弹窗即重置瞬时态**：`showErrors`、上一次的报错等都清掉，避免跨次残留。
5. **校验不通过不发请求**：`if (!canCreate) { showErrors = true; return }`——只点亮内联红框+汇总，不弹 toast（toast 留给"请求结果"）。
6. **文案说人话**：业务语言、动宾短句、含对象名（「物料「智能网关」已更新。」），不出现 operationId/字段名/`#`号/英文错误码。

## 4. 标准范式（照抄）

```ts
import { notifyError, notifySuccess } from '@/utils/notify'

async function submit() {
  if (!canCreate.value) { showErrors.value = true; return }   // 字段校验：内联，不发请求
  try {
    editingCode.value
      ? await actions.update(editingCode.value, patch())
      : await create(body())
    notifySuccess(`${entityName}「${form.name}」已${editingCode.value ? '更新' : '创建'}。`)
    resetForm(); open.value = false                            // 成功才关弹窗
  }
  catch (error) {
    notifyError(error)                                         // 失败：toast 人话，弹窗不关、无残留
  }
}
function openCreate() { editingCode.value = null; resetForm(); showErrors.value = false; open.value = true }
```

模板里**只保留**：字段 `:data-invalid` + 顶部校验汇总 + 列表加载失败条。**删除**弹窗内/页面内的「创建/更新结果」常驻 `<p>`。

## 5. 反例（评审打回）

- ❌ 弹窗里 `<p v-if="createError">{{ createError }}</p>` —— 提交失败后文字残留、又不显眼。
- ❌ 把 `downstream-invalid-response` / `Error: 502` 直接显示给用户。
- ❌ 成功提示做成页面绿条常驻，占布局还要手动清。
- ❌ 必填没填只弹 toast 不标红 —— 用户不知道是哪个字段。
