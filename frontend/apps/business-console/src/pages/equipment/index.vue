<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import {
  describeEquipmentReason,
  equipmentStatusTone,
  useBusinessEquipmentOverview,
  type EquipmentTone,
} from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
  Button,
  DataTable,
  DropdownMenuItem,
  Input,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  Toolbar,
} from '@nerv-iip/ui'
import { ActivityIcon, BellRingIcon, EyeIcon, RefreshCwIcon, WrenchIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '设备运行看板' } })

const router = useRouter()
const { activeBlocks, devices, filters, overviewError, overviewPending, refreshOverview } = useBusinessEquipmentOverview()

const errorMessage = computed(() => formatError(overviewError.value))
const runningCount = computed(() => devices.value.filter((d) => equipmentStatusTone(d.currentState) === 'success').length)
const faultCount = computed(() => devices.value.filter((d) => equipmentStatusTone(d.currentState) === 'danger').length)
const alarmCount = computed(() => devices.value.reduce((total, d) => total + (d.activeAlarmCount ?? 0), 0))

type Device = (typeof devices)['value'][number]
const columns: DataTableColumn<Device>[] = [
  { key: 'deviceAssetId', header: '设备', cellClass: 'font-medium', accessor: (r) => r.deviceAssetId ?? '无编号' },
  { key: 'currentState', header: '状态', width: 'w-24' },
  { key: 'isSourceFresh', header: '数据新鲜', width: 'w-24' },
  { key: 'activeAlarmCount', header: '报警', align: 'end', width: 'w-20' },
  { key: 'activeBlockCount', header: '阻塞', align: 'end', width: 'w-20' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function badgeVariant(tone: EquipmentTone) {
  if (tone === 'success') return 'success'
  if (tone === 'danger') return 'destructive'
  return 'secondary'
}
function statusLabel(status?: string | null) {
  const labels: Record<string, string> = { down: '停机', faulted: '故障', idle: '空闲', offline: '离线', ready: '就绪', running: '运行中', stopped: '停止' }
  return status ? (labels[status.toLowerCase()] ?? status) : '未知'
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
    <PageHeader title="设备运行看板" :breadcrumbs="[{ label: '设备监控（IoT）' }]" :count="`${devices.length} 台设备`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/alarms"><BellRingIcon aria-hidden="true" />查看报警</RouterLink>
        </Button>
        <Button size="sm" type="button" variant="outline" :disabled="overviewPending" @click="refreshOverview">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="运行就绪" :value="runningCount" hint="运行 / 就绪 / 空闲" />
      <SectionCard description="异常停机" :value="faultCount" hint="故障 / 停止 / 离线 / 停机" />
      <SectionCard description="未解除报警" :value="alarmCount" hint="设备当前报警" />
      <SectionCard description="阻塞中" :value="activeBlocks.length" hint="影响排程或执行" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.deviceAssetIds" class="h-9 w-72" placeholder="默认全部设备；逗号分隔设备号可缩小范围" aria-label="设备范围（留空显示全部）" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(360px,0.75fr)]">
      <DataTable
        :columns="columns"
        :rows="devices"
        :row-key="(r) => r.deviceAssetId ?? '无'"
        :loading="overviewPending"
        empty-message="暂无设备运行事实。请先在基础数据登记设备资产，或调整上方设备范围后再试。"
      >
        <template #cell-deviceAssetId="{ row }">
          <RouterLink :to="`/equipment/${row.deviceAssetId}`" class="font-medium text-brand underline-offset-4 hover:underline">
            {{ row.deviceAssetId ?? '无编号' }}
          </RouterLink>
        </template>
        <template #cell-currentState="{ row }">
          <Badge class="rounded-sm" :variant="badgeVariant(equipmentStatusTone(row.currentState))">{{ statusLabel(row.currentState) }}</Badge>
        </template>
        <template #cell-isSourceFresh="{ row }">
          <Badge class="rounded-sm" :variant="row.isSourceFresh ? 'success' : 'warning'">{{ row.isSourceFresh ? '正常' : '过期' }}</Badge>
        </template>
        <template #cell-activeAlarmCount="{ row }"><span class="tabular-nums">{{ row.activeAlarmCount ?? 0 }}</span></template>
        <template #cell-activeBlockCount="{ row }"><span class="tabular-nums">{{ row.activeBlockCount ?? 0 }}</span></template>
        <template #cell-actions="{ row }">
          <RowActions :label="`设备操作 ${row.deviceAssetId ?? ''}`">
            <DropdownMenuItem as-child>
              <RouterLink :to="`/equipment/${row.deviceAssetId}`"><EyeIcon aria-hidden="true" />查看详情</RouterLink>
            </DropdownMenuItem>
            <DropdownMenuItem @click="recordDowntime(row.deviceAssetId)">
              <WrenchIcon aria-hidden="true" />
              记录停机
            </DropdownMenuItem>
          </RowActions>
        </template>
      </DataTable>

      <div class="rounded-lg border bg-card">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">当前阻塞</h2>
          <Badge class="rounded-sm" variant="secondary">{{ activeBlocks.length }}</Badge>
        </div>
        <div class="grid gap-3 p-4">
          <div v-for="block in activeBlocks" :key="`${block.deviceAssetId}-${block.reasonCode}-${block.startUtc}`" class="grid gap-2 rounded-lg border p-3">
            <div class="flex min-w-0 items-center justify-between gap-2">
              <div class="min-w-0">
                <p class="truncate text-sm font-semibold text-foreground">{{ block.deviceAssetId ?? '无设备' }}</p>
                <p class="truncate text-xs text-muted-foreground">{{ block.workCenterId ?? '未绑定工作中心' }}</p>
              </div>
              <Badge class="rounded-sm" variant="destructive">{{ describeEquipmentReason(block.reasonCode ?? '').label }}</Badge>
            </div>
            <p class="text-sm leading-6 text-muted-foreground">{{ describeEquipmentReason(block.reasonCode ?? '').nextStep }}</p>
            <div class="flex flex-wrap gap-2 text-xs text-muted-foreground">
              <span><ActivityIcon class="inline size-3" /> {{ formatDateTime(block.startUtc) }}</span>
              <span>{{ formatDateTime(block.endUtc) }}</span>
              <span v-if="block.sourceReferenceId">关联单据 {{ block.sourceReferenceId }}</span>
              <span v-if="block.substituteDeviceAssetIds?.length">替代设备 {{ block.substituteDeviceAssetIds.join(', ') }}</span>
            </div>
            <Button size="sm" type="button" variant="outline" class="justify-self-start" @click="recordDowntime(block.deviceAssetId)">
              <WrenchIcon aria-hidden="true" />
              记录停机
            </Button>
          </div>
          <div v-if="!activeBlocks.length" class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
            当前没有设备阻塞窗口。
          </div>
        </div>
      </div>
    </div>
  </BusinessLayout>
</template>
