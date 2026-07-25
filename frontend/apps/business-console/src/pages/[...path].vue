<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton } from '@nerv-iip/ui'
import { LayoutDashboardIcon } from '@lucide/vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '页面暂未开放',
  },
})
</script>

<template>
  <BusinessLayout>
    <section class="rounded-lg border bg-background p-6">
      <h1 class="text-lg font-semibold text-foreground">页面暂未开放</h1>
      <p class="mt-1 text-sm text-muted-foreground">
        这个地址还没有对应的功能页面，可能是链接已过期，或该能力尚在建设中。
      </p>
      <NvButton as-child class="mt-4" size="sm">
        <RouterLink to="/">
          <LayoutDashboardIcon aria-hidden="true" />
          返回工作台
        </RouterLink>
      </NvButton>
    </section>
  </BusinessLayout>
</template>
