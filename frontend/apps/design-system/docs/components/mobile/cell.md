---
layout: page
title: NvCell 单元格
---

<script setup>
import { NvCell, NvCellGroup, NvMobileSwitch } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const lockMaterial = ref(true)
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <div class="px-3">
      <NvCellGroup>
        <NvCell title="工单号" value="WO-2406-0413" />
        <NvCell title="产品" value="前桥壳体 A2" />
        <NvCell title="工艺路线" note="3 道工序" arrow />
        <NvCell title="加急插单">
          <template #value><NvMobileSwitch v-model="lockMaterial" /></template>
        </NvCell>
      </NvCellGroup>
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">分组标题</p>
    <div class="px-3">
      <NvCellGroup title="生产信息">
        <NvCell title="目标产线" value="A 线 · 精密加工" arrow />
        <NvCell title="计划日期" value="2026-06-18" arrow />
      </NvCellGroup>
    </div>
  </section>
</template>

# NvCell 单元格

信息 / 表单行（tdesign-mobile 风格）：标题 + 可选备注 + 尾部值，可选箭头。`NvCellGroup` 把多个单元格组合成带细分割线的圆角卡片。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

单元格可承载文本值、备注、箭头，或通过 `#value` 插槽嵌入开关等控件。

```vue
<NvCellGroup>
  <NvCell title="工单号" value="WO-2406-0413" />
  <NvCell title="产品" value="前桥壳体 A2" />
  <NvCell title="工艺路线" note="3 道工序" arrow @click="openRoute" />
  <NvCell title="加急插单">
    <template #value><NvMobileSwitch v-model="lockMaterial" /></template>
  </NvCell>
</NvCellGroup>
```

## 分组标题

`NvCellGroup` 的 `title` 在卡片上方显示分组标题。

```vue
<NvCellGroup title="生产信息">
  <NvCell title="目标产线" value="A 线 · 精密加工" arrow />
  <NvCell title="计划日期" value="2026-06-18" arrow />
</NvCellGroup>
```

## 属性

### NvCell

| 属性    | 说明                           | 类型               | 默认    |
| ------- | ------------------------------ | ------------------ | ------- |
| `title` | 标题                           | `string`           | —       |
| `note`  | 标题下方备注                   | `string`           | —       |
| `value` | 尾部值（也可用 `#value` 插槽） | `string \| number` | —       |
| `arrow` | 显示箭头并启用点击             | `boolean`          | `false` |

事件：`@click`（仅在 `arrow` 为真时触发）。插槽：`#icon` 前置图标、`#value` 自定义尾部内容。

### NvCellGroup

| 属性    | 说明     | 类型     | 默认 |
| ------- | -------- | -------- | ---- |
| `title` | 分组标题 | `string` | —    |

</MobileDoc>
