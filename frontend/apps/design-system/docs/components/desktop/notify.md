---
title: Notify 消息提醒
---

<script setup>
import { messagePro, notificationPro, NvNotifierHost, NvButton } from '@nerv-iip/ui'

function fireMessage(kind) {
  const map = {
    info: () => messagePro.info('已同步 MES 网关'),
    success: () => messagePro.success('保存成功'),
    warning: () => messagePro.warning('库存接近下限'),
    error: () => messagePro.error('网络连接中断'),
  }
  map[kind]()
}
function fireBurst() {
  messagePro.success('已保存草稿')
  setTimeout(() => messagePro.info('已同步 MES 网关'), 140)
  setTimeout(() => messagePro.warning('库存接近下限'), 280)
}
function fireNotification(kind) {
  const map = {
    info: () => notificationPro.info('排产已更新', { description: '今日计划重排，受影响工单 6 张。' }),
    success: () => notificationPro.success('保养工单已创建', { description: 'CNC-07 · 今晚 22:00 执行。' }),
    warning: () => notificationPro.warning('B 线物料不足', { description: '液压阀体 V3 缺口 452 件，已转采购申请。' }),
    error: () => notificationPro.error('派工失败', { description: '工作中心 WC-ASM-04 处于阻塞状态。' }),
  }
  map[kind]()
}
</script>

# Notify 消息提醒

两类反馈通道：`messagePro` 顶部居中、单行、短暂自停；`notificationPro` 右上角卡片，带标题与描述。二者皆为命令式函数调用，需在应用根部挂载一次 `NvNotifierHost`。

> 应用入口处放置 `<NvNotifierHost />`，本页 Demo 已内置。

## 轻提示 Message

`messagePro.{info|success|warning|error}(标题)`，适合无需详述的即时结果。

<Demo>
  <NvNotifierHost />
  <div class="flex flex-wrap gap-2">
    <NvButton size="sm" variant="outline" @click="fireMessage('success')">成功</NvButton>
    <NvButton size="sm" variant="outline" @click="fireMessage('info')">信息</NvButton>
    <NvButton size="sm" variant="outline" @click="fireMessage('warning')">预警</NvButton>
    <NvButton size="sm" variant="outline" @click="fireMessage('error')">错误</NvButton>
    <NvButton size="sm" variant="brand" @click="fireBurst">连发 3 条</NvButton>
  </div>
</Demo>

```vue
<script setup>
import { messagePro } from '@nerv-iip/ui'
</script>

<template>
  <NvButton @click="messagePro.success('保存成功')">成功</NvButton>
  <NvButton @click="messagePro.error('网络连接中断')">错误</NvButton>
</template>
```

## 通知 Notification

`notificationPro.{info|success|warning|error}(标题, { description })`，右上角卡片，适合带上下文的异步结果。

<Demo>
  <NvNotifierHost />
  <div class="flex flex-wrap gap-2">
    <NvButton size="sm" variant="outline" @click="fireNotification('success')">成功</NvButton>
    <NvButton size="sm" variant="outline" @click="fireNotification('info')">信息</NvButton>
    <NvButton size="sm" variant="outline" @click="fireNotification('warning')">预警</NvButton>
    <NvButton size="sm" variant="outline" @click="fireNotification('error')">错误</NvButton>
  </div>
</Demo>

```vue
<script setup>
import { notificationPro } from '@nerv-iip/ui'
</script>

<template>
  <NvButton
    @click="
      notificationPro.warning('B 线物料不足', {
        description: '液压阀体 V3 缺口 452 件，已转采购申请。',
      })
    "
    >预警</NvButton
  >
</template>
```

## API

| 方法                                               | 说明           | 参数                                          |
| -------------------------------------------------- | -------------- | --------------------------------------------- |
| `messagePro.info / success / warning / error`      | 顶部轻提示     | `(title: string, { duration }?)`              |
| `notificationPro.info / success / warning / error` | 右上角通知卡片 | `(title: string, { description, duration }?)` |
| `dismissNotify`                                    | 主动关闭指定项 | `(id: number)`                                |

## 选项

| 选项          | 适用         | 说明                               | 类型     | 默认                                 |
| ------------- | ------------ | ---------------------------------- | -------- | ------------------------------------ |
| `duration`    | 两者         | 自动关闭时长（ms），`0` 不自动关闭 | `number` | message `2600` / notification `4500` |
| `description` | notification | 卡片描述文本                       | `string` | —                                    |
