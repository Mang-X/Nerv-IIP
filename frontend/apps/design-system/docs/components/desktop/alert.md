---
title: Alert 警告提示
---

<script setup>
import { Alert, AlertTitle, AlertDescription } from '@nerv-iip/ui'
import { InfoIcon, TriangleAlertIcon } from 'lucide-vue-next'
</script>

# Alert 警告提示

页面内常驻的提示条，用于陈述上下文、风险或操作结果，区别于短暂的轻提示。`Alert` 由 `AlertTitle` 与 `AlertDescription` 组合而成。

## 基础用法

<Demo>
  <Alert class="max-w-md">
    <AlertTitle>排产已锁定</AlertTitle>
    <AlertDescription>今日 A 线计划已下发，物料预占完成，开工后不可再调整顺序。</AlertDescription>
  </Alert>
</Demo>

```vue
<Alert>
  <AlertTitle>排产已锁定</AlertTitle>
  <AlertDescription>今日 A 线计划已下发，物料预占完成，开工后不可再调整顺序。</AlertDescription>
</Alert>
```

## 带图标

图标作为 `Alert` 的直接子节点，自动对齐到标题与描述左侧。

<Demo>
  <Alert class="max-w-md">
    <InfoIcon aria-hidden="true" />
    <AlertTitle>MES 网关已同步</AlertTitle>
    <AlertDescription>最近一次采集于 16:00，下一次将在 5 分钟后自动拉取。</AlertDescription>
  </Alert>
</Demo>

```vue
<Alert>
  <InfoIcon aria-hidden="true" />
  <AlertTitle>MES 网关已同步</AlertTitle>
  <AlertDescription>最近一次采集于 16:00，下一次将在 5 分钟后自动拉取。</AlertDescription>
</Alert>
```

## 危险变体

<Demo>
  <Alert variant="destructive" class="max-w-md">
    <TriangleAlertIcon aria-hidden="true" />
    <AlertTitle>工作中心阻塞</AlertTitle>
    <AlertDescription>WC-ASM-04 处于阻塞状态，已暂停派工，请先处理设备报警。</AlertDescription>
  </Alert>
</Demo>

```vue
<Alert variant="destructive">
  <TriangleAlertIcon aria-hidden="true" />
  <AlertTitle>工作中心阻塞</AlertTitle>
  <AlertDescription>WC-ASM-04 处于阻塞状态，已暂停派工，请先处理设备报警。</AlertDescription>
</Alert>
```

## 属性

| 属性      | 说明     | 类型                     | 默认      |
| --------- | -------- | ------------------------ | --------- |
| `variant` | 视觉变体 | `default \| destructive` | `default` |
