<script setup lang="ts">
import type { BusinessConsoleMesProductionStatisticsDimension } from '@nerv-iip/api-client'
import type { DateRange } from '@nerv-iip/ui'
import {
  NvDateRangePicker,
  NvField,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvToolbar,
} from '@nerv-iip/ui'
import { computed } from 'vue'
import type { MesProductionStatisticsFilters } from '@/composables/useMesProductionStatistics'

const props = defineProps<{ filters: MesProductionStatisticsFilters }>()
const emit = defineEmits<{
  update: [patch: Partial<MesProductionStatisticsFilters>]
}>()

const dimensions: Array<{
  value: BusinessConsoleMesProductionStatisticsDimension
  label: string
}> = [
  { value: 'day', label: '按业务日' },
  { value: 'shift', label: '按班次' },
  { value: 'workCenter', label: '按工作中心' },
  { value: 'sku', label: '按物料' },
]

const dimension = computed({
  get: () => props.filters.dimension,
  set: (value: BusinessConsoleMesProductionStatisticsDimension) =>
    emit('update', { dimension: value }),
})
const windowRange = computed<DateRange>({
  get: () => ({
    start: toDateInput(props.filters.windowStartUtc),
    end: toDateInput(props.filters.windowEndUtc, -1),
  }),
  set: (range) => {
    const patch: Partial<MesProductionStatisticsFilters> = {}
    if (range.start) patch.windowStartUtc = fromDateInput(range.start, 0)
    if (range.end) patch.windowEndUtc = fromDateInput(range.end, 1)
    emit('update', patch)
  },
})

function toDateInput(value: string, dayOffset = 0) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  if (dayOffset) date.setDate(date.getDate() + dayOffset)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 10)
}

function fromDateInput(value: string, dayOffset: number) {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(year!, month! - 1, day! + dayOffset).toISOString()
}
</script>

<template>
  <NvToolbar :show-search="false">
    <template #filters>
      <NvField class="min-w-40">
        <NvFieldLabel>统计维度</NvFieldLabel>
        <NvSelect v-model="dimension">
          <NvSelectTrigger aria-label="统计维度"><NvSelectValue /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="item in dimensions" :key="item.value" :value="item.value">
              {{ item.label }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </NvField>
      <NvField class="min-w-64">
        <NvFieldLabel>统计窗口</NvFieldLabel>
        <NvDateRangePicker v-model="windowRange" placeholder="选择统计窗口" />
      </NvField>
      <NvField class="min-w-36">
        <NvFieldLabel>业务日</NvFieldLabel>
        <NvInput
          type="date"
          :model-value="filters.businessDate"
          @update:model-value="emit('update', { businessDate: String($event) })"
        />
      </NvField>
      <NvField class="min-w-36">
        <NvFieldLabel>班次编码</NvFieldLabel>
        <NvInput
          :model-value="filters.shiftCode"
          placeholder="全部班次"
          @update:model-value="emit('update', { shiftCode: String($event) })"
        />
      </NvField>
      <NvField class="min-w-44">
        <NvFieldLabel>工作中心 ID</NvFieldLabel>
        <NvInput
          :model-value="filters.workCenterId"
          placeholder="全部工作中心"
          @update:model-value="emit('update', { workCenterId: String($event) })"
        />
      </NvField>
      <NvField class="min-w-44">
        <NvFieldLabel>物料 ID</NvFieldLabel>
        <NvInput
          :model-value="filters.skuId"
          placeholder="全部物料"
          @update:model-value="emit('update', { skuId: String($event) })"
        />
      </NvField>
    </template>
  </NvToolbar>
</template>
