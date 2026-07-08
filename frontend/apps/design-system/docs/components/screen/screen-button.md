---
title: NvScreenButton 按钮
---

<script setup>
import { NvScreenButton } from '@nerv-iip/ui'
</script>

# NvScreenButton 按钮

大屏操作按钮,深底上的三种权重:`primary` 青色渐变带外辉光与顶部内高光,`secondary` 靛蓝细描边压在淡底上,`ghost` 仅一道细线。聚焦落高对比青色环,按下面板下沉一丝(无回弹)。基于独立的 `--sb-*` 工业蓝令牌。

## 三种权重

主操作用 `primary`,次操作用 `secondary`,弱化项用 `ghost`。

<ScreenDemo>
  <NvScreenButton>开始生产</NvScreenButton>
  <NvScreenButton variant="secondary">暂停</NvScreenButton>
  <NvScreenButton variant="ghost">复位</NvScreenButton>
</ScreenDemo>

```vue
<NvScreenButton>开始生产</NvScreenButton>
<NvScreenButton variant="secondary">暂停</NvScreenButton>
<NvScreenButton variant="ghost">复位</NvScreenButton>
```

## 禁用态

`disabled` 时降低不透明度并禁止指针,三种权重一致。

<ScreenDemo>
  <NvScreenButton disabled>开始生产</NvScreenButton>
  <NvScreenButton variant="secondary" disabled>暂停</NvScreenButton>
  <NvScreenButton variant="ghost" disabled>复位</NvScreenButton>
</ScreenDemo>

```vue
<NvScreenButton disabled>开始生产</NvScreenButton>
```

## 属性

| 属性       | 说明                                  | 类型                                  | 默认        |
| ---------- | ------------------------------------- | ------------------------------------- | ----------- |
| `variant`  | 按钮权重                              | `'primary' \| 'secondary' \| 'ghost'` | `'primary'` |
| `disabled` | 禁用                                  | `boolean`                             | `false`     |
| `type`     | 透传给原生 `<button>`,可提交/重置表单 | `'button' \| 'submit' \| 'reset'`     | `'button'`  |

## 插槽

| 插槽      | 说明                    |
| --------- | ----------------------- |
| `default` | 按钮文本,缺省为「确定」 |
