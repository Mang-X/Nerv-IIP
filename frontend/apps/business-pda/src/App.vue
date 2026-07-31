<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { RouterView, useRoute } from 'vue-router'
import PdaBottomNavigation from './components/navigation/PdaBottomNavigation.vue'

// dev-only 悬浮「模拟扫码」按钮（PDA 方案 §4.1 尾注的 M2 项）。
// `import.meta.env.DEV` 在构建期被 vite 静态替换：生产构建里整个三元折叠为
// `null`，动态 import 分支成为死代码被整体树摇 —— DevScanSimulator 及其
// chunk 不进生产包（build 后可 grep dist 验证无 DevScanSimulator 痕迹）。
const DevScanSimulator = import.meta.env.DEV
  ? defineAsyncComponent(() => import('./components/dev/DevScanSimulator.vue'))
  : null

const route = useRoute()
const showBottomNavigation = computed(() => route?.meta?.requiresAuth === true)
</script>

<template>
  <div :class="{ 'pda-navigation-active': showBottomNavigation }">
    <RouterView />
  </div>
  <PdaBottomNavigation v-if="showBottomNavigation" />
  <component :is="DevScanSimulator" v-if="DevScanSimulator" />
</template>

<style>
.pda-navigation-active [data-shell='content'] {
  padding-bottom: calc(4rem + env(safe-area-inset-bottom));
}
</style>
