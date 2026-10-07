<script setup lang="ts">
import {
  listBusinessConsoleSchedulingPlanHistoryQueryOptions,
  type BusinessConsoleSchedulingPlanSummaryResponse,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, ref, watch } from 'vue'
import { NvButton, NvDataTable, Spinner, type NvDataTableColumn } from '@nerv-iip/ui'
import { useSchedulingInsertionPreview } from '@/composables/useSchedulingInsertionPreview'
import { assertEnvelopeSuccess } from '@/composables/serviceEnvelope'
import { notifyOperationFailure } from '@/utils/notify'
import { schedulingPlanStatusLabel } from '@/utils/schedulingPlanPresentation'
import { formatDateTime } from '@/utils/format'
import SchedulingPreviewResult from './SchedulingPreviewResult.vue'
const props = defineProps<{
  workOrderId: string
  context: { organizationId: string; environmentId: string }
  canManage: boolean
}>()
const selectedPlanId = ref('')
const page = ref(0)
const pageSize = 10
const hasScope = computed(() =>
  Boolean(props.context.organizationId && props.context.environmentId),
)
const task = useSchedulingInsertionPreview(ref(true))
const plansQuery = useQuery(() => ({
  ...listBusinessConsoleSchedulingPlanHistoryQueryOptions({
    query: { ...props.context, pageIndex: page.value, pageSize },
  }),
  enabled: hasScope.value,
}))
const plans = computed(() => plansQuery.data.value?.data?.items ?? [])
const total = computed(() => plansQuery.data.value?.data?.total ?? 0)
const columns: NvDataTableColumn<BusinessConsoleSchedulingPlanSummaryResponse>[] = [
  { key: 'planId', header: '方案' },
  { key: 'status', header: '状态', accessor: (row) => schedulingPlanStatusLabel(row.status ?? '') },
  { key: 'assignmentCount', header: '已排工序' },
]
const deniedReason = computed(() =>
  !props.canManage
    ? '当前账号没有排产管理权限。'
    : !hasScope.value
      ? '请先选择业务范围。'
      : !props.workOrderId
        ? '请先选择要排产的工单。'
        : !selectedPlanId.value
          ? '请选择要插入的现有方案。'
          : '',
)
const statusLabel = computed(
  () =>
    ({ created: '排队中', running: '计算中', completed: '预览完成', failed: '计算失败' })[
      task.job.value?.status ?? 'created'
    ],
)
watch(plansQuery.error, (error) => {
  if (error) notifyOperationFailure('方案读取失败', error, '现有方案读取失败，请稍后重试。')
})
watch(plansQuery.data, (response) => {
  if (!response) return
  try {
    assertEnvelopeSuccess(response, '现有方案读取失败')
  } catch (error) {
    notifyOperationFailure('方案读取失败', error, '现有方案读取失败，请稍后重试。')
  }
})
watch(task.error, (error) => {
  if (error) notifyOperationFailure('插单预览失败', error, '插单预览失败，请稍后重试。')
})
async function start() {
  if (deniedReason.value || task.pending.value) return
  try {
    await task.start({
      ...props.context,
      planId: selectedPlanId.value,
      workOrderId: props.workOrderId,
    })
  } catch (error) {
    notifyOperationFailure('插单预览失败', error, '插单预览失败，请稍后重试。')
  }
}
</script>
<template>
  <section class="grid gap-4" aria-label="插入现有方案">
    <div>
      <h3 class="font-semibold">选择现有方案</h3>
      <p class="text-sm text-muted-foreground">
        沿用原方案完整工单集合与排程窗口，工单急单和优先级使用已保存值。合并去重后最多 500
        单；已在方案中的工单不会重复插入。
      </p>
    </div>
    <NvDataTable
      :columns="columns"
      :rows="plans"
      row-key="planId"
      :loading="plansQuery.isLoading.value"
      :searchable="false"
      :pagination="false"
      :column-settings="false"
      density="compact"
    >
      <template #cell-planId="{ row }"
        ><NvButton
          type="button"
          variant="ghost"
          :disabled="task.pending.value"
          :aria-pressed="selectedPlanId === row.planId"
          @click="selectedPlanId = row.planId ?? ''"
          >{{ selectedPlanId === row.planId ? '已选择 · ' : '' }}{{ row.planId }}</NvButton
        ></template
      >
    </NvDataTable>
    <div class="flex items-center justify-end gap-2 text-sm">
      <span>共 {{ total }} 个方案 · 第 {{ page + 1 }} 页</span>
      <NvButton
        type="button"
        size="sm"
        variant="outline"
        :disabled="page === 0 || task.pending.value"
        @click="page--"
        >上一页</NvButton
      >
      <NvButton
        type="button"
        size="sm"
        variant="outline"
        :disabled="(page + 1) * pageSize >= total || task.pending.value"
        @click="page++"
        >下一页</NvButton
      >
    </div>
    <p v-if="selectedPlanId" class="text-sm">
      所选方案 · {{ selectedPlanId }} ＋ 工单 {{ workOrderId }}
    </p>
    <p v-if="deniedReason" class="text-sm text-muted-foreground" role="status">
      {{ deniedReason }}
    </p>
    <NvButton
      type="button"
      class="justify-self-end"
      :disabled="!!deniedReason || task.pending.value"
      :title="deniedReason || undefined"
      @click="start"
      ><Spinner v-if="task.pending.value" />插入该单并重预览</NvButton
    >
    <div v-if="task.job.value" class="grid gap-1 rounded-md border p-3 text-sm" role="status">
      <p>
        本次核算 · {{ task.job.value.input?.planId }} ＋ 工单
        {{ task.job.value.input?.workOrderId }}
      </p>
      <p class="font-medium">
        {{ statusLabel }} · {{ task.job.value.input?.workOrderIds?.length }} 单
      </p>
      <p>
        原窗口 · {{ formatDateTime(task.job.value.input?.horizonStartUtc) }} 至
        {{ formatDateTime(task.job.value.input?.horizonEndUtc) }}
      </p>
      <p
        v-if="task.job.value.status === 'created' || task.job.value.status === 'running'"
        class="text-muted-foreground"
      >
        正在读取实际计算状态，完成后在此显示结果。
      </p>
    </div>
    <SchedulingPreviewResult v-if="task.preview.value" :plan="task.preview.value" />
  </section>
</template>
