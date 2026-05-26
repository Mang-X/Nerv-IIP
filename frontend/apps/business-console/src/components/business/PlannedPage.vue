<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'

defineProps<{
  domain: string
  routeName: string
  summary: string
}>()
</script>

<template>
  <section class="grid gap-3">
    <div class="flex flex-wrap items-end justify-between gap-3">
      <div>
        <p class="text-xs font-bold uppercase text-primary">{{ domain }}</p>
        <h1 class="text-xl font-semibold text-foreground">{{ routeName }}</h1>
        <p class="mt-1 max-w-3xl text-sm text-muted-foreground">{{ summary }}</p>
      </div>
      <Badge variant="secondary">MVP 路由</Badge>
    </div>

    <div class="rounded-lg border bg-background p-4 text-sm text-muted-foreground">
      当前路由已接入业务控制台认证壳，数据表格和表单将在后续纵切任务中落地。
    </div>
  </section>
</template>
