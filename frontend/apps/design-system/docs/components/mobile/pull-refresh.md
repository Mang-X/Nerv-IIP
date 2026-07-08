---
layout: page
title: NvPullRefresh 下拉刷新
---

<script setup>
import { NvCell, NvPullRefresh } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const refreshing = ref(false)
const refreshList = ref([
  '齿轮箱端盖 · WO-2406-0421',
  '液压阀体 V3 · WO-2406-0426',
  '电机定子叠片 · WO-2406-0430',
  '前桥壳体 A2 · WO-2406-0413',
  '转向节 L · WO-2406-0419',
])
let refreshSeq = 0
function onRefresh() {
  window.setTimeout(() => {
    refreshList.value.unshift(`新到工单 · WO-2406-05${10 + refreshSeq++}`)
    refreshing.value = false
  }, 1200)
}
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">基础用法</p>
    <div class="w-full overflow-hidden rounded-xl border border-border">
      <NvPullRefresh v-model="refreshing" class="h-56" @refresh="onRefresh">
        <NvCell v-for="(item, i) in refreshList" :key="`${item}-${i}`" :title="item" />
      </NvPullRefresh>
    </div>
  </section>
</template>

# NvPullRefresh 下拉刷新

在自身滚动区域顶部下拉以刷新。指针驱动并带阻尼，松手越过阈值即触发 `refresh`，显示加载态直到 `v-model` 清零。

## 基础用法

`v-model` 绑定刷新中状态，`refresh` 事件里加载数据后将其置回 `false`。

```vue
<script setup>
import { NvCell, NvPullRefresh } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const refreshing = ref(false)
const list = ref(['齿轮箱端盖 · WO-2406-0421', '液压阀体 V3 · WO-2406-0426'])
function onRefresh() {
  window.setTimeout(() => {
    list.value.unshift('新到工单 · WO-2406-0510')
    refreshing.value = false
  }, 1200)
}
</script>

<template>
  <NvPullRefresh v-model="refreshing" class="h-56" @refresh="onRefresh">
    <NvCell v-for="(item, i) in list" :key="`${item}-${i}`" :title="item" />
  </NvPullRefresh>
</template>
```

## 属性

| 属性         | 说明                     | 类型      | 默认    |
| ------------ | ------------------------ | --------- | ------- |
| `modelValue` | 刷新中状态（`v-model`）  | `boolean` | `false` |
| `threshold`  | 触发刷新的下拉距离（px） | `number`  | `56`    |

## 事件

| 事件      | 说明             | 回调参数 |
| --------- | ---------------- | -------- |
| `refresh` | 下拉越过阈值触发 | —        |

</MobileDoc>
