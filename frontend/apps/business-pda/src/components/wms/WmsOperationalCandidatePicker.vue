<script setup lang="ts">
import {
  NvCell,
  NvCellGroup,
  NvMobileButton,
  NvPicker,
  NvScanBar,
  NvSearchBar,
  type PickerOption,
} from '@nerv-iip/ui-mobile'
import { computed, ref, watch } from 'vue'

interface CandidateOption extends PickerOption {
  hint?: string
}

const props = withDefaults(
  defineProps<{
    locationCode?: string
    lotNo?: string
    locationOptions: CandidateOption[]
    lotOptions: CandidateOption[]
    ready?: boolean
    error?: unknown
    searchKeyword?: string
    scanOverrides?: Readonly<Partial<Record<'location' | 'lot', string>>>
    showLot?: boolean
    sourceLabel: string
    asOfUtc?: string
    freshnessUtc?: string
    truncated?: boolean
    pending?: boolean
    active?: boolean
    showScanner?: boolean
  }>(),
  {
    locationCode: undefined,
    lotNo: undefined,
    ready: false,
    error: undefined,
    searchKeyword: '',
    scanOverrides: () => ({}),
    showLot: true,
    asOfUtc: undefined,
    freshnessUtc: undefined,
    truncated: false,
    pending: false,
    active: true,
    showScanner: true,
  },
)

const emit = defineEmits<{
  'update:locationCode': [value: string | undefined]
  'update:lotNo': [value: string | undefined]
  'update:searchKeyword': [value: string]
  scanOverrideChange: [target: 'location' | 'lot', value: string | undefined]
  retry: []
}>()

const locationPickerOpen = ref(false)
const lotPickerOpen = ref(false)
const scanTarget = ref<'location' | 'lot'>('location')

const searchModel = computed({
  get: () => props.searchKeyword,
  set: (value: string) => emit('update:searchKeyword', value),
})
const normalizedKeyword = computed(() => props.searchKeyword.trim().toLocaleLowerCase())
function filtered(options: CandidateOption[]) {
  if (!normalizedKeyword.value) return options
  return options.filter((option) =>
    [option.label, option.value, option.hint]
      .filter(Boolean)
      .some((text) => text!.toLocaleLowerCase().includes(normalizedKeyword.value)),
  )
}
const filteredLocationOptions = computed(() => filtered(props.locationOptions))
const filteredLotOptions = computed(() => filtered(props.lotOptions))
const pickerLocationOptions = computed<PickerOption[]>(() => [
  { value: '', label: '全部库位' },
  ...filteredLocationOptions.value,
])
const pickerLotOptions = computed<PickerOption[]>(() => [
  { value: '', label: props.locationCode ? '该库位全部批次' : '全部批次' },
  ...filteredLotOptions.value,
])
const currentOptions = computed(() =>
  scanTarget.value === 'location' ? filteredLocationOptions.value : filteredLotOptions.value,
)

const locationModel = computed({
  get: () => props.locationCode,
  set: (value: string | undefined) => {
    const normalized = value || undefined
    emit('scanOverrideChange', 'location', undefined)
    emit('update:locationCode', normalized)
    if (normalized !== props.locationCode) {
      emit('scanOverrideChange', 'lot', undefined)
      emit('update:lotNo', undefined)
    }
  },
})
const lotModel = computed({
  get: () => props.lotNo,
  set: (value: string | undefined) => {
    emit('scanOverrideChange', 'lot', undefined)
    emit('update:lotNo', value || undefined)
  },
})

function chooseTarget(target: 'location' | 'lot') {
  scanTarget.value = target
  emit('update:searchKeyword', '')
}

function openLocationPicker() {
  if (!props.ready || props.error) return
  chooseTarget('location')
  locationPickerOpen.value = true
}

function openLotPicker() {
  if (!props.ready || props.error) return
  chooseTarget('lot')
  lotPickerOpen.value = true
}

function acceptScan(value: string) {
  if (!props.ready || props.error) return
  const normalized = value.trim()
  if (!normalized) return
  const matched = currentOptions.value.find((option) => option.value === normalized)
  if (!matched) {
    emit('scanOverrideChange', scanTarget.value, normalized)
    if (scanTarget.value === 'location') {
      emit('scanOverrideChange', 'lot', undefined)
      emit('update:locationCode', normalized)
      emit('update:lotNo', undefined)
    } else {
      emit('update:lotNo', normalized)
    }
    return
  }
  if (scanTarget.value === 'location') locationModel.value = matched.value
  else lotModel.value = matched.value
}

function clearScanOverride(target: 'location' | 'lot') {
  if (!props.scanOverrides[target]) return
  emit('scanOverrideChange', target, undefined)
  if (target === 'location') {
    emit('scanOverrideChange', 'lot', undefined)
    emit('update:locationCode', undefined)
    emit('update:lotNo', undefined)
  } else {
    emit('update:lotNo', undefined)
  }
}

