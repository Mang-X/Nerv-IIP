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
    showLot?: boolean
    sourceLabel: string
    sourceKind?: string
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
    showLot: true,
    sourceKind: undefined,
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
}>()

const locationPickerOpen = ref(false)
const lotPickerOpen = ref(false)
const scanTarget = ref<'location' | 'lot'>('location')
const searchKeyword = ref('')
const scanNotice = ref('')
const scanOverride = ref<{ target: 'location' | 'lot'; value: string }>()

const normalizedKeyword = computed(() => searchKeyword.value.trim().toLocaleLowerCase())
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
const currentOptions = computed(() =>
  scanTarget.value === 'location' ? filteredLocationOptions.value : filteredLotOptions.value,
)

const locationModel = computed({
  get: () => props.locationCode,
  set: (value: string | undefined) => {
    emit('update:locationCode', value)
    if (value !== props.locationCode) emit('update:lotNo', undefined)
  },
})
const lotModel = computed({
  get: () => props.lotNo,
  set: (value: string | undefined) => emit('update:lotNo', value),
})

function chooseTarget(target: 'location' | 'lot') {
  scanTarget.value = target
  searchKeyword.value = ''
  scanNotice.value = ''
}

function openLocationPicker() {
  chooseTarget('location')
  locationPickerOpen.value = true
}

function openLotPicker() {
  chooseTarget('lot')
  lotPickerOpen.value = true
}

function acceptScan(value: string) {
  const normalized = value.trim()
  if (!normalized) return
  const matched = currentOptions.value.find((option) => option.value === normalized)
  if (!matched) {
    scanOverride.value = { target: scanTarget.value, value: normalized }
    scanNotice.value = `${
      scanTarget.value === 'location' ? '库位' : '批次'
    } ${normalized} 已作为扫码筛选值应用；未在当前候选中，候选可能因范围或截断不完整，未验证为主数据。`
    if (scanTarget.value === 'location') locationModel.value = normalized
    else lotModel.value = normalized
    return
  }
  scanOverride.value = undefined
  scanNotice.value = ''
  if (scanTarget.value === 'location') locationModel.value = matched.value
  else lotModel.value = matched.value
}

function clearScanOverride() {
  const override = scanOverride.value
  if (!override) return
  if (override.target === 'location') locationModel.value = undefined
  else lotModel.value = undefined
  scanOverride.value = undefined
  scanNotice.value = ''
}

watch(
  () => props.showLot,
  (showLot) => {
    if (!showLot && scanTarget.value === 'lot') chooseTarget('location')
  },
)
watch([() => props.locationCode, () => props.lotNo], ([locationCode, lotNo]) => {
  const override = scanOverride.value
  if (!override) return
  const currentValue = override.target === 'location' ? locationCode : lotNo
  if (currentValue !== override.value) {
    scanOverride.value = undefined
    scanNotice.value = ''
  }
})
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
      v-model="searchKeyword"
      :placeholder="scanTarget === 'location' ? '搜索库位候选' : '按批次、SKU 或库位搜索'"
    />

    <NvCellGroup>
      <NvCell
        title="库位"
        :value="locationCode || '从当前范围候选选择'"
        :note="
          filteredLocationOptions.length
            ? `${filteredLocationOptions.length} 个候选`
            : '当前范围仓储作业记录中暂无库位候选'
        "
        arrow
        @click="openLocationPicker"
      />
      <NvCell
        v-if="showLot"
        title="批次"
        :value="lotNo || '从当前范围候选选择'"
        :note="
          locationCode
            ? filteredLotOptions.length
              ? `${filteredLotOptions.length} 个候选，已按库位收窄`
              : '当前库位暂无批次候选'
            : '选择库位后可进一步收窄批次'
        "
        arrow
        @click="openLotPicker"
      />
    </NvCellGroup>

    <NvScanBar
      v-if="showScanner"
      :active="active && !locationPickerOpen && !lotPickerOpen"
      :placeholder="scanTarget === 'location' ? '扫描当前范围库位候选' : '扫描当前范围批次候选'"
      @scan="acceptScan"
    />
    <div v-if="scanNotice" class="space-y-1 px-1">
      <p class="text-sm text-warning">{{ scanNotice }}</p>
      <NvMobileButton size="sm" variant="text" @click="clearScanOverride">
        清除扫码筛选
      </NvMobileButton>
    </div>

    <div class="space-y-0.5 px-1 text-xs text-muted-foreground">
      <p>
        {{ sourceLabel }}<span v-if="sourceKind"> · {{ sourceKind }}</span>
      </p>
      <p v-if="pending">候选加载中…</p>
      <p v-else-if="asOfUtc || freshnessUtc">
        截至 {{ asOfUtc || '—' }}<span v-if="freshnessUtc"> · 数据新鲜度 {{ freshnessUtc }}</span>
      </p>
      <p v-if="truncated" class="text-warning">候选已截断，请搜索进一步收窄。</p>
    </div>

    <NvPicker
      v-model:open="locationPickerOpen"
      v-model="locationModel"
      title="选择当前范围库位"
      :options="filteredLocationOptions"
    />
    <NvPicker
      v-if="showLot"
      v-model:open="lotPickerOpen"
      v-model="lotModel"
      title="选择当前范围批次"
      :options="filteredLotOptions"
    />
  </section>
</template>
