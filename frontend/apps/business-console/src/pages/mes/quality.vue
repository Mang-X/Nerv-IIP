<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import { mesQualityStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesRelatedQualityItems } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePagination,
  DataTablePro,
  PageHeader,
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '质量与不良' } })

const route = useRoute()
const { filters, qualityItems, qualityItemsError, qualityItemsPending, qualityItemsTotal, refreshQualityItems } = useMesRelatedQualityItems()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => { filters.status = value === 'all' ? undefined : value },
})
const errorMessage = computed(() => formatError(qualityItemsError.value))
// 上下文穿透：从工单/工序带入时显示来源并提供返回链接。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
const openCount = computed(() => qualityItems.value.filter((r) => (r.status ?? '').toLowerCase() !== 'closed').length)
const ncrCount = computed(() => qualityItems.value.filter((r) => r.ncrId).length)

type QualityRow = (typeof qualityItems)['value'][number]
const columns: DataTableProColumn<QualityRow>[] = [
  { key: 'qualityItemId', header: '质量项', cellClass: 'font-medium', accessor: (r) => r.qualityItemId ?? '无' },
  { key: 'sourceType', header: '来源类型', accessor: (r) => r.sourceType ?? '未指定' },
  { key: 'sourceDocumentId', header: '来源单据', accessor: (r) => r.sourceDocumentId ?? '未指定' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'defectCode', header: '缺陷代码', accessor: (r) => r.defectCode ?? '无' },
  { key: 'ncrId', header: 'NCR', accessor: (r) => r.ncrId ?? '无' },
]

function isWorkOrder(value?: string | null) {
  return !!value && /^WO/i.test(value)
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="质量与不良" :breadcrumbs="[{ label: '制造执行' }]" :count="`${qualityItemsTotal} 条质量项`">
      <template #actions>
        <ButtonPro v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`">返回工单 {{ contextWorkOrderId }}</RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="qualityItemsPending" @click="refreshQualityItems">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="质量项" :value="qualityItemsTotal" hint="后端筛选总数" />
      <SectionCard description="本页未关闭" :value="openCount" hint="当前页待处理" />
      <SectionCard description="本页关联 NCR" :value="ncrCount" hint="当前页已开 NCR" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="质量状态"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="option in mesQualityStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
      :columns="columns"
      :rows="qualityItems"
      row-key="qualityItemId"
      :loading="qualityItemsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无质量或不良记录。工单/工序产生检验、不良或质量阻塞后会出现在这里。"
    >
      <template #cell-sourceDocumentId="{ row }">
        <RouterLink
          v-if="isWorkOrder(row.sourceDocumentId)"
          :to="`/mes/work-orders/${encodeURIComponent(row.sourceDocumentId!)}`"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.sourceDocumentId }}
        </RouterLink>
        <span v-else>{{ row.sourceDocumentId ?? '未指定' }}</span>
      </template>
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status" /></template>
      <template #cell-ncrId="{ row }">
        <RouterLink
          v-if="row.ncrId"
          :to="{ path: '/quality/ncrs', query: { ncrId: row.ncrId, workOrderId: isWorkOrder(row.sourceDocumentId) ? row.sourceDocumentId : undefined } }"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.ncrId }}
        </RouterLink>
        <span v-else class="text-muted-foreground">无</span>
      </template>
    </DataTablePro>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="qualityItemsTotal" />
  </BusinessLayout>
</template>
