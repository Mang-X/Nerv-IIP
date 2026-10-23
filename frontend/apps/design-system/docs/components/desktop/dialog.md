---
title: NvDialog 对话框
---

<script setup>
import {
  NvDialog,
  NvDialogTrigger,
  NvDialogContent,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogClose,
  NvButton,
  NvInput,
} from '@nerv-iip/ui'
import { GaugeIcon } from '@lucide/vue'
import { ref } from 'vue'

const dialogOpen = ref(false)
const scrollOpen = ref(false)
const scrollMode = ref('content')
const rowCount = ref(12)
const showError = ref(false)

function openScrollExample(mode) {
  scrollMode.value = mode
  rowCount.value = mode === 'short' ? 2 : 12
  showError.value = false
  scrollOpen.value = true
}
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
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>确认派发工单</NvDialogTitle>
        <NvDialogDescription>
          WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。
        </NvDialogDescription>
      </NvDialogHeader>
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
      <NvDialogFooter>
        <NvDialogClose as-child>
          <NvButton variant="ghost">取消</NvButton>
        </NvDialogClose>
        <NvButton variant="brand" @click="dialogOpen = false">确认派发</NvButton>
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
    <NvDialogContent>
      <NvDialogHeader>
        <NvDialogTitle>确认派发工单</NvDialogTitle>
        <NvDialogDescription
          >WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。</NvDialogDescription
        >
      </NvDialogHeader>
      <NvDialogFooter>
        <NvDialogClose as-child><NvButton variant="ghost">取消</NvButton></NvDialogClose>
        <NvButton variant="brand" @click="dialogOpen = false">确认派发</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
```

## 滚动与操作区

默认由遮罩滚动，短窗居中，长窗从顶部开始向下滚动。`NvDialogContent` 保留内容内边距；
`NvDialogFooter` 在正常文档流中占位，并相对最近的滚动容器吸底。
Footer 不向父容器外扩展，也不抵消父容器底部内边距，因此可以直接放在 Content 下，
也可以放在无内边距或带少量内边距的 form 内。头部、字段、校验信息与操作区使用父容器的 gap 分隔。

需要局部滚动时，只指定一个滚动容器：Content 限高滚动，或 form 限高滚动。
嵌套 form 应保持 `min-height: 0`，不要再用负外边距抵消 Footer 的占位。
下面三种示例使用真实组件；“增加一行”和“显示错误”可检查内容增高后首尾字段与操作区的可达性。

<Demo>
  <div class="flex flex-wrap gap-2">
    <NvButton variant="outline" @click="openScrollExample('short')">短窗示例</NvButton>
    <NvButton variant="outline" @click="openScrollExample('content')">Content 滚动示例</NvButton>
    <NvButton variant="outline" @click="openScrollExample('form')">form 滚动示例</NvButton>
  </div>
  <NvDialog v-model:open="scrollOpen">
    <NvDialogContent
      :class="scrollMode === 'content' ? 'max-h-[85vh] overflow-y-auto' : ''"
    >
      <NvDialogHeader>
        <NvDialogTitle>滚动与操作区示例</NvDialogTitle>
        <NvDialogDescription>检查首尾字段、动态校验信息与操作按钮。</NvDialogDescription>
      </NvDialogHeader>
      <form
        class="grid min-h-0 gap-4"
        :class="scrollMode === 'form' ? 'max-h-[60vh] overflow-y-auto px-1' : ''"
        @submit.prevent="scrollOpen = false"
      >
        <div class="flex flex-wrap gap-2">
          <NvButton type="button" variant="outline" @click="rowCount++">增加一行</NvButton>
          <NvButton type="button" variant="outline" @click="showError = !showError">显示错误</NvButton>
        </div>
        <label v-for="row in rowCount" :key="row" class="grid gap-2 text-sm">
          字段 {{ row }}
          <NvInput :aria-label="`字段 ${row}`" :placeholder="`输入字段 ${row}`" />
        </label>
        <p v-if="showError" role="alert" class="text-sm text-destructive">请核对最后一行后再确认。</p>
        <NvDialogFooter>
          <NvDialogClose as-child><NvButton type="button" variant="ghost">取消</NvButton></NvDialogClose>
          <NvButton type="submit">确认</NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</Demo>

```vue
<!-- Content 滚动：form 只负责字段间距，不另设滚动 -->
<NvDialogContent class="max-h-[85vh] overflow-y-auto">
  <NvDialogHeader><!-- 标题和说明 --></NvDialogHeader>
  <form class="grid min-h-0 gap-4" @submit.prevent="submit">
    <!-- 字段、校验信息、行操作 -->
    <NvDialogFooter><!-- 取消、确认 --></NvDialogFooter>
  </form>
</NvDialogContent>

<!-- form 滚动：Content 不再限高滚动 -->
<NvDialogContent>
  <NvDialogHeader><!-- 标题和说明 --></NvDialogHeader>
  <form class="grid min-h-0 max-h-[60vh] gap-4 overflow-y-auto px-1" @submit.prevent="submit">
    <!-- 字段、校验信息、行操作 -->
    <NvDialogFooter><!-- 取消、确认 --></NvDialogFooter>
  </form>
</NvDialogContent>
```

## 组成

| 组件                                                       | 说明                                   |
| ---------------------------------------------------------- | -------------------------------------- |
| `NvDialog`                                                 | 根容器，支持 `v-model:open`            |
| `NvDialogTrigger`                                          | 触发器，配合 `as-child` 包裹自定义按钮 |
| `NvDialogContent`                                          | 内容卡片（模糊遮罩 + 缩放入场）        |
| `NvDialogHeader` / `NvDialogTitle` / `NvDialogDescription` | 头部、标题与描述                       |
| `NvDialogFooter`                                           | 底部操作区                             |
| `NvDialogClose`                                            | 关闭触发器                             |

## 属性

| 属性       | 所属                                | 说明                           | 类型      | 默认    |
| ---------- | ----------------------------------- | ------------------------------ | --------- | ------- |
| `open`     | `NvDialog`                          | 受控开关状态（`v-model:open`） | `boolean` | `false` |
| `as-child` | `NvDialogTrigger` / `NvDialogClose` | 将渲染合并到子元素             | `boolean` | `false` |
