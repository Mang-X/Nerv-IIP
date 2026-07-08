---
layout: page
title: NvListRow 列表行
---

<script setup>
import { NvListRow } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <div class="overflow-hidden border-y border-border">
      <NvListRow title="齿轮箱端盖" subtitle="WO-2406-0421 · 320 件" />
      <NvListRow title="液压阀体 V3" subtitle="WO-2406-0426 · 640 件" />
      <NvListRow title="电机定子叠片" subtitle="WO-2406-0430 · 1,200 件" />
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">非交互行</p>
    <div class="overflow-hidden border-y border-border">
      <NvListRow title="班组" subtitle="精密一班 · 张伟" :interactive="false" />
    </div>
  </section>
</template>

# NvListRow 列表行

可点击的列表行：主标题 + 可选副标题，尾部箭头表示可进入。适合工单、物料等列表场景。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 基础用法

可点击行尾部显示箭头，点击或回车触发 `@select`。

```vue
<NvListRow title="齿轮箱端盖" subtitle="WO-2406-0421 · 320 件" @select="open('WO-2406-0421')" />
```

## 非交互行

传 `:interactive="false"` 隐藏箭头并禁用点击，用于纯展示行。

```vue
<NvListRow title="班组" subtitle="精密一班 · 张伟" :interactive="false" />
```

## 属性

| 属性          | 说明                 | 类型      | 默认   |
| ------------- | -------------------- | --------- | ------ |
| `title`       | 主标题               | `string`  | —      |
| `subtitle`    | 副标题               | `string`  | —      |
| `interactive` | 可点击并显示尾部箭头 | `boolean` | `true` |

事件：`@select`（点击或回车，仅在 `interactive` 为真时触发）。插槽：`#meta` 标题下方附加内容、`#trailing` 尾部自定义内容。

</MobileDoc>
