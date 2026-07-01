<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { sanitizeRedirectPath } from '@/router/redirects'
import { Button } from '@nerv-iip/ui'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '无权限',
  },
})

const route = useRoute()
const returnPath = computed(() => sanitizeRedirectPath(route.query.from))
</script>

<template>
  <BusinessLayout>
    <section class="mx-auto grid max-w-xl gap-4 rounded-lg border bg-background p-6">
      <div class="grid gap-2">
        <p class="text-xs font-bold uppercase text-primary">权限不足</p>
        <h1 class="text-xl font-semibold text-foreground">当前账号不能访问此页面</h1>
        <p class="text-sm text-muted-foreground">
          导航入口会按权限自动裁剪；如果这是你日常职责所需，请联系管理员调整角色授权。
        </p>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button as-child>
          <RouterLink to="/">回到工作台</RouterLink>
        </Button>
        <Button as-child variant="outline">
          <RouterLink :to="returnPath">重试原页面</RouterLink>
        </Button>
      </div>
    </section>
  </BusinessLayout>
</template>
