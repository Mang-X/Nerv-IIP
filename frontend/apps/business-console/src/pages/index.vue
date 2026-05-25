<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Badge } from '@nerv-iip/ui'
import { useI18n } from 'vue-i18n'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.dashboard',
  },
})

const { t } = useI18n()

const mvpPages = [
  { path: '/master-data/skus', domain: 'MasterData', labelKey: 'nav.skus' },
  { path: '/inventory/availability', domain: 'Inventory', labelKey: 'nav.availability' },
  { path: '/inventory/movements', domain: 'Inventory', labelKey: 'nav.movements' },
  { path: '/inventory/counts', domain: 'Inventory', labelKey: 'nav.counts' },
  { path: '/quality/inspections', domain: 'Quality', labelKey: 'nav.inspections' },
  { path: '/quality/ncrs', domain: 'Quality', labelKey: 'nav.ncrs' },
  { path: '/mes/work-orders', domain: 'MES', labelKey: 'nav.workOrders' },
  { path: '/mes/schedules', domain: 'MES', labelKey: 'nav.schedules' },
] as const
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p class="text-xs font-bold uppercase text-primary">{{ t('app.tagline') }}</p>
          <h1 class="text-xl font-semibold text-foreground">{{ t('dashboard.title') }}</h1>
          <p class="mt-1 max-w-3xl text-sm text-muted-foreground">
            {{ t('dashboard.summary') }}
          </p>
        </div>
        <Badge variant="secondary">{{ t('dashboard.status') }}</Badge>
      </div>

      <div class="grid gap-2 md:grid-cols-2 xl:grid-cols-4">
        <RouterLink
          v-for="page in mvpPages"
          :key="page.path"
          class="rounded-lg border bg-background px-4 py-3 text-sm transition-colors hover:border-primary/50 hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          :to="page.path"
        >
          <span class="block text-xs font-bold uppercase text-muted-foreground">{{ page.domain }}</span>
          <span class="mt-1 block font-semibold text-foreground">{{ t(page.labelKey) }}</span>
        </RouterLink>
      </div>
    </section>
  </BusinessLayout>
</template>
