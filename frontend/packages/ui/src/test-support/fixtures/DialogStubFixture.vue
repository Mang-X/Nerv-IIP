<script setup lang="ts">
// dialogStubs.test.ts 的最小消费夹具：真实 `<script setup>` 编译产物，
// 复现 `NvDialog` 在业务组件里被使用的方式（编译期直接绑定导入标识符，
// 不经 `resolveComponent` 按名解析）。仅供 dialogStubs 的红绿验证使用，
// 不进 test-support 的公开桶。
import { NvDialog } from '../../components/pc/dialog'
</script>

<template>
  <NvDialog>
    <div data-testid="dialog-slot-content">dialog slot content</div>
  </NvDialog>
</template>
