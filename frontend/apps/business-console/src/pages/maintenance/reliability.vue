<script setup lang="ts">
import { useMaintenanceReliability } from '@/composables/useBusinessMaintenance'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  FieldPro,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
} from '@nerv-iip/ui'
import { ActivityIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '可靠性指标', requiredPermissions: ['business.maintenance.work-orders.read'] } })

const route = useRoute()
const initialDeviceAssetId = typeof route.query.deviceAssetId === 'string' ? route.query.deviceAssetId : ''
const { filters, reliability, reliabilityError, reliabilityPending, refreshReliability } = useMaintenanceReliability({
  deviceAssetId: initialDeviceAssetId,
})

const errorMessage = computed(() => formatError(reliabilityError.value))
const hasDeviceScope = computed(() => filters.deviceAssetId.trim().length > 0)

const windowStartLocal = computed({
  get: () => toLocalDateTime(filters.windowStartUtc),
  set: (value: string) => {
    filters.windowStartUtc = toIsoDateTime(value)
  },
})
const windowEndLocal = computed({
  get: () => toLocalDateTime(filters.windowEndUtc),
  set: (value: string) => {
    filters.windowEndUtc = toIsoDateTime(value)
  },
})

function metricLabel(value?: number | null, suffix = '') {
  if (value === null || value === undefined) return '无样本'
  return `${Number(value).toFixed(1)}${suffix}`
}
function toLocalDateTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset())
  return date.toISOString().slice(0, 16)
}
function toIsoDateTime(value: string) {
  const date = value ? new Date(value) : new Date()
  return Number.isNaN(date.getTime()) ? new Date().toISOString() : date.toISOString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="可靠性指标" :breadcrumbs="[{ label: '设备监控' }]" :count="hasDeviceScope ? filters.deviceAssetId : '选择设备后查询'">
      <template #actions>
        <ButtonPro v-if="hasDeviceScope" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/equipment/${filters.deviceAssetId}`">
            <ActivityIcon aria-hidden="true" />
            设备详情
          </RouterLink>
        </ButtonPro>
        <ButtonPro v-else size="sm" type="button" variant="outline" :disabled="true">
          <ActivityIcon aria-hidden="true" />
          设备详情
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="!hasDeviceScope || reliabilityPending" @click="refreshReliability">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <FieldProGroup class="grid gap-3 rounded-lg border bg-card p-4 md:grid-cols-[minmax(220px,1fr)_220px_220px]">
      <FieldPro>
        <FieldProLabel for="rel-device">设备</FieldProLabel>
        <InputPro id="rel-device" v-model="filters.deviceAssetId" autocomplete="off" placeholder="输入设备编号后查询，如 DEV-PRESS-01" />
      </FieldPro>
      <FieldPro>
        <FieldProLabel for="rel-start">窗口开始</FieldProLabel>
        <InputPro id="rel-start" v-model="windowStartLocal" type="datetime-local" />
      </FieldPro>
      <FieldPro>
        <FieldProLabel for="rel-end">窗口结束</FieldProLabel>
        <InputPro id="rel-end" v-model="windowEndLocal" type="datetime-local" />
      </FieldPro>
    </FieldProGroup>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div v-if="!hasDeviceScope" class="rounded-lg border border-dashed p-6 text-sm text-muted-foreground">
      请选择设备后查看 MTBF、MTTR、故障次数和修复次数。
    </div>

    <SectionCards v-else :columns="4">
      <SectionCard description="MTBF" :value="metricLabel(reliability?.mtbfHours, ' 小时')" :hint="reliability?.mtbfRuntimeHasSamples ? '按运行样本计算' : '当前窗口无运行样本'" />
      <SectionCard description="MTTR" :value="metricLabel(reliability?.mttrMinutes, ' 分钟')" hint="维修完成样本均值" />
      <SectionCard description="故障次数" :value="reliability?.failureCount ?? 0" hint="窗口内维护故障" />
      <SectionCard description="修复次数" :value="reliability?.repairCount ?? 0" hint="窗口内完成维修" />
    </SectionCards>
  </BusinessLayout>
</template>
