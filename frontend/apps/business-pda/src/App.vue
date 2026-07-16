<script setup lang="ts">
import { defineAsyncComponent } from 'vue'
import { RouterView } from 'vue-router'

// dev-only 悬浮「模拟扫码」按钮（PDA 方案 §4.1 尾注的 M2 项）。
// `import.meta.env.DEV` 在构建期被 vite 静态替换：生产构建里整个三元折叠为
// `null`，动态 import 分支成为死代码被整体树摇 —— DevScanSimulator 及其
// chunk 不进生产包（build 后可 grep dist 验证无 DevScanSimulator 痕迹）。
const DevScanSimulator = import.meta.env.DEV
  ? defineAsyncComponent(() => import('./components/dev/DevScanSimulator.vue'))
  : null
</script>

<template>
  <RouterView />
  <component :is="DevScanSimulator" v-if="DevScanSimulator" />
</template>
