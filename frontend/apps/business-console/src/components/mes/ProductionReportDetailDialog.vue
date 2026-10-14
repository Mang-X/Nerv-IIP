<script setup lang="ts">
import { useMesProductionReportDetail } from '@/composables/useBusinessMes'
import { inlineErrorMessage } from '@/utils/notify'
import { formatDateTime } from '@/utils/format'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvSpinner,
} from '@nerv-iip/ui'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { RouterLink } from 'vue-router'
import { computed } from 'vue'

const props = defineProps<{ sourceNodeType?: string; sourceNodeId?: string; returnTo?: string }>()
const auth = useAuthStore()
const canRead = computed(() => (auth.principal?.permissionCodes ?? []).includes(P.mesReportingRead))
const reportNo = defineModel<string | null>('reportNo', { default: null })
const { detail, detailError, detailPending, refresh } = useMesProductionReportDetail(() =>
  canRead.value ? reportNo.value : null,
)
const open = computed({
  get: () => !!reportNo.value,
  set: (value) => {
    if (!value) reportNo.value = null
  },
})
const report = computed(() => detail.value?.report)
const serialNumbers = computed(() =>
  report.value?.serialNumbers?.length
    ? report.value.serialNumbers
    : report.value?.serialNo
      ? [report.value.serialNo]
      : [],
)
const sourceMatches = computed(() => {
  if (!props.sourceNodeType && !props.sourceNodeId) return true
  if (props.sourceNodeType === 'ProductionReport')
    return props.sourceNodeId === report.value?.reportNo
  if (props.sourceNodeType === 'ProducedLot')
    return props.sourceNodeId === report.value?.producedLotNo
  if (props.sourceNodeType === 'Serial')
    return serialNumbers.value.includes(props.sourceNodeId ?? '')
  return false
})
const outputSerials = computed(() =>
  props.sourceNodeType === 'Serial' ? [props.sourceNodeId] : serialNumbers.value,
)
const errorMessage = computed(() => inlineErrorMessage(detailError.value))
const fields = computed(() => [
  { label: '工单', value: report.value?.workOrderNo || '—' },
  { label: '工序任务', value: report.value?.operationTaskNo || '—' },
  { label: '操作人', value: report.value?.reportedByDisplayName?.trim() || '姓名不可用' },
  {
    label: '报工时间',
    value: formatDateTime(report.value?.reportedAtUtc),
  },
  { label: '良品', value: report.value?.goodQuantity ?? 0 },
  { label: '报废', value: report.value?.scrapQuantity ?? 0 },
  { label: '返工', value: report.value?.reworkQuantity ?? 0 },
])
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>报工详情 · {{ reportNo }}</NvDialogTitle>
        <NvDialogDescription>查看本条报工的操作人与生产记录。</NvDialogDescription>
      </NvDialogHeader>
      <p v-if="!canRead">无报工读取权限，无法查看报工详情。</p>
      <div v-else-if="detailError" class="space-y-2">
        <p>{{ errorMessage }}</p>
        <NvButton type="button" variant="outline" @click="refresh">重试</NvButton>
      </div>
      <div v-else-if="detailPending || !report" class="flex items-center gap-2">
        <NvSpinner class="size-4" /> 加载报工详情…
      </div>
      <p v-else-if="!sourceMatches">来源节点与报工产出事实不匹配，无法定位该产出。</p>
      <div v-else class="space-y-6">
        <dl class="grid grid-cols-2 gap-4 text-sm">
          <div v-for="field in fields" :key="field.label">
            <dt class="text-muted-foreground">{{ field.label }}</dt>
            <dd class="mt-1 font-medium">{{ field.value }}</dd>
          </div>
        </dl>
        <section
          v-if="report.producedLotNo || serialNumbers.length"
          class="space-y-3"
          aria-label="产出事实"
        >
          <h3 class="text-base font-semibold">
            {{
              sourceNodeType === 'ProducedLot' || sourceNodeType === 'Serial'
                ? '所选产出事实'
                : '产出事实'
            }}
          </h3>
          <dl class="grid grid-cols-2 gap-4 text-sm">
            <div v-if="report.producedLotNo">
              <dt class="text-muted-foreground">产出批次</dt>
              <dd class="mt-1 font-medium">{{ report.producedLotNo }}</dd>
            </div>
            <div v-if="outputSerials.length">
              <dt class="text-muted-foreground">序列号</dt>
              <dd v-for="serial in outputSerials" :key="serial" class="mt-1 font-medium">
                {{ serial }}
              </dd>
            </div>
          </dl>
        </section>
      </div>
      <NvDialogFooter>
        <NvButton v-if="returnTo" type="button" variant="outline" as-child
          ><RouterLink :to="returnTo">返回追溯</RouterLink></NvButton
        >
        <NvButton type="button" variant="outline" @click="reportNo = null">关闭</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
