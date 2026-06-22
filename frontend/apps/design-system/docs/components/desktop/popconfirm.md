---
title: Popconfirm 气泡确认
---

<script setup>
import { PopconfirmPro, ButtonPro, messagePro } from '@nerv-iip/ui'
</script>

# Popconfirm 气泡确认

行内危险操作或主操作的二次确认，气泡锚定在触发元素旁，比整页对话框更轻量。

## 危险操作

默认 `danger` 基调，确认按钮为红色。

<Demo>
  <PopconfirmPro
    title="确认删除该工单？"
    description="删除后不可恢复。"
    confirm-text="删除"
    @confirm="messagePro.error('工单已删除')"
  >
    <ButtonPro variant="outline" size="sm">删除工单</ButtonPro>
  </PopconfirmPro>
</Demo>

```vue
<PopconfirmPro
  title="确认删除该工单？"
  description="删除后不可恢复。"
  confirm-text="删除"
  @confirm="messagePro.error('工单已删除')"
>
  <ButtonPro variant="outline" size="sm">删除工单</ButtonPro>
</PopconfirmPro>
```

## 主操作确认

设置 `confirm-tone="brand"`，确认按钮使用品牌色。

<Demo>
  <PopconfirmPro
    title="确认下发到产线？"
    confirm-text="下发"
    confirm-tone="brand"
    @confirm="messagePro.success('已下发排产')"
  >
    <ButtonPro variant="brand" size="sm">下发排产</ButtonPro>
  </PopconfirmPro>
</Demo>

```vue
<PopconfirmPro
  title="确认下发到产线？"
  confirm-text="下发"
  confirm-tone="brand"
  @confirm="messagePro.success('已下发排产')"
>
  <ButtonPro variant="brand" size="sm">下发排产</ButtonPro>
</PopconfirmPro>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `title` | 确认标题 | `string` | `确认执行该操作？` |
| `description` | 补充描述 | `string` | — |
| `confirmText` | 确认按钮文案 | `string` | `确定` |
| `cancelText` | 取消按钮文案 | `string` | `取消` |
| `confirmTone` | 确认按钮基调 | `brand \| danger` | `danger` |
| `loading` | 确认按钮加载态（受控时） | `boolean` | `false` |
| `open` | 受控开关（`v-model:open`） | `boolean` | — |

## 事件

| 事件 | 说明 |
|---|---|
| `confirm` | 点击确认时触发 |
| `cancel` | 点击取消时触发 |
