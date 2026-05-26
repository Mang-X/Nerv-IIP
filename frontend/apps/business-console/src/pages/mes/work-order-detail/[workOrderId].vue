<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesWorkOrderDetail } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工单详情',
  },
})

const route = useRoute()
const {
  detail,
  detailError,
  detailPending,
  filters,
  materialReadiness,
  materialReadinessError,
  materialReadinessPending,
  refreshDetail,
  refreshMaterialReadiness,
} = useMesWorkOrderDetail()

watch(
  () => (route.params as Record<string, string | string[] | undefined>).workOrderId,
  (value) => {
    filters.workOrderId = Array.isArray(value) ? value[0] ?? 'WO-001' : value ?? 'WO-001'
  },
  { immediate: true },
)

const operationTasks = computed(() => detail.value?.operationTasks ?? [])
const materialRows = computed(() => materialReadiness.value?.items ?? [])
const blockingReasons = computed(() => [
  ...(detail.value?.blockingReasons ?? []),
  ...(materialReadiness.value?.blockingReasons ?? []),
])
const errorMessage = computed(
  () => formatError(detailError.value) || formatError(materialReadinessError.value),
)

function refreshAll() {
  void refreshDetail()
  void refreshMaterialReadiness()
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="MES"
        title="工单详情"
        :summary="`查看工单 ${filters.workOrderId} 的工序、用料和开工阻塞。`"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="detailPending || materialReadinessPending" @click="refreshAll">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-3">
          <Field>
            <FieldLabel for="detail-org">组织</FieldLabel>
            <Input id="detail-org" v-model="filters.organizationId" />
          </Field>
          <Field>
            <FieldLabel for="detail-env">环境</FieldLabel>
            <Input id="detail-env" v-model="filters.environmentId" />
          </Field>
          <Field>
            <FieldLabel for="detail-work-order">工单号</FieldLabel>
            <Input id="detail-work-order" v-model="filters.workOrderId" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-4">
        <BusinessMetricCell label="工单状态" :value="detail?.status ?? '未知'" :detail="detail?.skuId ?? '无 SKU'" />
        <BusinessMetricCell label="计划数量" :value="formatQuantity(detail?.quantity)" detail="工单计划量" />
        <BusinessMetricCell label="工序数" :value="operationTasks.length" detail="执行任务" />
        <BusinessMetricCell label="用料状态" :value="materialReadiness?.readinessStatus ?? '未知'" detail="齐套检查" />
      </div>

      <div v-if="blockingReasons.length" class="rounded-lg border bg-background p-4">
        <h2 class="text-sm font-semibold text-foreground">阻塞原因</h2>
        <div class="mt-3 flex flex-wrap gap-2">
          <Badge v-for="reason in blockingReasons" :key="reason" variant="secondary">{{ reason }}</Badge>
        </div>
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">工序任务</h2>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>任务</TableHead>
                <TableHead>状态</TableHead>
                <TableHead>序号</TableHead>
                <TableHead>工作中心</TableHead>
                <TableHead>设备</TableHead>
                <TableHead>班次</TableHead>
                <TableHead>开始</TableHead>
                <TableHead>质量</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="task in operationTasks" :key="task.operationTaskId">
                <TableCell class="font-medium">{{ task.operationTaskId ?? '无' }}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ task.status ?? '未知' }}</Badge>
                </TableCell>
                <TableCell class="tabular-nums">{{ task.operationSequence ?? 0 }}</TableCell>
                <TableCell>{{ task.workCenterId ?? '无' }}</TableCell>
                <TableCell>{{ task.deviceAssetId ?? '未指定' }}</TableCell>
                <TableCell>{{ task.shiftId ?? '未指定' }}</TableCell>
                <TableCell>{{ formatDateTime(task.startedAtUtc ?? task.plannedStartUtc) }}</TableCell>
                <TableCell>{{ task.qualityStatus ?? '未检' }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!operationTasks.length && !detailPending" :colspan="8">
                暂无工序任务。
              </TableEmpty>
              <TableEmpty v-if="detailPending" :colspan="8">正在加载工单详情...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">用料齐套</h2>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>物料</TableHead>
                <TableHead>批次</TableHead>
                <TableHead class="text-right">需求</TableHead>
                <TableHead class="text-right">可用</TableHead>
                <TableHead class="text-right">已备</TableHead>
                <TableHead class="text-right">短缺</TableHead>
                <TableHead>状态</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in materialRows" :key="`${row.materialId}-${row.materialLotId}`">
                <TableCell class="font-medium">{{ row.materialId ?? '无' }}</TableCell>
                <TableCell>{{ row.materialLotId ?? '未指定' }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.requiredQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.availableQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.stagedQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.shortageQuantity) }}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{{ row.status ?? '未知' }}</Badge>
                </TableCell>
              </TableRow>
              <TableEmpty v-if="!materialRows.length && !materialReadinessPending" :colspan="7">
                暂无用料行。
              </TableEmpty>
              <TableEmpty v-if="materialReadinessPending" :colspan="7">正在加载用料齐套...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
