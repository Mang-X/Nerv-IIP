<script setup lang="ts">
import type {
  BusinessConsoleEngineeringBomItem,
  BusinessConsoleProductionVersionItem,
  BusinessConsoleRoutingItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn, StatusTone } from '@nerv-iip/ui'
import { useBusinessProductEngineering } from '@/composables/useBusinessProductEngineering'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  DataTable,
  Field,
  FieldGroup,
  FieldLabel,
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

definePage({ meta: { requiresAuth: true, title: '工程版本' } })

const {
  boms,
  bomsError,
  bomsPending,
  filters,
  productionVersions,
  productionVersionsError,
  productionVersionsPending,
  refreshEngineering,
  resolvedProductionVersion,
  resolveError,
  resolveFilters,
  resolvePending,
  routings,
  routingsError,
  routingsPending,
} = useBusinessProductEngineering()

const loading = computed(() => bomsPending.value || routingsPending.value || productionVersionsPending.value || resolvePending.value)
const errorMessage = computed(() =>
  formatError(bomsError.value) || formatError(routingsError.value)
  || formatError(productionVersionsError.value) || formatError(resolveError.value),
)
const publishedBomCount = computed(() => boms.value.filter((i) => isPublished(i.status)).length)
const publishedRoutingCount = computed(() => routings.value.filter((i) => isPublished(i.status)).length)
const activeProductionVersionCount = computed(() => productionVersions.value.filter((i) => i.status?.toLowerCase() === 'active').length)

const lifecycleOptions = [
  { label: '已发布', value: 'Published' },
  { label: '草稿', value: 'Draft' },
  { label: '已归档', value: 'Archived' },
]
const productionVersionStatusOptions = [
  { label: '有效', value: 'active' },
  { label: '已归档', value: 'archived' },
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
const pvColumns: DataTableColumn<BusinessConsoleProductionVersionItem>[] = [
  { key: 'productionVersionId', header: '生产版本', cellClass: 'font-medium' },
  { key: 'skuCode', header: 'SKU' },
  { key: 'binding', header: 'MBOM / 工艺路线' },
  { key: 'lotSize', header: '批量', width: 'w-28' },
  { key: 'status', header: '状态', width: 'w-24' },
]

function isPublished(status?: string) {
  return status?.toLowerCase() === 'published'
}
function engStatus(status?: string | null): { label: string, tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'published') return { label: '已发布', tone: 'success' }
  if (s === 'active') return { label: '有效', tone: 'success' }
  if (s === 'draft') return { label: '草稿', tone: 'warning' }
  if (s === 'archived') return { label: '已归档', tone: 'neutral' }
  return { label: status || '未解析', tone: 'neutral' }
}
function formatDate(value?: string | null) {
  return value || '长期'
}
function formatRange(min?: number | null, max?: number | null) {
  if (min == null && max == null) return '不限'
  return `${min ?? 0} - ${max ?? '不限'}`
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

    <SectionCards :columns="3">
      <SectionCard description="已发布 MBOM" :value="publishedBomCount" hint="可供 MES / MRP 消费" />
      <SectionCard description="已发布工艺路线" :value="publishedRoutingCount" hint="按当前筛选" />
      <SectionCard description="有效生产版本" :value="activeProductionVersionCount" hint="可被解析绑定" />
    </SectionCards>

    <Card class="bg-gradient-to-t from-primary/5 to-card">
      <CardHeader>
        <CardTitle class="text-base">生产版本解析</CardTitle>
      </CardHeader>
      <CardContent class="grid gap-3 md:grid-cols-2 lg:grid-cols-[repeat(3,minmax(0,1fr))_auto]">
        <FieldGroup class="contents">
          <Field>
            <FieldLabel for="resolve-sku">SKU</FieldLabel>
            <Input id="resolve-sku" v-model="resolveFilters.skuCode" placeholder="输入 SKU 解析绑定" />
          </Field>
          <Field>
            <FieldLabel for="resolve-date">生效日</FieldLabel>
            <Input id="resolve-date" v-model="resolveFilters.effectiveDate" type="date" />
          </Field>
          <Field>
            <FieldLabel for="resolve-lot">批量</FieldLabel>
            <Input id="resolve-lot" v-model.number="resolveFilters.lotSize" min="0" type="number" />
          </Field>
        </FieldGroup>
        <div class="flex items-end">
          <StatusBadge :label="engStatus(resolvedProductionVersion?.status).label" :tone="engStatus(resolvedProductionVersion?.status).tone" />
        </div>
        <div class="grid gap-2 rounded-md border bg-muted/30 p-3 text-sm lg:col-span-full">
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">生产版本</span>
            <span class="font-medium">{{ resolvedProductionVersion?.productionVersionId ?? '无匹配' }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">MBOM</span>
            <span class="font-medium">{{ resolvedProductionVersion?.mbomVersionId ?? '无' }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">工艺路线</span>
            <span class="font-medium">{{ resolvedProductionVersion?.routingVersionId ?? '无' }}</span>
          </div>
        </div>
      </CardContent>
    </Card>

    <Toolbar v-model:search="skuSearch" search-placeholder="按 SKU 筛选工艺路线与生产版本">
      <template #filters>
        <Input v-model="filters.parentItemCode" class="h-9 w-40" placeholder="EBOM 父项（可选）" aria-label="EBOM 父项" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <Tabs default-value="mbom">
      <TabsList>
        <TabsTrigger value="mbom">MBOM 版本 ({{ boms.length }})</TabsTrigger>
        <TabsTrigger value="routing">工艺路线 ({{ routings.length }})</TabsTrigger>
        <TabsTrigger value="production-version">生产版本绑定 ({{ productionVersions.length }})</TabsTrigger>
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

      <TabsContent value="production-version" class="grid gap-3">
        <div class="flex justify-end">
          <Select v-model="filters.productionVersionStatus">
            <SelectTrigger class="h-9 w-32" aria-label="生产版本状态"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem v-for="o in productionVersionStatusOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <DataTable :columns="pvColumns" :rows="productionVersions" row-key="productionVersionId" :loading="productionVersionsPending" empty-message="当前范围没有生产版本。">
          <template #cell-binding="{ row }">
            <div class="flex flex-col gap-0.5">
              <span>{{ row.mbomVersionId }}</span>
              <span class="text-xs text-muted-foreground">{{ row.routingVersionId }}</span>
            </div>
          </template>
          <template #cell-lotSize="{ row }">{{ formatRange(row.lotSizeMin, row.lotSizeMax) }}</template>
          <template #cell-status="{ row }">
            <StatusBadge :label="engStatus(row.status).label" :tone="engStatus(row.status).tone" />
          </template>
        </DataTable>
      </TabsContent>
    </Tabs>
  </BusinessLayout>
</template>
