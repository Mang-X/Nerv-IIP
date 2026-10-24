---
title: NvDialog 对话框
---

<script setup>
import {
  NvDialog,
  NvDialogTrigger,
  NvDialogContent,
  NvDialogBody,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogClose,
  NvButton,
  NvInput,
} from '@nerv-iip/ui'
import { GaugeIcon } from '@lucide/vue'
import { onMounted, ref } from 'vue'

const dialogOpen = ref(false)
const scrollOpen = ref(false)
const pending = ref(false)
const rowCount = ref(12)
const showError = ref(false)

function openScrollExample(mode) {
  pending.value = mode === 'pending'
  rowCount.value = mode === 'short' ? 2 : 12
  showError.value = mode === 'error'
  scrollOpen.value = true
}
// 文档示例支持直达状态，便于共享和同视口视觉对照。
onMounted(() => {
  const example = new URLSearchParams(window.location.search).get('example')
  if (['short', 'long', 'error', 'pending'].includes(example)) openScrollExample(example)
})
</script>

# NvDialog 对话框

承载需要用户聚焦确认的关键操作。`NvDialog` 提供模糊遮罩与缩放入场，由头部、标题、说明与底部操作区组合内容。

## 基础用法

通过 `NvDialogTrigger` 触发，`v-model:open` 双向绑定开关状态。

<Demo>
  <NvDialog v-model:open="dialogOpen">
    <NvDialogTrigger as-child>
      <NvButton variant="brand">
        <template #leading><GaugeIcon aria-hidden="true" /></template>
        派发工单
      </NvButton>
    </NvDialogTrigger>
    <NvDialogContent scrollable>
      <NvDialogHeader>
        <NvDialogTitle>确认派发工单</NvDialogTitle>
        <NvDialogDescription>
          WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。
        </NvDialogDescription>
      </NvDialogHeader>
      <NvDialogBody>
      <div class="rounded-lg border border-border bg-muted/40 p-4 text-sm">
        <div class="flex justify-between">
          <span class="text-muted-foreground">数量</span>
          <span class="tabular-nums">480</span>
        </div>
        <div class="mt-2 flex justify-between">
          <span class="text-muted-foreground">预计工时</span>
          <span class="tabular-nums">14.5 h</span>
        </div>
      </div>
      </NvDialogBody>
      <NvDialogFooter>
        <NvDialogClose as-child>
          <NvButton variant="outline">取消</NvButton>
        </NvDialogClose>
        <NvButton @click="dialogOpen = false">确认派发</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</Demo>

```vue
<script setup>
import {
  NvDialog,
  NvDialogTrigger,
  NvDialogContent,
  NvDialogBody,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogClose,
  NvButton,
} from '@nerv-iip/ui'
import { ref } from 'vue'
const dialogOpen = ref(false)
</script>

<template>
  <NvDialog v-model:open="dialogOpen">
    <NvDialogTrigger as-child>
      <NvButton variant="brand">派发工单</NvButton>
    </NvDialogTrigger>
    <NvDialogContent scrollable>
      <NvDialogHeader>
        <NvDialogTitle>确认派发工单</NvDialogTitle>
        <NvDialogDescription
          >WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。</NvDialogDescription
        >
      </NvDialogHeader>
      <NvDialogFooter>
        <NvDialogClose as-child><NvButton variant="outline">取消</NvButton></NvDialogClose>
        <NvButton @click="dialogOpen = false">确认派发</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
```

## 长表单：标题、正文与操作栏

长表单使用 `NvDialogContent scrollable` 与 `NvDialogBody`。弹窗按内容自然增长，
达到动态视口上限后，只有正文滚动。标题、关闭入口和底部操作始终保留位置。
操作栏与弹窗共用表面、横跨完整宽度，以一条轻分隔线区别于正文；不是表单里的独立卡片。

form 保留原生提交语义，直接包含 Body 和 Footer。不要再对 Content/form 叠加
`overflow-y-auto`、手写限高、负外边距或 Footer 的 `static` 补丁。
Body 内用 `grid gap-4` 组织字段；字段和操作区各自保留内边距，滚动条不挤占按钮宽度。

