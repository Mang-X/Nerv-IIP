<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import {
  describeEquipmentReason,
  equipmentStatusTone,
  useBusinessEquipmentOverview,
  type EquipmentTone,
} from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Badge, Button, Field, FieldLabel, Input, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { ActivityIcon, BellRingIcon, EyeIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '设备运行看板' } })

const { activeBlocks, devices, filters, overviewError, overviewPending, refreshOverview } =
  useBusinessEquipmentOverview()

const errorMessage = computed(() => formatError(overviewError.value))
const runningCount = computed(() =>
  devices.value.filter((device) => equipmentStatusTone(device.currentState) === 'success').length,
)
const faultCount = computed(() =>
  devices.value.filter((device) => equipmentStatusTone(device.currentState) === 'danger').length,
)
const alarmCount = computed(() =>
  devices.value.reduce((total, device) => total + (device.activeAlarmCount ?? 0), 0),
)

function badgeVariant(tone: EquipmentTone) {
  if (tone === 'success') return 'success'
  if (tone === 'danger') return 'destructive'
  return 'secondary'
}

function statusLabel(status?: string | null) {
  const labels: Record<string, string> = {
    down: '停机',
    faulted: '故障',
    idle: '空闲',
    offline: '离线',
    ready: '就绪',
    running: '运行中',
    stopped: '停止',
  }
  return status ? (labels[status.toLowerCase()] ?? status) : '未知'
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="设备异常"
        title="设备运行看板"
        summary="查看关键设备当前状态、未解除报警和会影响排程的设备占用窗口。"
        badge="IIoT"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="overviewPending" @click="refreshOverview">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <Field>
          <FieldLabel for="equipment-device-scope">设备范围</FieldLabel>
          <Input id="equipment-device-scope" v-model="filters.deviceAssetIds" placeholder="DEV-OIL-01,DEV-PACK-01" />
        </Field>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-4">
        <BusinessMetricCell label="设备数" :value="devices.length" detail="当前范围" />
        <BusinessMetricCell label="运行就绪" :value="runningCount" detail="running / ready / idle" />
        <BusinessMetricCell label="异常停机" :value="faultCount" detail="faulted / stopped / offline / down" />
        <BusinessMetricCell label="未解除报警" :value="alarmCount" detail="设备当前报警" />
      </div>

      <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(360px,0.75fr)]">
        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="flex items-center justify-between border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">设备状态</h2>
            <span class="text-sm text-muted-foreground">行内进入设备详情</span>
          </div>
          <div class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>设备</TableHead>
                  <TableHead>状态</TableHead>
                  <TableHead>数据新鲜</TableHead>
                  <TableHead class="text-right">报警</TableHead>
                  <TableHead class="text-right">阻塞</TableHead>
                  <TableHead class="text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="device in devices" :key="device.deviceAssetId ?? 'device'">
                  <TableCell class="font-medium">
                    <RouterLink
                      class="text-primary underline-offset-4 hover:underline"
                      :to="{ path: `/equipment/${device.deviceAssetId}` }"
                    >
                      {{ device.deviceAssetId ?? '无编号' }}
                    </RouterLink>
                  </TableCell>
                  <TableCell>
                    <Badge class="rounded-sm" :variant="badgeVariant(equipmentStatusTone(device.currentState))">
                      {{ statusLabel(device.currentState) }}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <Badge class="rounded-sm" :variant="device.isSourceFresh ? 'success' : 'warning'">
                      {{ device.isSourceFresh ? '正常' : '过期' }}
                    </Badge>
                  </TableCell>
                  <TableCell class="text-right tabular-nums">{{ device.activeAlarmCount ?? 0 }}</TableCell>
                  <TableCell class="text-right tabular-nums">{{ device.activeBlockCount ?? 0 }}</TableCell>
                  <TableCell class="text-right">
                    <Button as-child size="sm" type="button" variant="ghost">
                      <RouterLink :to="{ path: `/equipment/${device.deviceAssetId}` }">
                        <EyeIcon data-icon="inline-start" />
                        详情
                      </RouterLink>
                    </Button>
                  </TableCell>
                </TableRow>
                <TableEmpty v-if="overviewPending" :colspan="6">正在加载设备状态...</TableEmpty>
                <TableEmpty v-if="!devices.length && !overviewPending" :colspan="6">
                  当前范围没有设备运行事实。
                </TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>

        <div class="rounded-lg border bg-background">
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
              <p class="text-sm leading-6 text-muted-foreground">
                {{ describeEquipmentReason(block.reasonCode ?? '').nextStep }}
              </p>
              <div class="flex flex-wrap gap-2 text-xs text-muted-foreground">
                <span><ActivityIcon class="inline size-3" /> {{ formatDateTime(block.startUtc) }}</span>
                <span>{{ formatDateTime(block.endUtc) }}</span>
                <span v-if="block.sourceReferenceId">关联单据 {{ block.sourceReferenceId }}</span>
                <span v-if="block.substituteDeviceAssetIds?.length">
                  替代设备 {{ block.substituteDeviceAssetIds.join(', ') }}
                </span>
              </div>
            </div>
            <div v-if="!activeBlocks.length" class="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
              当前没有设备阻塞窗口。
            </div>
            <Button as-child size="sm" type="button" variant="outline">
              <RouterLink to="/equipment/alarms">
                <BellRingIcon data-icon="inline-start" />
                查看报警
              </RouterLink>
            </Button>
          </div>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
