---
layout: page
title: MobileAvatar 头像
---

<script setup>
import { NvMobileAvatar } from '@nerv-iip/ui-mobile'
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">尺寸</p>
    <div class="flex items-center gap-3">
      <NvMobileAvatar size="sm" name="张伟" />
      <NvMobileAvatar size="md" name="李芳" />
      <NvMobileAvatar size="lg" name="王强" />
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">形状</p>
    <div class="flex items-center gap-3">
      <NvMobileAvatar shape="circle" name="陈" />
      <NvMobileAvatar shape="square" name="备" />
    </div>
  </section>
  <section>
    <p class="ds-mdoc-label">回退</p>
    <div class="flex items-center gap-3">
      <NvMobileAvatar name="赵敏" />
      <NvMobileAvatar src="https://invalid.example/none.png" name="孙明" />
      <NvMobileAvatar />
    </div>
  </section>
</template>

# MobileAvatar 头像

展示用户或设备的图像头像。`src` 加载失败或缺省时，自动回退为姓名首字（中文取前两字）；无姓名则显示通用人形图标，底色为柔和的 `bg-muted`。右侧手机模拟器为实时组件，随页面滚动吸顶。

## 尺寸

`sm / md / lg` 三档，分别对应 32 / 40 / 56 像素。

```vue
<NvMobileAvatar size="sm" name="张伟" />
<NvMobileAvatar size="lg" name="王强" />
```

## 形状

默认 `circle` 圆形头像；`square` 圆角方形适合设备 / 物料缩略图。

```vue
<NvMobileAvatar shape="circle" name="陈" />
<NvMobileAvatar shape="square" name="备" />
```

## 回退

无 `src` 时显示姓名首字；图片加载失败自动降级为首字；二者皆无则显示人形图标。

```vue
<NvMobileAvatar name="赵敏" />
<NvMobileAvatar src="..." name="孙明" />
<NvMobileAvatar />
```

## 属性

| 属性    | 说明                   | 类型               | 默认     |
| ------- | ---------------------- | ------------------ | -------- |
| `src`   | 图片地址               | `string`           | —        |
| `name`  | 姓名，用于生成回退首字 | `string`           | —        |
| `alt`   | 图片替代文本           | `string`           | `name`   |
| `size`  | 尺寸                   | `sm \| md \| lg`   | `md`     |
| `shape` | 形状                   | `circle \| square` | `circle` |

</MobileDoc>
