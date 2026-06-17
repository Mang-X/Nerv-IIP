---
title: ListRow 列表行
---

<script setup>
import { ListRow } from '@nerv-iip/ui-mobile'
</script>

# ListRow 列表行

可点击的列表行：主标题 + 可选副标题，尾部箭头表示可进入。适合工单、物料等列表场景。

## 基础用法

<Demo mobile>
  <div class="overflow-hidden border-y border-border">
    <ListRow title="齿轮箱端盖" subtitle="WO-2406-0421 · 320 件" />
    <ListRow title="液压阀体 V3" subtitle="WO-2406-0426 · 640 件" />
    <ListRow title="电机定子叠片" subtitle="WO-2406-0430 · 1,200 件" />
  </div>
</Demo>

```vue
<ListRow
  title="齿轮箱端盖"
  subtitle="WO-2406-0421 · 320 件"
  @select="open('WO-2406-0421')"
/>
```

## 非交互行

<Demo mobile>
  <div class="overflow-hidden border-y border-border">
    <ListRow title="班组" subtitle="精密一班 · 张伟" :interactive="false" />
  </div>
</Demo>

```vue
<ListRow title="班组" subtitle="精密一班 · 张伟" :interactive="false" />
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `title` | 主标题 | `string` | — |
| `subtitle` | 副标题 | `string` | — |
| `interactive` | 可点击并显示尾部箭头 | `boolean` | `true` |

事件：`@select`（点击或回车，仅在 `interactive` 为真时触发）。插槽：`#meta` 标题下方附加内容、`#trailing` 尾部自定义内容。
