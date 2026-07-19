<script setup lang="ts">
import type {
  BusinessConsoleWmsReceivingQualityGateItem,
  BusinessConsoleWmsSupplierReturnItem,
} from '@nerv-iip/api-client'
import { NvButton, NvStatusBadge } from '@nerv-iip/ui'
import { AlertCircleIcon, CheckCircle2Icon, ClipboardCheckIcon, MapPinIcon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

const props = defineProps<{
  inboundOrderId?: string
  inboundOrderNo: string
  gates: BusinessConsoleWmsReceivingQualityGateItem[]
  supplierReturns: BusinessConsoleWmsSupplierReturnItem[]
  qualityGateStatus?: string
  isReleasedForPutaway?: boolean
  canManagePutaway: boolean
  canReadQuality: boolean
  loading: boolean
  error?: unknown
}>()

const orderGates = computed(() =>
  props.gates.filter((gate) => gate.inboundOrderNo === props.inboundOrderNo),
)
const orderReturns = computed(() =>
  props.supplierReturns.filter((item) => item.inboundOrderNo === props.inboundOrderNo),
)

type GateCategory =
  | 'not-required'
  | 'pending'
  | 'passed'
  | 'conditional-release'
  | 'rejected'
  | 'unknown'

function normalize(value?: string | null) {
  return (value ?? '').trim().toLowerCase()
}

function gateCategory(value?: string | null): GateCategory {
  const status = normalize(value)
  if (
    status === 'not-required' ||
    status === 'pending' ||
    status === 'passed' ||
    status === 'conditional-release' ||
    status === 'rejected'
  ) {
    return status
  }
  return 'unknown'
}

const orderCategory = computed<GateCategory>(() => {
  const categories = orderGates.value.map((gate) => gateCategory(gate.qualityGateStatus))
  if (categories.length === 0 || categories.includes('unknown')) return 'unknown'

  if (categories.includes('rejected')) return 'rejected'
  if (categories.includes('pending')) return 'pending'
  if (categories.includes('conditional-release')) return 'conditional-release'

  const serverCategory = gateCategory(props.qualityGateStatus)
  if (serverCategory !== 'unknown') return serverCategory

  if (categories.length > 0 && categories.every((category) => category === 'not-required')) {
    return 'not-required'
  }
  if (categories.length > 0 && categories.every((category) => category === 'passed')) {
    return 'passed'
  }
  return 'unknown'
})

const summary = computed(() => {
  if (props.loading)
    return {
      label: '读取门禁中',
      value: 'loading',
      description: '正在读取 WMS 返回的质检门禁事实。',
    }
  if (props.error)
    return {
      label: '门禁不可用',
      value: 'unknown',
      description: '无法确认质检状态，已停止上架操作。请刷新或联系管理员。',
    }
  if (orderGates.value.length === 0 || orderCategory.value === 'unknown')
    return {
      label: '门禁缺少',
      value: 'unknown',
      description: '当前响应没有这张入库单的质检行，不能据此放行上架。',
    }

  if (orderCategory.value === 'rejected')
    return {
      label: '不合格',
      value: 'rejected',
      description: '不合格物料须留在隔离库位并按真实退供单处理。',
    }
  if (orderCategory.value === 'pending') {
    return {
      label: '待检',
      value: 'pending',
      description: '检验完成前不能上架，物料留在待检暂存位置。',
    }
  }
  if (orderCategory.value === 'conditional-release') {
    return {
      label: '条件放行',
      value: 'conditional-release',
      description: '已允许受限上架，请按质量处置要求操作。',
    }
  }
  if (orderCategory.value === 'not-required' || orderCategory.value === 'passed') {
    return {
      label: orderCategory.value === 'not-required' ? '免检' : '合格',
      value: 'released',
      description:
        orderCategory.value === 'not-required'
          ? '已跳过待检，可进入上架。'
          : '检验已通过，可进入上架。',
    }
  }
  return {
    label: '门禁待确认',
    value: 'unknown',
    description: '服务端返回了未识别的门禁状态，已停止上架操作。',
  }
})

const putawayDisabled = computed(
  () =>
    !props.inboundOrderId?.trim() ||
    !props.canManagePutaway ||
    props.isReleasedForPutaway !== true ||
    (summary.value.value !== 'released' && summary.value.value !== 'conditional-release'),
)
const putawayLabel = computed(() =>
  orderCategory.value === 'conditional-release' ? '受限上架' : '上架',
)
const flowLabel = computed(() => {
  if (summary.value.value === 'rejected') return '收货 → 待检 → 隔离/退供'
  if (summary.value.value === 'pending') return '收货 → 待检 → 上架'
  if (summary.value.value === 'conditional-release') return '收货 → 待检 → 受限上架'
  if (orderCategory.value === 'not-required') return '收货 → 免检 → 上架'
  if (summary.value.value === 'released') return '收货 → 待检 → 合格上架'
  return '收货 → 质检门禁确认 → 上架'
})
const putawayRoute = computed(() => ({
  path: '/wms/putaway',
  query: {
    inboundOrderNo: props.inboundOrderNo,
    inboundOrderId: props.inboundOrderId,
    create: '1',
  },
}))
const putawayPermissionExplanation = computed(() => {
  if (props.loading || props.error) return ''
  if (!props.canManagePutaway) return '缺少收货管理权限，当前操作已禁用。'
  if (props.isReleasedForPutaway !== true) return 'WMS 尚未返回整单上架放行权限，当前操作已禁用。'
  if (!props.inboundOrderId?.trim()) return 'WMS 尚未返回真实入库单标识，无法创建上架任务。'
  return ''
})
const visibleLocations = computed(() => [
  ...new Set(orderGates.value.map((gate) => gate.stagingLocationCode).filter(Boolean)),
])
const hasPendingInspection = computed(() =>
  orderGates.value.some((gate) => gateCategory(gate.qualityGateStatus) === 'pending'),
)
const inspectionRecordIds = computed(() => [
  ...new Set(
    orderGates.value
      .map((gate) => gate.inspectionRecordId)
      .filter((inspectionRecordId): inspectionRecordId is string => Boolean(inspectionRecordId)),
  ),
])
const completedInspectionWithoutRecord = computed(
  () =>
    !hasPendingInspection.value &&
    orderCategory.value !== 'not-required' &&
    inspectionRecordIds.value.length === 0,
)

function returnFor(gate: BusinessConsoleWmsReceivingQualityGateItem) {
  if (!gate.lineNo || !gate.skuCode || !gate.inspectionRecordId) return undefined
  return orderReturns.value.find(
    (item) =>
      item.inboundOrderLineNo === gate.lineNo &&
      item.skuCode === gate.skuCode &&
      item.inspectionRecordId === gate.inspectionRecordId,
  )
}

const lineFacts = computed(() =>
  orderGates.value.map((gate) => ({ gate, supplierReturn: returnFor(gate) })),
)

function gateLabel(gate: BusinessConsoleWmsReceivingQualityGateItem) {
  const category = gateCategory(gate.qualityGateStatus)
  if (category === 'not-required') return '免检'
  if (category === 'conditional-release') return '条件放行'
  if (category === 'rejected') return '不合格'
  if (category === 'pending') return '待检'
  if (category === 'passed') return '合格'
  return '门禁待确认'
}
</script>

<template>
  <div class="grid gap-2 rounded-lg border bg-muted/20 p-3" data-testid="receiving-quality-flow">
    <div class="flex flex-wrap items-center gap-2">
      <NvStatusBadge :value="summary.label" />
      <span class="text-sm font-medium">{{ flowLabel }}</span>
      <span class="text-sm text-muted-foreground">{{ summary.description }}</span>
    </div>

    <p
      v-if="summary.value === 'rejected'"
      class="flex items-center gap-1 text-sm text-destructive"
      role="status"
    >
      <AlertCircleIcon class="size-4" aria-hidden="true" />
      不合格行已隔离，禁止直接上架；退供信息以 WMS 返回的真实记录为准。
    </p>
    <p
      v-else-if="summary.value === 'pending'"
      class="flex items-center gap-1 text-sm text-warning"
      role="status"
    >
      <ClipboardCheckIcon class="size-4" aria-hidden="true" />
      待检期间上架按钮已禁用，检验状态会自动刷新收敛。
    </p>
    <p
      v-else-if="summary.value === 'conditional-release'"
      class="flex items-center gap-1 text-sm text-warning"
      role="status"
    >
      <AlertCircleIcon class="size-4" aria-hidden="true" />
      条件放行仅支持受限上架，不代表无条件合格。
    </p>
    <p
      v-else-if="summary.value === 'released'"
      class="flex items-center gap-1 text-sm text-success"
      role="status"
    >
      <CheckCircle2Icon class="size-4" aria-hidden="true" />
      {{ summary.description }}
    </p>

    <div
      v-if="visibleLocations.length"
      class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground"
    >
      <span
        v-for="location in visibleLocations"
        :key="location"
        class="inline-flex items-center gap-1"
      >
        <MapPinIcon class="size-3.5" aria-hidden="true" />{{ location }}
      </span>
    </div>

    <div
      v-for="line in lineFacts"
      :key="line.gate.inboundOrderLineId"
      class="flex flex-wrap items-center gap-2 text-xs"
    >
      <span class="font-medium">第 {{ line.gate.lineNo }} 行 · {{ line.gate.skuCode }}</span>
      <NvStatusBadge :value="gateLabel(line.gate)" />
      <span
        v-if="gateCategory(line.gate.qualityGateStatus) === 'rejected'"
        class="text-destructive"
      >
        {{ line.gate.qualityDispositionReason || '质量不合格' }}
      </span>
      <template v-if="line.supplierReturn">
        <span class="text-destructive"
          >退供应商 {{ line.supplierReturn.supplierReturnNo }} · 隔离
          {{ line.supplierReturn.locationCode }}</span
        >
      </template>
      <span
        v-else-if="gateCategory(line.gate.qualityGateStatus) === 'rejected'"
        class="text-muted-foreground"
      >
        {{
          line.gate.inspectionRecordId
            ? '退供单：暂无真实记录'
            : '退供关联缺口：门禁未返回真实检验记录引用，未猜配退供单'
        }}
      </span>
    </div>

    <div class="flex flex-wrap items-center gap-2">
      <NvButton
        v-if="putawayDisabled"
        size="sm"
        type="button"
        variant="outline"
        :disabled="putawayDisabled"
        :aria-label="`上架 ${inboundOrderNo}`"
      >
        {{ putawayLabel }}
      </NvButton>
      <NvButton v-else size="sm" type="button" variant="outline" as-child>
        <RouterLink :to="putawayRoute" :aria-label="`${putawayLabel} ${inboundOrderNo}`">
          {{ putawayLabel }}
        </RouterLink>
      </NvButton>
      <span v-if="putawayDisabled && putawayPermissionExplanation" class="text-xs text-warning">
        {{ putawayPermissionExplanation }}
      </span>
      <NvButton
        v-if="hasPendingInspection && canReadQuality"
        size="sm"
        type="button"
        variant="ghost"
        as-child
      >
        <RouterLink
          :to="{ path: '/quality/inspection-tasks', query: { sourceDocumentNo: inboundOrderNo } }"
          :aria-label="`查看检验任务 ${inboundOrderNo}`"
        >
          查看检验任务
        </RouterLink>
      </NvButton>
      <NvButton
        v-for="inspectionRecordId in canReadQuality ? inspectionRecordIds : []"
        :key="inspectionRecordId"
        size="sm"
        type="button"
        variant="ghost"
        as-child
      >
        <RouterLink
          :to="{ path: '/quality/inspections', query: { inspectionRecordId } }"
          :aria-label="`查看检验记录 ${inspectionRecordId}`"
        >
          查看检验记录 {{ inspectionRecordId }}
        </RouterLink>
      </NvButton>
      <span v-if="orderCategory === 'not-required'" class="text-xs text-muted-foreground">
        免检无需检验任务
      </span>
      <span
        v-else-if="!canReadQuality && (hasPendingInspection || inspectionRecordIds.length)"
        class="text-xs text-muted-foreground"
      >
        缺少质量检验读取权限，无法打开检验任务或记录
      </span>
      <span v-else-if="completedInspectionWithoutRecord" class="text-xs text-muted-foreground">
        门禁未返回真实检验记录引用，无法定位检验记录
      </span>
    </div>
  </div>
</template>