<Demo>
  <div class="flex flex-wrap gap-2">
    <NvButton variant="outline" @click="openScrollExample('short')">短表单</NvButton>
    <NvButton variant="outline" @click="openScrollExample('long')">长表单</NvButton>
    <NvButton variant="outline" @click="openScrollExample('error')">校验状态</NvButton>
    <NvButton variant="outline" @click="openScrollExample('pending')">提交中状态</NvButton>
  </div>
  <NvDialog v-model:open="scrollOpen">
    <NvDialogContent scrollable class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>发布制造 BOM 新版本</NvDialogTitle>
        <NvDialogDescription>核对物料明细后发布，已发布版本保持不变。</NvDialogDescription>
      </NvDialogHeader>
      <form @submit.prevent="showError = true">
        <NvDialogBody>
          <div class="grid gap-4">
            <div class="flex items-center justify-between gap-3">
              <span class="text-sm font-medium">物料明细</span>
              <NvButton type="button" variant="outline" @click="rowCount++">添加物料</NvButton>
            </div>
            <label v-for="row in rowCount" :key="row" class="grid gap-2 text-sm">
              物料 {{ row }}
              <NvInput :aria-label="`物料 ${row}`" :placeholder="`选择第 ${row} 项组件物料`" />
            </label>
            <p v-if="showError" role="alert" class="text-sm text-destructive">请补全物料后再发布，当前填写内容已保留。</p>
          </div>
        </NvDialogBody>
        <NvDialogFooter>
          <NvDialogClose as-child><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose>
          <NvButton type="submit" class="sm:min-w-28" :disabled="pending">{{ pending ? '正在发布…' : '发布版本' }}</NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</Demo>

```vue
<NvDialogContent scrollable class="sm:max-w-lg">
  <NvDialogHeader>
    <NvDialogTitle>发布新版本</NvDialogTitle>
    <NvDialogDescription>核对明细后发布。</NvDialogDescription>
  </NvDialogHeader>
  <form @submit.prevent="submit">
    <NvDialogBody>
      <div class="grid gap-4"><!-- 字段、动态行、校验提示 --></div>
    </NvDialogBody>
    <NvDialogFooter>
      <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
      <NvButton type="submit" :disabled="pending">发布版本</NvButton>
    </NvDialogFooter>
  </form>
</NvDialogContent>
```

未开启 `scrollable` 的旧组合继续使用遮罩滚动与原有 Footer 规则；不混用两套滚动归属。
短确认窗可以沿用基础用法，含动态行的表单推荐使用上面的三段布局。

## 组成

| 组件                                                       | 说明                                   |
| ---------------------------------------------------------- | -------------------------------------- |
| `NvDialog`                                                 | 根容器，支持 `v-model:open`            |
| `NvDialogTrigger`                                          | 触发器，配合 `as-child` 包裹自定义按钮 |
| `NvDialogContent`                                          | 内容卡片（模糊遮罩 + 缩放入场）        |
| `NvDialogHeader` / `NvDialogTitle` / `NvDialogDescription` | 头部、标题与描述                       |
| `NvDialogBody`                                             | `scrollable` 模式下唯一的正文滚动区    |
| `NvDialogFooter`                                           | 底部操作区                             |
| `NvDialogClose`                                            | 关闭触发器                             |

## 属性

| 属性       | 所属                                | 说明                           | 类型      | 默认    |
| ---------- | ----------------------------------- | ------------------------------ | --------- | ------- |
| `open`     | `NvDialog`                          | 受控开关状态（`v-model:open`） | `boolean` | `false` |
| `as-child` | `NvDialogTrigger` / `NvDialogClose` | 将渲染合并到子元素             | `boolean` | `false` |

## NvDialogContent 扩展属性

| 属性         | 类型      | 默认值  | 说明                                                     |
| ------------ | --------- | ------- | -------------------------------------------------------- |
| `scrollable` | `boolean` | `false` | 配合 Body 使用限高三段布局；Header/Footer 不参与正文滚动 |
