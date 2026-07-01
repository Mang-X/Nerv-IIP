<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useBusinessEquipmentAlarms } from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  BadgePro,
  ButtonPro,
  DataTablePro,
  DropdownMenuProItem,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
} from '@nerv-iip/ui'
import { EyeIcon, RefreshCwIcon, WrenchIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '设备报警', requiredPermissions: ['business.iiot.alarms.read'] } })

const router = useRouter()
const { alarms, alarmsError, alarmsPending, refreshAlarms } = useBusinessEquipmentAlarms()

const errorMessage = computed(() => formatError(alarmsError.value))
const criticalCount = computed(() => alarms.value.filter((a) => ['critical', 'blocked'].includes((a.severity ?? '').toLowerCase())).length)
const warningCount = computed(() => alarms.value.filter((a) => (a.severity ?? '').toLowerCase() === 'warning').length)

type Alarm = (typeof alarms)['value'][number]
const columns: DataTableProColumn<Alarm>[] = [
  { key: 'alarmEventId', header: '报警', cellClass: 'font-medium', accessor: (r) => r.alarmEventId ?? '无编号' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '无设备' },
  { key: 'alarmCode', header: '报警代码', accessor: (r) => r.alarmCode ?? '无代码' },
  { key: 'severity', header: '级别', width: 'w-24' },
  { key: 'raisedAtUtc', header: '发生时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function severityLabel(value?: string | null) {
  const labels: Record<string, string> = { blocked: '阻塞', critical: '严重', info: '信息', warning: '预警' }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function severityVariant(value?: string | null) {
  const severity = value?.toLowerCase()
  if (severity === 'critical' || severity === 'blocked') return 'danger'
  if (severity === 'warning') return 'warning'
  return 'neutral'
}
function recordDowntime(deviceAssetId?: string | null) {
  void router.push({ path: '/mes/downtime', query: { deviceAssetId: deviceAssetId ?? undefined } })
}
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
    <PageHeader title="设备报警" :breadcrumbs="[{ label: '设备监控（IoT）' }]" :count="`${alarms.length} 条未解除`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment">设备看板</RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="alarmsPending" @click="refreshAlarms">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="报警数量" :value="alarms.length" hint="当前未解除" />
      <SectionCard description="严重报警" :value="criticalCount" hint="需立即处理" />
      <SectionCard description="预警报警" :value="warningCount" hint="需要跟踪" />
    </SectionCards>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
      :columns="columns"
      :rows="alarms"
      :row-key="(r) => r.alarmEventId ?? `${r.deviceAssetId}-${r.alarmCode}`"
      :loading="alarmsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前没有未解除设备报警。"
    >
      <template #cell-deviceAssetId="{ row }">
        <RouterLink :to="`/equipment/${row.deviceAssetId}`" class="text-brand underline-offset-4 hover:underline">{{ row.deviceAssetId ?? '无设备' }}</RouterLink>
      </template>
      <template #cell-severity="{ row }">
        <BadgePro class="rounded-sm" :variant="severityVariant(row.severity)">{{ severityLabel(row.severity) }}</BadgePro>
      </template>
      <template #cell-raisedAtUtc="{ row }">{{ formatDateTime(row.raisedAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <RowActions :label="`报警操作 ${row.alarmEventId ?? ''}`">
          <DropdownMenuProItem as-child>
            <RouterLink :to="`/equipment/${row.deviceAssetId}`"><EyeIcon aria-hidden="true" />设备详情</RouterLink>
          </DropdownMenuProItem>
          <DropdownMenuProItem @click="recordDowntime(row.deviceAssetId)">
            <WrenchIcon aria-hidden="true" />
            记录停机
          </DropdownMenuProItem>
        </RowActions>
      </template>
    </DataTablePro>
  </BusinessLayout>
</template>