function formatTime(value?: string) {
  if (!value) return '尚无时间'
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return '时间未知'
  const age = Date.now() - parsed.getTime()
  if (age >= 0 && age < 60_000) return '刚刚'
  if (age >= 0 && age < 3_600_000) return `${Math.max(1, Math.floor(age / 60_000))} 分钟前`
  return parsed.toLocaleString('zh-CN', { hour12: false })
}

watch(
  () => props.showLot,
  (showLot) => {
    if (!showLot && scanTarget.value === 'lot') chooseTarget('location')
  },
)
watch(
  [() => props.ready, () => props.error],
  ([ready, error]) => {
    if (ready && !error) return
    locationPickerOpen.value = false
    lotPickerOpen.value = false
  },
  { flush: 'sync' },
)
</script>

<template>
  <section class="space-y-2" aria-label="仓储作业候选">
    <div class="flex gap-2 px-1">
      <NvMobileButton
        size="sm"
        :variant="scanTarget === 'location' ? 'primary' : 'outline'"
        @click="chooseTarget('location')"
      >
        库位候选
      </NvMobileButton>
      <NvMobileButton
        v-if="showLot"
        size="sm"
        :variant="scanTarget === 'lot' ? 'primary' : 'outline'"
        @click="chooseTarget('lot')"
      >
        批次候选
      </NvMobileButton>
    </div>

    <NvSearchBar
      v-if="ready && !error"
      v-model="searchModel"
      :placeholder="scanTarget === 'location' ? '搜索库位候选' : '按批次、SKU 或库位搜索'"
    />

    <NvCellGroup>
      <NvCell
        title="库位"
        :value="locationCode || '从当前范围候选选择'"
        :note="
          !ready
            ? '请先选择可用作业范围'
            : error
              ? '候选加载失败，不能按空候选继续'
              : filteredLocationOptions.length
                ? `${filteredLocationOptions.length} 个候选`
                : '当前范围仓储作业记录中暂无库位候选'
        "
        :arrow="ready && !error"
        @click="openLocationPicker"
      />
      <NvCell
        v-if="showLot"
        title="批次"
        :value="lotNo || '从当前范围候选选择'"
        :note="
          !ready
            ? '请先选择可用作业范围'
            : error
              ? '候选加载失败，不能按空候选继续'
              : locationCode
                ? filteredLotOptions.length
                  ? `${filteredLotOptions.length} 个候选，已按库位收窄`
                  : '当前库位暂无批次候选'
                : '选择库位后可进一步收窄批次'
        "
        :arrow="ready && !error"
        @click="openLotPicker"
      />
    </NvCellGroup>

    <NvScanBar
      v-if="showScanner && ready && !error"
      :active="active && !locationPickerOpen && !lotPickerOpen"
      :placeholder="scanTarget === 'location' ? '扫描当前范围库位候选' : '扫描当前范围批次候选'"
      @scan="acceptScan"
    />
    <template v-for="target in ['location', 'lot'] as const" :key="target">
      <div v-if="props.scanOverrides[target]" class="space-y-1 px-1">
        <p class="text-sm text-warning">
          {{ target === 'location' ? '库位' : '批次' }} {{ props.scanOverrides[target] }}
          已作为扫码筛选值应用；未在当前候选中，候选可能因范围或截断不完整，未验证为主数据。
        </p>
        <NvMobileButton size="sm" variant="text" @click="clearScanOverride(target)">
          清除扫码筛选
        </NvMobileButton>
      </div>
    </template>

    <div class="space-y-0.5 px-1 text-xs text-muted-foreground">
      <p v-if="!ready">请先选择可用作业范围，范围就绪前不会请求或应用候选。</p>
      <div v-else-if="error" class="space-y-1 text-destructive">
        <p>候选加载失败，请重试；当前不会把失败伪装为空候选。</p>
        <NvMobileButton
          data-testid="candidate-retry"
          size="sm"
          variant="outline"
          @click="emit('retry')"
        >
          重试
        </NvMobileButton>
      </div>
      <p v-else>{{ sourceLabel }}</p>
      <p v-if="ready && !error && pending">候选加载中…</p>
      <p v-else-if="ready && !error && (asOfUtc || freshnessUtc)">
        截至 {{ formatTime(asOfUtc)
        }}<span v-if="freshnessUtc"> · 数据新鲜度 {{ formatTime(freshnessUtc) }}</span>
      </p>
      <p v-if="ready && !error && truncated" class="text-warning">候选已截断，请搜索进一步收窄。</p>
    </div>

    <NvPicker
      v-model:open="locationPickerOpen"
      v-model="locationModel"
      title="选择当前范围库位"
      :options="pickerLocationOptions"
    />
    <NvPicker
      v-if="showLot"
      v-model:open="lotPickerOpen"
      v-model="lotModel"
      title="选择当前范围批次"
      :options="pickerLotOptions"
    />
  </section>
</template>
