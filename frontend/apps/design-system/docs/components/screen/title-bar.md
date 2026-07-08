---
title: TitleBar 标题栏
---

<script setup>
import { NvTitleBar } from '@nerv-iip/ui'
</script>

# TitleBar 标题栏

区块级标题栏:居中标题(可带一行副题),左右各一条渐隐发丝线,内端各收于一颗青色菱形节点。不用面板就给一个分区抬头以分量 —— 菱形是唯一的青色点缀。用来在大屏里切分「产线综合监控」「设备健康」这类版块。

## 基础用法

`title` 是居中主标题。

<ScreenDemo>
  <div style="width: 560px">
    <NvTitleBar title="产线综合监控" />
  </div>
</ScreenDemo>

```vue
<NvTitleBar title="产线综合监控" />
```

## 带副题

`sub` 在标题下补一行小字,放说明或时间戳。

<ScreenDemo>
  <div style="width: 560px">
    <NvTitleBar title="设备健康总览" sub="焊接线 A · 装配线 B · CNC 线 C · 截至 2024-06-12 10:24" />
  </div>
</ScreenDemo>

```vue
<NvTitleBar title="设备健康总览" sub="焊接线 A · 装配线 B · CNC 线 C · 截至 2024-06-12 10:24" />
```

## 属性

| 属性    | 说明                       | 类型     | 默认             |
| ------- | -------------------------- | -------- | ---------------- |
| `title` | 居中主标题                 | `string` | `'产线综合监控'` |
| `sub`   | 标题下的副题(为空则不渲染) | `string` | —                |
