<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Badge } from '@nerv-iip/ui'

definePage({
  meta: {
    requiresAuth: true,
    title: '业务工作台',
  },
})

const mvpPages = [
  { path: '/master-data/skus', domain: '主数据', label: 'SKU 维护' },
  { path: '/inventory/availability', domain: '库存', label: '库存可用量' },
  { path: '/inventory/movements', domain: '库存', label: '库存移动' },
  { path: '/inventory/counts', domain: '库存', label: '库存盘点' },
  { path: '/quality/inspections', domain: '质量', label: '检验记录' },
  { path: '/quality/ncrs', domain: '质量', label: '不合格品处理' },
  { path: '/mes', domain: 'MES', label: 'MES 总览' },
  { path: '/mes/foundation', domain: 'MES', label: '基础就绪' },
  { path: '/mes/work-orders', domain: 'MES', label: '工单执行' },
  { path: '/mes/operation-tasks', domain: 'MES', label: '工序任务' },
  { path: '/mes/wip', domain: 'MES', label: '在制状态' },
  { path: '/mes/production-reports', domain: 'MES', label: '生产报工' },
  { path: '/mes/receipts', domain: 'MES', label: '完工入库' },
  { path: '/mes/capacity', domain: 'MES', label: '产能影响' },
  { path: '/mes/schedules', domain: 'MES', label: '规则排程' },
] as const
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p class="text-xs font-bold uppercase text-primary">业务控制台</p>
          <h1 class="text-xl font-semibold text-foreground">业务工作台</h1>
          <p class="mt-1 max-w-3xl text-sm text-muted-foreground">
            面向主数据、库存、质量和 MES 的 PC 操作入口。
          </p>
        </div>
        <Badge variant="secondary">MVP 已接入</Badge>
      </div>

      <div class="grid gap-2 md:grid-cols-2 xl:grid-cols-4">
        <RouterLink
          v-for="page in mvpPages"
          :key="page.path"
          class="rounded-lg border bg-background px-4 py-3 text-sm transition-colors hover:border-primary/50 hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          :to="page.path"
        >
          <span class="block text-xs font-bold uppercase text-muted-foreground">{{ page.domain }}</span>
          <span class="mt-1 block font-semibold text-foreground">{{ page.label }}</span>
        </RouterLink>
      </div>
    </section>
  </BusinessLayout>
</template>
