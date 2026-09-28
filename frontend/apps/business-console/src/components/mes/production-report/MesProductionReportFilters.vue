<script setup lang="ts">
import type { BusinessConsoleMesProductionStatisticsDimension } from '@nerv-iip/api-client'
import type { DateRange } from '@nerv-iip/ui'
import {
  NvDatePicker,
  NvDateRangePicker,
  NvField,
  NvFieldLabel,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvToolbar,
} from '@nerv-iip/ui'
import { computed } from 'vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
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
      <!-- 六个筛选器此前写的是 `min-w-*`，于是各占一整行、吃掉整个首屏，数据被顶到折叠线
           以下（#3735）。根因不在 min-w：`NvField` 基础类自带 `w-full`，每个字段都声明
           「占满一行」，flex-wrap 里的每一行都只放得下一个。`min-w-*` 加得再多也压不下去——
           1440px 下实测顶沿 145/216/288/359/430/501，六行。
           改法是本 app 通行的 `w-full sm:w-*`：窄屏仍占满（不挤成一行），到 sm 断点收成
           固定宽度才能横排，与 NvFilterBar 的搜索框 / 下拉框同一套写法。 -->
      <NvField class="w-full sm:w-40">
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
      <NvField class="w-full sm:w-64">
        <NvFieldLabel>统计时段</NvFieldLabel>
        <NvDateRangePicker
          v-model="windowRange"
          placeholder="选择统计时段"
          class="w-full sm:w-64"
        />
      </NvField>
      <!-- 业务日比同排的 36/44 宽一档：触发器上要给清除叉让位（`pr-12`），
           `YYYY-MM-DD` 加日历图标加叉在 144px 里放不下（实测 2026-09-15 只剩
           56px 可见、需要 91px，被 `truncate` 截断还压着叉）。
           壳与控件必须**同一个**断点值：控件宽度只由它自己的 `class` 给（组件
           不再自带任何 `w-*`），壳给宽而控件给窄，两处不等就是这条。 -->
      <NvField class="w-full sm:w-48">
        <NvFieldLabel>业务日</NvFieldLabel>
        <NvDatePicker
          :model-value="filters.businessDate || null"
          placeholder="选择业务日"
          aria-label="业务日"
          clearable
          class="w-full sm:w-48"
          @update:model-value="emit('update', { businessDate: $event })"
        />
      </NvField>
      <NvField class="w-full sm:w-36">
        <NvFieldLabel>班次</NvFieldLabel>
        <DirectoryPicker
          directory-type="shift"
          :model-value="filters.shiftCode"
          placeholder="全部班次"
          clearable
          @update:model-value="emit('update', { shiftCode: $event })"
        />
      </NvField>
      <NvField class="w-full sm:w-44">
        <NvFieldLabel>工作中心</NvFieldLabel>
        <DirectoryPicker
          directory-type="work-center"
          :model-value="filters.workCenterId"
          placeholder="全部工作中心"
          clearable
          @update:model-value="emit('update', { workCenterId: $event })"
        />
      </NvField>
      <NvField class="w-full sm:w-44">
        <NvFieldLabel>物料</NvFieldLabel>
        <DirectoryPicker
          directory-type="material"
          :model-value="filters.skuId"
          placeholder="全部物料"
          clearable
          @update:model-value="emit('update', { skuId: $event })"
        />
      </NvField>
    </template>
  </NvToolbar>
</template>
