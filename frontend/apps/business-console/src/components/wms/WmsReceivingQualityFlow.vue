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
  inboundOrderNo: string
  gates: BusinessConsoleWmsReceivingQualityGateItem[]
  supplierReturns: BusinessConsoleWmsSupplierReturnItem[]
  loading: boolean
  error?: unknown
}>()

const orderGates = computed(() =>
  props.gates.filter((gate) => gate.inboundOrderNo === props.inboundOrderNo),
)
const orderReturns = computed(() =>
  props.supplierReturns.filter((item) => item.inboundOrderNo === props.inboundOrderNo),
)

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
  if (orderGates.value.length === 0)
    return {
      label: '门禁缺少',
      value: 'unknown',
      description: '当前响应没有这张入库单的质检行，不能据此放行上架。',
    }

  const statuses = orderGates.value.map((gate) => normalize(gate.qualityGateStatus))
  if (statuses.includes('rejected'))
    return {
      label: '不合格',
      value: 'rejected',
      description: '不合格物料须留在隔离库位并按真实退供单处理。',
    }
  if (
    statuses.includes('pending') ||
    statuses.includes('inspection') ||
    statuses.includes('in-progress')
  ) {
    return {
      label: '待检',
      value: 'pending',
      description: '检验完成前不能上架，物料留在待检暂存位置。',
    }
  }
  if (statuses.includes('conditional-release') || statuses.includes('conditionalrelease')) {
    return {
      label: '条件放行',
      value: 'conditional-release',
      description: '已允许受限上架，请按质量处置要求操作。',
    }
  }
  if (
    statuses.every(
      (status) =>
        status === 'not-required' ||
        status === 'passed' ||
        status === 'accepted' ||
        status === 'available',
    )
  ) {
    return {
      label: statuses.every((status) => status === 'not-required') ? '免检' : '合格',
      value: 'released',
      description: statuses.every((status) => status === 'not-required')
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
  () => summary.value.value !== 'released' && summary.value.value !== 'conditional-release',
)
const putawayLabel = computed(() =>
  orderGates.value.some((gate) => {
    const status = normalize(gate.qualityGateStatus)
    return status === 'conditional-release' || status === 'conditionalrelease'
  })
    ? '受限上架'
    : '上架',
)
const visibleLocations = computed(() => [
  ...new Set(orderGates.value.map((gate) => gate.stagingLocationCode).filter(Boolean)),
])
const inspectionRecordIds = computed(() => [
  ...new Set(orderGates.value.map((gate) => gate.inspectionRecordId).filter(Boolean)),
])

function normalize(value?: string | null) {
  return (value ?? '').trim().toLowerCase().replaceAll('_', '-')
}

function returnFor(gate: BusinessConsoleWmsReceivingQualityGateItem) {
  return orderReturns.value.find(
    (item) => item.inboundOrderLineNo === gate.lineNo && item.skuCode === gate.skuCode,
  )
}

function gateLabel(gate: BusinessConsoleWmsReceivingQualityGateItem) {
  const status = normalize(gate.qualityGateStatus)
  if (status === 'not-required') return '免检'
  if (status === 'conditional-release' || status === 'conditionalrelease') return '条件放行'
  if (status === 'rejected') return '不合格'
  if (status === 'pending' || status === 'inspection' || status === 'in-progress') return '待检'
  if (status === 'passed' || status === 'accepted' || status === 'available') return '合格'
  return '门禁待确认'
}
</script>

<template>
  <div class="grid gap-2 rounded-lg border bg-muted/20 p-3" data-testid="receiving-quality-flow">
    <div class="flex flex-wrap items-center gap-2">
      <NvStatusBadge :value="summary.label" />
      <span class="text-sm font-medium">收货 → 待检 → 上架</span>
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
      待检期间上架按钮已禁用，检验状态回写后请刷新本页。
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
      v-for="gate in orderGates"
      :key="gate.inboundOrderLineId"
      class="flex flex-wrap items-center gap-2 text-xs"
    >
      <span class="font-medium">第 {{ gate.lineNo }} 行 · {{ gate.skuCode }}</span>
      <NvStatusBadge :value="gateLabel(gate)" />
      <span v-if="normalize(gate.qualityGateStatus) === 'rejected'" class="text-destructive">
        {{ gate.qualityDispositionReason || '质量不合格' }}
      </span>
      <template v-if="returnFor(gate)">
        <span class="text-destructive"
          >退供应商 {{ returnFor(gate)?.supplierReturnNo }} · 隔离
          {{ returnFor(gate)?.locationCode }}</span
        >
      </template>
      <span
        v-else-if="normalize(gate.qualityGateStatus) === 'rejected'"
        class="text-muted-foreground"
      >
        退供单：暂无真实记录
      </span>
    </div>

    <div class="flex flex-wrap items-center gap-2">
      <NvButton
        size="sm"
        type="button"
        variant="outline"
        :disabled="putawayDisabled"
        :aria-label="`上架 ${inboundOrderNo}`"
      >
        {{ putawayLabel }}
      </NvButton>
      <NvButton size="sm" type="button" variant="ghost" as-child>
        <RouterLink
          :to="{ path: '/quality/inspection-tasks', query: { sourceDocumentNo: inboundOrderNo } }"
          :aria-label="`查看检验任务 ${inboundOrderNo}`"
        >
          查看检验任务
        </RouterLink>
      </NvButton>
      <span v-if="inspectionRecordIds.length" class="text-xs text-muted-foreground"
        >检验记录：{{ inspectionRecordIds.join('、') }}</span
      >
    </div>
  </div>
</template>
