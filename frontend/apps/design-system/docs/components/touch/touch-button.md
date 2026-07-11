---
title: NvTouchButton 触控按钮
---

<script setup>
import { NvTouchButton } from '@nerv-iip/ui'
import { CheckCircleIcon, PauseIcon, BellRingIcon } from 'lucide-vue-next'
</script>

# NvTouchButton 触控按钮

工位一体机的大触控按钮：**≥56px** 触控目标、动作语义色、按下整体缩放 0.97 的强反馈。区别于 PC 紧凑的 NvButton 与移动贴近原生的 NvMobileButton——它为"戴手套也点得准、一眼看到该点哪"而放大。

## 变体

按动作语义选色：`brand` 完工、`success` 报工、`warning` 暂停、`destructive` 危险、`outline` / `ghost` 次级。

<Demo>
  <NvTouchButton variant="success"><template #leading><CheckCircleIcon aria-hidden="true" /></template>报工</NvTouchButton>
  <NvTouchButton variant="brand">完工</NvTouchButton>
  <NvTouchButton variant="warning"><template #leading><PauseIcon aria-hidden="true" /></template>暂停</NvTouchButton>
  <NvTouchButton variant="outline"><template #leading><BellRingIcon aria-hidden="true" /></template>呼叫</NvTouchButton>
  <NvTouchButton variant="ghost">复位</NvTouchButton>
</Demo>

```vue
<NvTouchButton variant="success">
  <template #leading><CheckCircleIcon /></template>
  报工
</NvTouchButton>
<NvTouchButton variant="warning">暂停</NvTouchButton>
```

## 尺寸

`md` 44px、`lg` 56px（默认）、`xl` 72px——工位主操作用 `xl`，副操作用 `lg`。加 `block` 占满整行。

<Demo>
  <NvTouchButton size="md">md · 44px</NvTouchButton>
  <NvTouchButton size="lg">lg · 56px</NvTouchButton>
  <NvTouchButton size="xl">xl · 72px</NvTouchButton>
</Demo>

<Demo block>
  <NvTouchButton variant="success" size="xl" block>整宽报工</NvTouchButton>
</Demo>

## 加载态

`loading` 时显示环形加载器并禁止重复点击（`aria-busy`）。

<Demo>
  <NvTouchButton variant="brand" size="lg" loading>提交中</NvTouchButton>
  <NvTouchButton size="lg" disabled>禁用</NvTouchButton>
</Demo>

## 属性

| 属性       | 说明         | 类型                                                                        | 默认      |
| ---------- | ------------ | --------------------------------------------------------------------------- | --------- |
| `variant`  | 动作语义变体 | `brand \| default \| success \| warning \| destructive \| outline \| ghost` | `default` |
| `size`     | 触控尺寸     | `md \| lg \| xl`                                                            | `lg`      |
| `block`    | 是否整宽     | `boolean`                                                                   | `false`   |
| `loading`  | 加载态       | `boolean`                                                                   | `false`   |
| `disabled` | 禁用         | `boolean`                                                                   | `false`   |

## 插槽

| 插槽      | 说明         |
| --------- | ------------ |
| `leading` | 文本前的图标 |
| `default` | 按钮文本     |
