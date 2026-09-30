<script setup lang="ts">
import type { BusinessConsoleSchedulePlan } from '@nerv-iip/api-client'
import { NvButton, NvInput } from '@nerv-iip/ui'
import { computed, ref, watch } from 'vue'
import { formatDate, today } from '@/utils/format'
import SchedulingDaySheet from './SchedulingDaySheet.vue'

const props = defineProps<{ plan: BusinessConsoleSchedulePlan }>()
const day = ref(today())
const workCenter = ref('all')
watch(
  () => props.plan.planId,
  () => {
    const firstStart = props.plan.assignments?.[0]?.startUtc
    day.value = firstStart ? formatDate(firstStart) : today()
    workCenter.value = 'all'
  },
  { immediate: true },
)
const workCenters = computed(() =>
  [
    ...new Set(
      (props.plan.assignments ?? [])
        .map((item) => item.workCenterId)
        .filter((id): id is string => Boolean(id)),
    ),
  ].sort(),
)
const assignments = computed(() => {
  const start = new Date(`${day.value}T00:00:00`)
  const end = new Date(start)
  end.setDate(end.getDate() + 1)
  return (props.plan.assignments ?? [])
    .filter(
      (item) =>
        (workCenter.value === 'all' || item.workCenterId === workCenter.value) &&
        new Date(item.startUtc!).getTime() < end.getTime() &&
        new Date(item.endUtc!).getTime() > start.getTime(),
    )
    .sort((a, b) => new Date(a.startUtc!).getTime() - new Date(b.startUtc!).getTime())
})
function print() {
  window.print()
}
</script>

<template>
  <section class="mt-6 grid gap-4 rounded-lg border bg-card p-4" aria-label="车间日排程单">
    <div class="flex flex-wrap items-center gap-3">
      <label for="day-schedule-date" class="text-sm font-medium">排程日期</label>
      <NvInput id="day-schedule-date" v-model="day" type="date" class="w-44" />
      <label for="day-schedule-center" class="text-sm font-medium">工作中心</label>
      <select
        id="day-schedule-center"
        v-model="workCenter"
        class="h-9 rounded-md border bg-background px-3 text-sm"
      >
        <option value="all">全部工作中心</option>
        <option v-for="center in workCenters" :key="center" :value="center">{{ center }}</option>
      </select>
      <NvButton
        type="button"
        variant="outline"
        :disabled="!day || !assignments.length"
        data-testid="print-day-schedule"
        @click="print"
        >打印日排程单</NvButton
      >
    </div>
    <div class="overflow-x-auto">
      <SchedulingDaySheet
        :plan="plan"
        :day="day"
        :work-center="workCenter"
        :assignments="assignments"
      />
    </div>
    <Teleport to="body">
      <div class="scheduling-day-print">
        <SchedulingDaySheet
          :plan="plan"
          :day="day"
          :work-center="workCenter"
          :assignments="assignments"
        />
      </div>
    </Teleport>
  </section>
</template>

<style scoped>
.scheduling-day-print {
  display: none;
}
@media print {
  :global(body > :not(.scheduling-day-print)) {
    display: none !important;
  }
  .scheduling-day-print {
    display: block;
  }
  @page {
    size: landscape;
    margin: 12mm;
  }
}
</style>
