<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useMesCapacityImpacts } from '@/composables/useBusinessMes'
import { mesCapacityStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePro,
  InputPro,
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
import { computed, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '产能影响' } })

const {
  capacityImpacts,
  capacityImpactsError,
  capacityImpactsPending,
  capacityImpactsTotal,
  filters,
  refreshCapacityImpacts,
} = useMesCapacityImpacts()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })
const statusFilter = shallowRef('all')

const openCount = computed(() => capacityImpacts.value.filter((item) => item.status?.toLowerCase() === 'open').length)
const errorMessage = computed(() => formatError(capacityImpactsError.value))
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

type ImpactRow = (typeof capacityImpacts)['value'][number]
const columns: DataTableProColumn<ImpactRow>[] = [
  { key: 'impactId', header: '影响编号', cellClass: 'font-medium', accessor: (r) => r.impactId ?? '无' },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterName ?? r.workCenterCode ?? r.workCenterId ?? '无' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetName ?? r.deviceAssetCode ?? r.deviceAssetId ?? '未指定' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveFromUtc', header: '开始', width: 'w-44' },
  { key: 'effectiveToUtc', header: '结束', width: 'w-44' },
  { key: 'reasonCode', header: '原因', accessor: (r) => r.reasonCode ?? '无' },
]

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="产能影响" :breadcrumbs="[{ label: '制造执行' }]" :count="`${capacityImpactsTotal} 条影响`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="capacityImpactsPending" @click="refreshCapacityImpacts">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="影响记录" :value="capacityImpactsTotal" hint="后端筛选总数" />
      <SectionCard description="本页未恢复" :value="openCount" hint="当前页统计" />
      <SectionCard description="本页已恢复" :value="capacityImpacts.length - openCount" hint="当前页统计" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="影响状态"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="option in mesCapacityStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="capacityImpactsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="capacityImpacts"
      row-key="impactId"
      :loading="capacityImpactsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无产能影响。设备停机或维护冲突发生时会在这里汇总。"
    >
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status" /></template>
      <template #cell-effectiveFromUtc="{ row }">{{ formatDateTime(row.effectiveFromUtc) }}</template>
      <template #cell-effectiveToUtc="{ row }">{{ formatDateTime(row.effectiveToUtc) }}</template>
    </DataTablePro>

  </BusinessLayout>
</template>
