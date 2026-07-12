---
layout: page
title: NvMobileImage 图片
---

<script setup>
import { NvMobileImage } from '@nerv-iip/ui-mobile'

const photo = 'https://picsum.photos/seed/nerv-cnc/480/360'
const tall = 'https://picsum.photos/seed/nerv-line/360/480'
const broken = 'https://invalid.nerv-iip.local/not-found.jpg'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="nv-mdoc-label">填充模式</p>
    <div class="grid grid-cols-2 gap-3">
      <div>
        <NvMobileImage :src="photo" fit="cover" ratio="1" radius="lg" alt="设备照片" />
        <p class="mt-1 text-center text-xs text-muted-foreground">cover 裁切填满</p>
      </div>
      <div>
        <NvMobileImage :src="tall" fit="contain" ratio="1" radius="lg" alt="产线照片" class="bg-muted" />
        <p class="mt-1 text-center text-xs text-muted-foreground">contain 完整留边</p>
      </div>
    </div>
  </section>
  <section>
    <p class="nv-mdoc-label">圆角 / 比例</p>
    <div class="flex items-center gap-3">
      <NvMobileImage :src="photo" radius="full" class="size-16" alt="操作员头像" />
      <NvMobileImage :src="photo" ratio="16/9" radius="md" alt="工位看板" class="flex-1" />
    </div>
  </section>
  <section>
    <p class="nv-mdoc-label">加载失败回退</p>
    <NvMobileImage :src="broken" ratio="16/9" radius="lg" alt="缺图示例" />
  </section>
</template>

# NvMobileImage 图片

懒加载图片。加载中显示静默的微光占位（shimmer），加载失败回退为破图图标与「加载失败」文案。支持 `object-fit`、圆角与固定纵横比（提前占位，避免布局抖动）。

## 填充模式

`fit="cover"` 裁切铺满，`fit="contain"` 完整显示并在两侧留边。`ratio` 锁定宽高比。

```vue
<NvMobileImage :src="photo" fit="cover" ratio="1" radius="lg" />
<NvMobileImage :src="photo" fit="contain" ratio="16/9" />
```

## 圆角与比例

`radius` 提供 `none / sm / md / lg / full` 五档；`full` 配合方形尺寸即圆形头像。

```vue
<NvMobileImage :src="avatar" radius="full" class="size-16" />
```

## 属性

| 属性     | 说明            | 类型                             | 默认    |
| -------- | --------------- | -------------------------------- | ------- |
| `src`    | 图片地址        | `string`                         | —       |
| `alt`    | 替代文本        | `string`                         | —       |
| `fit`    | 填充方式        | `cover \| contain`               | `cover` |
| `radius` | 圆角档位        | `none \| sm \| md \| lg \| full` | `md`    |
| `ratio`  | 纵横比（宽/高） | `number \| string`               | —       |

</MobileDoc>
