<script setup lang="ts">
import { Input, NvButton, NvSearchSelect, type SearchSelectOption } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    locationCode?: string
    lotNo?: string
    locationOptions: SearchSelectOption[]
    lotOptions?: SearchSelectOption[]
    pending?: boolean
    ready?: boolean
    error?: unknown
    showLot?: boolean
    sourceLabel: string
    searchKeyword?: string
    asOfUtc?: string
    freshnessUtc?: string | null
    truncated?: boolean
  }>(),
  {
    locationCode: undefined,
    lotNo: undefined,
    lotOptions: () => [],
    pending: false,
    ready: false,
    error: undefined,
    showLot: true,
    searchKeyword: '',
    asOfUtc: undefined,
    freshnessUtc: undefined,
    truncated: false,
  },
)

const emit = defineEmits<{
  'update:locationCode': [value: string | undefined]
  'update:lotNo': [value: string | undefined]
  'update:searchKeyword': [value: string]
  retry: []
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
const searchModel = computed({
  get: () => props.searchKeyword,
  set: (value: string) => emit('update:searchKeyword', value),
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
  if (Number.isNaN(parsed.getTime())) return '时间未知'
  const age = Date.now() - parsed.getTime()
  if (age >= 0 && age < 60_000) return '刚刚'
  if (age >= 0 && age < 3_600_000) return `${Math.max(1, Math.floor(age / 60_000))} 分钟前`
  return parsed.toLocaleString('zh-CN', { hour12: false })
}
</script>

<template>
  <Input
    v-if="ready && !error"
    v-model="searchModel"
    type="search"
    class="w-52"
    placeholder="远端搜索当前范围候选"
    aria-label="远端搜索仓储作业候选"
  />
  <NvSearchSelect
    v-model="locationModel"
    class="w-40"
    :options="locations"
    :loading="pending"
    :disabled="!ready || Boolean(error)"
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
    :disabled="!ready || Boolean(error)"
    placeholder="全部批次"
    search-placeholder="搜索批次、物料或库位"
    empty-text="当前范围仓储作业记录中没有匹配批次"
    aria-label="批次候选"
  />
  <p v-if="!ready" class="max-w-80 text-xs leading-5 text-muted-foreground">
    请先选择可用作业范围，范围就绪前不会请求或应用候选。
  </p>
  <div v-else-if="error" class="flex max-w-80 items-center gap-2 text-xs text-destructive">
    <span>候选加载失败，请重试；当前不会把失败伪装为空候选。</span>
    <NvButton data-testid="candidate-retry" size="sm" variant="outline" @click="emit('retry')">
      重试
    </NvButton>
  </div>
  <p v-else class="max-w-80 text-xs leading-5 text-muted-foreground" data-testid="candidate-source">
    {{ sourceLabel }} · 截至 {{ formatTime(asOfUtc) }} · 最近记录
    {{ formatTime(freshnessUtc) }}
    <span v-if="truncated"> · 候选已截断，可继续搜索收窄</span>
  </p>
</template>
