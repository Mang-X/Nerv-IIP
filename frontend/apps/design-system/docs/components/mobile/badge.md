---
layout: page
title: NvMobileBadge 角标
---

<script setup>
import { NvMobileBadge } from '@nerv-iip/ui-mobile'
import { BellIcon, ClipboardListIcon, UserIcon } from 'lucide-vue-next'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">计数</p>
    <div class="flex items-center gap-7 px-5">
      <NvMobileBadge :count="5">
        <BellIcon class="size-6 text-foreground" aria-hidden="true" />
      </NvMobileBadge>
      <NvMobileBadge :count="128" :max="99">
        <ClipboardListIcon class="size-6 text-foreground" aria-hidden="true" />
      </NvMobileBadge>
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">圆点</p>
    <div class="flex items-center gap-7 px-5">
      <NvMobileBadge dot>
        <UserIcon class="size-6 text-foreground" aria-hidden="true" />
      </NvMobileBadge>
    </div>
  </section>
</template>

# NvMobileBadge 角标

包裹图标或头像，在右上角钉一个红色计数或圆点（Vant / tdesign-mobile 风格）。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 计数

计数值为 0 时不显示；超过 `max` 显示为 `max+`。

```vue
<NvMobileBadge :count="5">
  <BellIcon class="size-6" />
</NvMobileBadge>
<NvMobileBadge :count="128" :max="99">
  <ClipboardListIcon class="size-6" />
</NvMobileBadge>
```

## 圆点

仅显示红色圆点，忽略计数。

```vue
<NvMobileBadge dot>
  <UserIcon class="size-6" />
</NvMobileBadge>
```

## 属性

| 属性    | 说明                      | 类型      | 默认    |
| ------- | ------------------------- | --------- | ------- |
| `count` | 计数值，为 0 时不显示     | `number`  | `0`     |
| `dot`   | 仅显示圆点（忽略计数）    | `boolean` | `false` |
| `max`   | 计数上限，超出显示 `max+` | `number`  | `99`    |

</MobileDoc>
