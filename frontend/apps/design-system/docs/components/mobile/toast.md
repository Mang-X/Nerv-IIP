---
layout: page
title: NvMobileToast 居中提示
---

<script setup>
import { NvMobileButton, NvMobileToast } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const toastShow = ref(false)
const toastType = ref('text')
const toastMsg = ref('已保存')
function fireToast(type, msg) {
  toastType.value = type
  toastMsg.value = msg
  toastShow.value = true
}

const loadingToast = ref(false)
function runLoadingToast() {
  loadingToast.value = true
  window.setTimeout(() => {
    loadingToast.value = false
    fireToast('success', '提交成功')
  }, 1600)
}
</script>

<MobileDoc>

<template #phone>

  <section>
    <p class="ds-mdoc-label">文字与状态</p>
    <div class="grid grid-cols-2 gap-2">
      <NvMobileButton variant="default" size="md" @click="fireToast('text', '已复制单号')">文字</NvMobileButton>
      <NvMobileButton variant="default" size="md" @click="fireToast('success', '报工成功')">成功</NvMobileButton>
      <NvMobileButton variant="default" size="md" @click="fireToast('error', '网络异常')">失败</NvMobileButton>
    </div>
    <NvMobileToast v-model:show="toastShow" :type="toastType" :message="toastMsg" />
  </section>
  <section>
    <p class="ds-mdoc-label">加载（带遮罩）</p>
    <NvMobileButton variant="default" size="md" block @click="runLoadingToast">
      加载（带遮罩）
    </NvMobileButton>
    <NvMobileToast v-model:show="loadingToast" type="loading" message="提交中…" overlay />
  </section>
</template>

# NvMobileToast 居中提示

居中浮层式 HUD 提示，深色圆角卡片配可选状态图标（加载/成功/失败），到时自动消失。

## 文字与状态

通过 `v-model:show` 控制显隐，`type` 切换状态图标，`message` 设置文案。

```vue
<script setup>
import { NvMobileButton, NvMobileToast } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'

const toastShow = ref(false)
const toastType = ref('text')
const toastMsg = ref('已保存')
function fireToast(type, msg) {
  toastType.value = type
  toastMsg.value = msg
  toastShow.value = true
}
</script>

<template>
  <NvMobileButton @click="fireToast('success', '报工成功')">成功</NvMobileButton>
  <NvMobileToast v-model:show="toastShow" :type="toastType" :message="toastMsg" />
</template>
```

## 加载（带遮罩）

`type="loading"` 持续显示直到手动关闭；`overlay` 阻断背后交互，适合提交等待。

```vue
<script setup>
import { ref } from 'vue'

const loadingToast = ref(false)
function runLoadingToast() {
  loadingToast.value = true
  window.setTimeout(() => (loadingToast.value = false), 1600)
}
</script>

<template>
  <NvMobileButton block @click="runLoadingToast">加载（带遮罩）</NvMobileButton>
  <NvMobileToast v-model:show="loadingToast" type="loading" message="提交中…" overlay />
</template>
```

## 属性

| 属性       | 说明                                              | 类型                                  | 默认    |
| ---------- | ------------------------------------------------- | ------------------------------------- | ------- |
| `show`     | 是否显示（`v-model:show`）                        | `boolean`                             | —       |
| `message`  | 提示文案                                          | `string`                              | —       |
| `type`     | 状态类型                                          | `text \| loading \| success \| error` | `text`  |
| `duration` | 自动关闭延时（ms）；`loading` 与 `0` 时不自动关闭 | `number`                              | `2000`  |
| `overlay`  | 阻断背后交互（配合 `loading`）                    | `boolean`                             | `false` |

</MobileDoc>
