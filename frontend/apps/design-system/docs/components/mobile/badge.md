---
layout: page
title: Badge 角标
---

<script setup>
import { Badge } from '@nerv-iip/ui-mobile'
import { BellIcon, ClipboardListIcon, UserIcon } from 'lucide-vue-next'
</script>

<MobileDoc>

<template #phone>
  <section>
    <p class="ds-mdoc-label">计数</p>
    <div class="flex items-center gap-7 px-5">
      <Badge :count="5">
        <BellIcon class="size-6 text-foreground" aria-hidden="true" />
      </Badge>
      <Badge :count="128" :max="99">
        <ClipboardListIcon class="size-6 text-foreground" aria-hidden="true" />
      </Badge>
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">圆点</p>
    <div class="flex items-center gap-7 px-5">
      <Badge dot>
        <UserIcon class="size-6 text-foreground" aria-hidden="true" />
      </Badge>
    </div>
  </section>
</template>

# Badge 角标

包裹图标或头像，在右上角钉一个红色计数或圆点（Vant / tdesign-mobile 风格）。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 计数

计数值为 0 时不显示；超过 `max` 显示为 `max+`。

```vue
<Badge :count="5">
  <BellIcon class="size-6" />
</Badge>
<Badge :count="128" :max="99">
  <ClipboardListIcon class="size-6" />
</Badge>
```

## 圆点

仅显示红色圆点，忽略计数。

```vue
<Badge dot>
  <UserIcon class="size-6" />
</Badge>
```

## 属性

| 属性 | 说明 | 类型 | 默认 |
|---|---|---|---|
| `count` | 计数值，为 0 时不显示 | `number` | `0` |
| `dot` | 仅显示圆点（忽略计数） | `boolean` | `false` |
| `max` | 计数上限，超出显示 `max+` | `number` | `99` |

</MobileDoc>
