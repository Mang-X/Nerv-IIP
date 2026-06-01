<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useBusinessEquipmentAlarms } from '@/composables/useBusinessEquipment'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Badge, Button, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '设备报警' } })

const { alarms, alarmsError, alarmsPending, refreshAlarms } = useBusinessEquipmentAlarms()

const errorMessage = computed(() => formatError(alarmsError.value))
const criticalCount = computed(() =>
  alarms.value.filter((alarm) => ['critical', 'blocked'].includes((alarm.severity ?? '').toLowerCase())).length,
)
const warningCount = computed(() =>
  alarms.value.filter((alarm) => (alarm.severity ?? '').toLowerCase() === 'warning').length,
)

function severityLabel(value?: string | null) {
  const labels: Record<string, string> = {
    blocked: '阻塞',
    critical: '严重',
    info: '信息',
    warning: '预警',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}

function severityVariant(value?: string | null) {
  const severity = value?.toLowerCase()
  if (severity === 'critical' || severity === 'blocked') return 'destructive'
  if (severity === 'warning') return 'warning'
  return 'secondary'
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
        title="设备报警"
        summary="查看当前未解除的设备报警，优先处理严重报警后再释放生产排程。"
        badge="报警"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="alarmsPending" @click="refreshAlarms">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <BusinessFormStatus :error="errorMessage" />

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="报警数量" :value="alarms.length" detail="当前未解除" />
        <BusinessMetricCell label="严重报警" :value="criticalCount" detail="需立即处理" />
        <BusinessMetricCell label="预警报警" :value="warningCount" detail="需要跟踪" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">当前/近期报警</h2>
          <span class="text-sm text-muted-foreground">按设备进入详情</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>报警</TableHead>
                <TableHead>设备</TableHead>
                <TableHead>报警代码</TableHead>
                <TableHead>级别</TableHead>
                <TableHead>发生时间</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="alarm in alarms" :key="alarm.alarmEventId ?? `${alarm.deviceAssetId}-${alarm.alarmCode}`">
                <TableCell class="font-medium">{{ alarm.alarmEventId ?? '无编号' }}</TableCell>
                <TableCell>
                  <RouterLink
                    class="text-primary underline-offset-4 hover:underline"
                    :to="{ path: `/equipment/${alarm.deviceAssetId}` }"
                  >
                    {{ alarm.deviceAssetId ?? '无设备' }}
                  </RouterLink>
                </TableCell>
                <TableCell>{{ alarm.alarmCode ?? '无代码' }}</TableCell>
                <TableCell>
                  <Badge class="rounded-sm" :variant="severityVariant(alarm.severity)">
                    {{ severityLabel(alarm.severity) }}
                  </Badge>
                </TableCell>
                <TableCell>{{ formatDateTime(alarm.raisedAtUtc) }}</TableCell>
              </TableRow>
              <TableEmpty v-if="alarmsPending" :colspan="5">正在加载设备报警...</TableEmpty>
              <TableEmpty v-if="!alarms.length && !alarmsPending" :colspan="5">当前没有未解除设备报警。</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
