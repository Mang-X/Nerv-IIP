---
title: Badge 角标
---

<script setup>
import { Badge } from '@nerv-iip/ui-mobile'
import { BellIcon, ClipboardListIcon, UserIcon } from 'lucide-vue-next'
</script>

# Badge 角标

包裹图标或头像，在右上角钉一个红色计数或圆点（Vant / tdesign-mobile 风格）。

## 计数

<Demo mobile>
  <div class="flex items-center gap-7 px-5">
    <Badge :count="5">
      <BellIcon class="size-6 text-foreground" aria-hidden="true" />
    </Badge>
    <Badge :count="128" :max="99">
      <ClipboardListIcon class="size-6 text-foreground" aria-hidden="true" />
    </Badge>
  </div>
</Demo>

```vue
<Badge :count="5">
  <BellIcon class="size-6" />
</Badge>
<Badge :count="128" :max="99">
  <ClipboardListIcon class="size-6" />
</Badge>
```

## 圆点

<Demo mobile>
  <div class="flex items-center gap-7 px-5">
    <Badge dot>
      <UserIcon class="size-6 text-foreground" aria-hidden="true" />
    </Badge>
  </div>
</Demo>

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
