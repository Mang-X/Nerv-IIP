<script setup lang="ts">
import { NvSearchSelect, type SearchSelectOption } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    locationCode?: string
    lotNo?: string
    locationOptions: SearchSelectOption[]
    lotOptions?: SearchSelectOption[]
    pending?: boolean
    showLot?: boolean
    sourceLabel: string
    sourceKind?: string | null
    asOfUtc?: string
    freshnessUtc?: string | null
    truncated?: boolean
  }>(),
  {
    locationCode: undefined,
    lotNo: undefined,
    lotOptions: () => [],
    pending: false,
    showLot: true,
    sourceKind: undefined,
    asOfUtc: undefined,
    freshnessUtc: undefined,
    truncated: false,
  },
)

const emit = defineEmits<{
  'update:locationCode': [value: string | undefined]
  'update:lotNo': [value: string | undefined]
}>()

const locationModel = computed({
  get: () => props.locationCode ?? '',
  set: (value: string) => {
    const normalized = value || undefined
    emit('update:locationCode', normalized)
    if (normalized !== props.locationCode) emit('update:lotNo', undefined)
  },
})
const lotModel = computed({
  get: () => props.lotNo ?? '',
  set: (value: string) => emit('update:lotNo', value || undefined),
})
const locations = computed<SearchSelectOption[]>(() => [
  { value: '', label: '全部库位' },
  ...props.locationOptions,
])
const lots = computed<SearchSelectOption[]>(() => [
  { value: '', label: props.locationCode ? '该库位全部批次' : '全部批次' },
  ...props.lotOptions,
])

function formatTime(value?: string | null) {
  if (!value) return '尚无时间'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('zh-CN', { hour12: false })
}
</script>

<template>
  <NvSearchSelect
    v-model="locationModel"
    class="w-40"
    :options="locations"
    :loading="pending"
    placeholder="全部库位"
    search-placeholder="搜索库位或物料"
    empty-text="当前范围仓储作业记录中没有匹配库位"
    aria-label="库位候选"
  />
  <NvSearchSelect
    v-if="showLot"
    v-model="lotModel"
    class="w-40"
    :options="lots"
    :loading="pending"
    placeholder="全部批次"
    search-placeholder="搜索批次、物料或库位"
    empty-text="当前范围仓储作业记录中没有匹配批次"
    aria-label="批次候选"
  />
  <p class="max-w-80 text-xs leading-5 text-muted-foreground" data-testid="candidate-source">
    {{ sourceLabel }}<span v-if="sourceKind"> · {{ sourceKind }}</span> · 截至
    {{ formatTime(asOfUtc) }} · 最近记录
    {{ formatTime(freshnessUtc) }}
    <span v-if="truncated"> · 候选已截断，可继续搜索收窄</span>
  </p>
</template>
