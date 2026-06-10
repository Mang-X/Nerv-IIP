<script setup lang="ts">
import {
  Button,
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@nerv-iip/ui'
import { SchedulingWorkbench } from '@nerv-iip/scheduling'
import { CalendarClockIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useScheduling } from '@/composables/useScheduling'
import BusinessLayout from '@/layouts/BusinessLayout.vue'

definePage({ meta: { requiresAuth: true, title: '排产工作台' } })

const { plans, planId, model, loading, release } = useScheduling()

const planOptions = computed(() =>
  plans.value.map((p) => ({
    value: p.planId ?? '',
    label: `${statusLabel(p.status)} · ${formatDate(p.generatedAtUtc)}`,
  })),
)
const planModel = computed({
  get: () => planId.value ?? '',
  set: (v: string) => (planId.value = v || undefined),
})

const operationCount = computed(
  () => model.value?.tasks.filter((t) => t.type === 'operation').length ?? 0,
)
const conflictCount = computed(() => model.value?.conflicts.length ?? 0)
const unscheduledCount = computed(() => model.value?.unscheduled.length ?? 0)
const avgUtilization = computed(() => {
  const loads = model.value?.loads ?? []
  if (!loads.length) return '—'
  const avg = loads.reduce((s, l) => s + l.utilization, 0) / loads.length
  return `${Math.round(avg * 100)}%`
})
const planStatus = computed(() => statusLabel(model.value?.meta.status))

function statusLabel(value?: string | null) {
  if (value === 'preview') return '预览'
  if (value === 'generated') return '已生成'
  if (value === 'released') return '已发布'
  return '暂无'
}
function formatDate(value?: string | null) {
  if (!value) return '—'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? value : d.toLocaleString('zh-CN', { hour12: false })
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="排产工作台"
      :breadcrumbs="[{ label: '需求与计划' }]"
      :count="`${operationCount} 道工序`"
    >
      <template #actions>
        <Select v-if="planOptions.length" v-model="planModel">
          <SelectTrigger class="h-9 w-56" aria-label="选择排程计划"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="o in planOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="计划状态" :value="planStatus" hint="当前排程计划的状态" />
      <SectionCard description="工序数" :value="operationCount" hint="本计划安排的工序总数" />
      <SectionCard description="冲突数" :value="conflictCount" hint="产能/交期/物料等冲突" />
      <SectionCard description="未排产" :value="unscheduledCount" :hint="`平均资源利用率 ${avgUtilization}`" />
    </SectionCards>

    <Empty v-if="!planId" class="py-16">
      <EmptyHeader>
        <EmptyMedia>
          <CalendarClockIcon class="size-12 text-muted-foreground" aria-hidden="true" />
        </EmptyMedia>
        <EmptyTitle>暂无排程计划</EmptyTitle>
        <EmptyDescription>
          请先在需求与计划中生成排程方案,或在制造执行的规则排程中触发一次排程。
        </EmptyDescription>
      </EmptyHeader>
      <Button as-child variant="outline">
        <RouterLink to="/mes/schedules">前往规则排程</RouterLink>
      </Button>
    </Empty>

    <div v-else class="h-[calc(100vh-18rem)] min-h-[480px]">
      <SchedulingWorkbench
        :model="model"
        :loading="loading"
        :release="release"
        engine-kind="auto"
      />
    </div>
  </BusinessLayout>
</template>
