<script setup lang="ts">
import type {
  BusinessConsoleEngineeringBomItem,
  BusinessConsoleRoutingItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn, StatusTone } from '@nerv-iip/ui'
import { useBusinessProductEngineering } from '@/composables/useBusinessProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

// 过渡页：MBOM 版本与工艺路线的只读浏览。生产版本已独立为 /engineering/production-versions；
// MBOM / 工艺路线将在后续拆为独立页后，本页下线（见产品工程设计文档 §6 重构顺序 ②③④）。
definePage({ meta: { requiresAuth: true, title: '工程版本' } })

const {
  boms,
  bomsError,
  bomsPending,
  filters,
  refreshEngineering,
  routings,
  routingsError,
  routingsPending,
} = useBusinessProductEngineering()

const loading = computed(() => bomsPending.value || routingsPending.value)
const errorMessage = computed(() =>
  formatError(bomsError.value) || formatError(routingsError.value),
)
const releasedBomCount = computed(() => boms.value.filter((i) => isReleased(i.status)).length)
const releasedRoutingCount = computed(() => routings.value.filter((i) => isReleased(i.status)).length)

const lifecycleOptions = [
  { label: '已发布', value: 'Released' },
  { label: '草稿', value: 'Draft' },
  { label: '已归档', value: 'Archived' },
]

const skuSearch = computed({
  get: () => filters.skuCode ?? '',
  set: (value: string) => { filters.skuCode = value },
})

const bomColumns: DataTableColumn<BusinessConsoleEngineeringBomItem>[] = [
  { key: 'bomCode', header: '版本号', cellClass: 'font-medium' },
  { key: 'revision', header: '修订', width: 'w-20' },
  { key: 'parentItemCode', header: '父项' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
]
const routingColumns: DataTableColumn<BusinessConsoleRoutingItem>[] = [
  { key: 'routingCode', header: '路线号', cellClass: 'font-medium' },
  { key: 'revision', header: '修订', width: 'w-20' },
  { key: 'skuCode', header: 'SKU' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveDate', header: '生效日', width: 'w-28' },
]

function isReleased(status?: string) {
  return status?.toLowerCase() === 'released'
}
function engStatus(status?: string | null): { label: string, tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'released') return { label: '已发布', tone: 'success' }
  if (s === 'draft') return { label: '草稿', tone: 'warning' }
  if (s === 'archived') return { label: '已归档', tone: 'neutral' }
  return { label: status || '未知', tone: 'neutral' }
}
function formatDate(value?: string | null) {
  return value || '长期'
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="工程版本" :breadcrumbs="[{ label: '产品工程' }]">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="loading" @click="refreshEngineering">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="已发布 MBOM" :value="releasedBomCount" hint="可用于排产投产的 MBOM 版本" />
      <SectionCard description="已发布工艺路线" :value="releasedRoutingCount" hint="可用于排产投产的路线版本" />
    </SectionCards>

    <Toolbar v-model:search="skuSearch" search-placeholder="按 SKU 筛选工艺路线">
      <template #filters>
        <Input v-model="filters.parentItemCode" class="h-9 w-40" placeholder="EBOM 父项（可选）" aria-label="EBOM 父项" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <Tabs default-value="mbom">
      <TabsList>
        <TabsTrigger value="mbom">MBOM 版本 ({{ boms.length }})</TabsTrigger>
        <TabsTrigger value="routing">工艺路线 ({{ routings.length }})</TabsTrigger>
      </TabsList>

      <TabsContent value="mbom" class="grid gap-3">
        <div class="flex justify-end">
          <Select v-model="filters.bomStatus">
            <SelectTrigger class="h-9 w-32" aria-label="MBOM 状态"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem v-for="o in lifecycleOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <DataTable :columns="bomColumns" :rows="boms" :row-key="(r) => `${r.bomCode}:${r.revision}`" :loading="bomsPending" empty-message="当前范围没有 MBOM 版本。">
          <template #cell-status="{ row }">
            <StatusBadge :label="engStatus(row.status).label" :tone="engStatus(row.status).tone" />
          </template>
          <template #cell-effectiveDate="{ row }">{{ formatDate(row.effectiveDate) }}</template>
        </DataTable>
      </TabsContent>

      <TabsContent value="routing" class="grid gap-3">
        <div class="flex justify-end">
          <Select v-model="filters.routingStatus">
            <SelectTrigger class="h-9 w-32" aria-label="工艺路线状态"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem v-for="o in lifecycleOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <DataTable :columns="routingColumns" :rows="routings" :row-key="(r) => `${r.routingCode}:${r.revision}`" :loading="routingsPending" empty-message="当前范围没有工艺路线。">
          <template #cell-status="{ row }">
            <StatusBadge :label="engStatus(row.status).label" :tone="engStatus(row.status).tone" />
          </template>
          <template #cell-effectiveDate="{ row }">{{ formatDate(row.effectiveDate) }}</template>
        </DataTable>
      </TabsContent>
    </Tabs>
  </BusinessLayout>
</template>
