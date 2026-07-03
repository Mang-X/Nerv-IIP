<script setup lang="ts">
import type { BusinessConsoleWmsInventoryContext } from '@nerv-iip/api-client'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

const props = withDefaults(defineProps<{
  compact?: boolean
  context?: BusinessConsoleWmsInventoryContext
  gapMessage?: string
  locationCode?: string | null
  lotNo?: string | null
  serialNo?: string | null
  siteCode?: string | null
  skuCode?: string | null
  sourceDocumentId?: string | null
  sourceLabel?: string
  sourceWorkflow?: string
  title?: string
  uomCode?: string | null
}>(), {
  compact: false,
  context: undefined,
  gapMessage: '后端缺口：当前 WMS 列表未返回逐行可用量、批次/序列号、冻结和预留明细；可带当前业务上下文到 Inventory 查看。',
  locationCode: '',
  lotNo: '',
  serialNo: '',
  siteCode: '',
  skuCode: '',
  sourceDocumentId: '',
  sourceLabel: '来源单据',
  sourceWorkflow: '',
  title: '库存上下文',
  uomCode: '',
})

const skuValue = computed(() => props.skuCode || props.context?.skuCode || '')
const uomValue = computed(() => props.uomCode || props.context?.uomCode || '')
const siteValue = computed(() => props.siteCode || props.context?.siteCode || '')
const locationValue = computed(() => props.locationCode || props.context?.locationCode || '')
const lotValue = computed(() => props.lotNo || props.context?.lotNo || '')
const serialValue = computed(() => props.serialNo || props.context?.serialNo || '')

const onHandQuantity = computed(() => props.context?.onHandQuantity ?? 0)
const reservedQuantity = computed(() => props.context?.reservedQuantity ?? 0)
const availableQuantity = computed(() => props.context?.availableQuantity ?? 0)
const frozenQuantity = computed(() =>
  Math.max(onHandQuantity.value - availableQuantity.value - reservedQuantity.value, 0),
)
const contextStatus = computed(() => (props.context?.status ?? '').toLowerCase())
const hasQuantityFacts = computed(() =>
  !!props.context && (contextStatus.value === '' || contextStatus.value === 'ok' || contextStatus.value === 'available'),
)
const visibleLines = computed(() => props.context?.items?.slice(0, 3) ?? [])
const hasInventoryScope = computed(() => skuValue.value.trim().length > 0)
const hasSourceScope = computed(() =>
  props.sourceWorkflow.trim().length > 0 && (props.sourceDocumentId ?? '').trim().length > 0,
)

const inventoryQuery = computed(() => ({
  skuCode: skuValue.value || undefined,
  uomCode: uomValue.value || undefined,
  siteCode: siteValue.value || undefined,
  locationCode: locationValue.value || undefined,
  lotNo: lotValue.value || undefined,
  serialNo: serialValue.value || undefined,
}))
const sourceQuery = computed(() => ({
  sourceWorkflow: props.sourceWorkflow || undefined,
  sourceDocumentId: props.sourceDocumentId || undefined,
}))

function lineFrozen(line: NonNullable<BusinessConsoleWmsInventoryContext['items']>[number]) {
  return Math.max((line.onHandQuantity ?? 0) - (line.availableQuantity ?? 0) - (line.reservedQuantity ?? 0), 0)
}

function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}
</script>

<template>
  <section :class="compact ? 'grid gap-2 rounded-md border bg-muted/20 p-2 text-xs' : 'grid gap-3 rounded-md border bg-muted/20 p-3 text-sm'">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div>
        <h2 :class="compact ? 'font-medium text-foreground' : 'text-sm font-semibold text-foreground'">{{ title }}</h2>
        <p class="text-muted-foreground">
          <span v-if="skuValue">{{ skuValue }}</span>
          <span v-if="siteValue"> · {{ siteValue }}</span>
          <span v-if="locationValue"> · {{ locationValue }}</span>
          <span v-if="!skuValue && !siteValue && !locationValue">等待库存范围</span>
        </p>
      </div>
      <div class="flex flex-wrap gap-2">
        <RouterLink
          v-if="hasInventoryScope"
          class="inline-flex h-8 items-center rounded-md border px-2 text-sm text-primary underline-offset-4 hover:underline"
          :to="{ path: '/inventory/availability', query: inventoryQuery }"
        >
          可用量
        </RouterLink>
        <RouterLink
          v-if="hasInventoryScope"
          class="inline-flex h-8 items-center rounded-md border px-2 text-sm text-primary underline-offset-4 hover:underline"
          :to="{ path: '/inventory/lots', query: inventoryQuery }"
        >
          批次与预留
        </RouterLink>
        <RouterLink
          v-if="hasSourceScope"
          class="inline-flex h-8 items-center rounded-md border px-2 text-sm text-primary underline-offset-4 hover:underline"
          :to="{ path: '/barcode/scans', query: sourceQuery }"
        >
          {{ sourceLabel }}
        </RouterLink>
        <span v-if="!hasInventoryScope" class="inline-flex h-8 items-center rounded-md border px-2 text-sm text-muted-foreground">
          库存链接暂不可用
        </span>
      </div>
    </div>

    <div v-if="hasQuantityFacts" class="grid gap-2 sm:grid-cols-4">
      <div><span class="text-muted-foreground">现存量：</span><span class="tabular-nums">{{ formatQuantity(onHandQuantity) }}</span></div>
      <div><span class="text-muted-foreground">可用量：</span><span class="tabular-nums">{{ formatQuantity(availableQuantity) }}</span></div>
      <div><span class="text-muted-foreground">预留量：</span><span class="tabular-nums">{{ formatQuantity(reservedQuantity) }}</span></div>
      <div><span class="text-muted-foreground">冻结/其他：</span><span class="tabular-nums">{{ formatQuantity(frozenQuantity) }}</span></div>
    </div>
    <p v-else class="text-muted-foreground">{{ gapMessage }}</p>

    <div v-if="visibleLines.length" class="grid gap-1">
      <div v-for="line in visibleLines" :key="`${line.locationCode ?? 'loc'}-${line.lotNo ?? 'lot'}-${line.serialNo ?? 'serial'}`" class="flex flex-wrap gap-x-3 gap-y-1 text-muted-foreground">
        <span>库位 {{ line.locationCode ?? '无' }}</span>
        <span>批次 {{ line.lotNo ?? '无批次' }}</span>
        <span>序列号 {{ line.serialNo ?? '无序列号' }}</span>
        <span>预留 {{ formatQuantity(line.reservedQuantity) }}</span>
        <span>冻结/其他 {{ formatQuantity(lineFrozen(line)) }}</span>
      </div>
    </div>
  </section>
</template>
