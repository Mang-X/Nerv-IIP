<script setup lang="ts">
import { NvButton } from '@nerv-iip/ui'

definePage({
  alias: ['/design-system/blocks', '/design-system/shell'],
  meta: { requiresAuth: false, title: '设计文档站' },
})
</script>

<template>
  <main class="mx-auto max-w-xl space-y-4 px-6 py-12">
    <h1 class="text-2xl font-semibold">设计文档站</h1>
    <p class="text-muted-foreground">
      在仓库根目录运行以下命令，即可在本机查看组件示例、设计令牌和交互规范。
    </p>
    <code class="block">pnpm -C frontend/apps/design-system dev</code>
    <NvButton as-child><a href="http://127.0.0.1:5180/">打开本机设计文档站</a></NvButton>
  </main>
</template>
