<script setup lang="ts">
import { formatDateTime } from '@/utils/format'
import { describeScheduleInvalidationReason } from '@/composables/scheduling/useScheduleInvalidation'
import {
  schedulingPlanStatusLabel,
  schedulingPlanStatusTone,
} from '@/utils/schedulingPlanPresentation'
import {
  NvButton,
  NvInput,
  NvDataTable,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
  NvStatusBadge,
  NvTabsContent,
} from '@nerv-iip/ui'
import { EyeIcon, SendIcon, Undo2Icon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useSchedulingPageController } from './useSchedulingPageController'

type View = ShallowUnwrapRef<ReturnType<typeof useSchedulingPageController>>

defineProps<{
  actionablePlans: View['actionablePlans']
  canRelease: View['canRelease']
  canRevoke: View['canRevoke']
  clearHistoryFilters: View['clearHistoryFilters']
  columns: View['columns']
  hasHistoryFilters: View['hasHistoryFilters']
  historyStatuses: View['historyStatuses']
  openDetail: View['openDetail']
  plansError: View['plansError']
  plansPending: View['plansPending']
  plansTotal: View['plansTotal']
  publish: View['publish']
  refreshPlans: View['refreshPlans']
  releaseDisabledReason: View['releaseDisabledReason']
  releasePlanPending: View['releasePlanPending']
  requestRevoke: View['requestRevoke']
  revokePlanPending: View['revokePlanPending']
  rowKey: View['rowKey']
  schedulingFilters: View['schedulingFilters']
}>()

const historyStatus = defineModel<View['historyStatus']>('historyStatus', { required: true })

const historyInvalidated = defineModel<View['historyInvalidated']>('historyInvalidated', {
  required: true,
})

const page = defineModel<View['page']>('page', { required: true })

const pageSize = defineModel<View['pageSize']>('pageSize', { required: true })

const activeView = defineModel<View['activeView']>('activeView', { required: true })
</script>

<template>
  <NvTabsContent value="table" class="grid min-w-0 gap-4">
    <!-- 只读边界要自解释：历史方案是已生成结果的查阅面，改排程只能回草案工作区。
         不写这句，用户会在表里反复点、以为"表格坏了"（MAN-691 / #1259）。 -->
    <div
      data-testid="plan-table-readonly-notice"
      class="flex flex-wrap items-start justify-between gap-3 rounded-lg border bg-muted/30 p-3"
    >
      <div class="grid gap-1">
        <p class="text-sm font-medium text-foreground">历史方案：只读查阅</p>
        <p class="max-w-2xl text-sm text-muted-foreground">
          这里列的是已生成的排程方案，只能查看明细、发布或撤销发布；工序的资源与时间不能在这张表上改。
          要调整排程，回草案工作区改完再重预览生成新版本。
        </p>
      </div>
      <NvButton
        size="sm"
        variant="outline"
        type="button"
        title="回到草案工作区调整工序资源与时间，再重预览生成新版本"
        @click="activeView = 'workbench'"
      >
        去草案工作区修改
      </NvButton>
    </div>
    <div class="flex flex-wrap items-end gap-3">
      <div class="grid gap-1">
        <span class="text-sm font-medium">状态</span>
        <NvSelect v-model="historyStatus">
          <NvSelectTrigger class="w-36" aria-label="按方案状态筛选"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部状态</NvSelectItem>
            <NvSelectItem v-for="status in historyStatuses" :key="status" :value="status">{{
              schedulingPlanStatusLabel(status)
            }}</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </div>
      <div class="grid gap-1">
        <label for="history-released-on" class="text-sm font-medium">发布日（UTC）</label>
        <NvInput
          id="history-released-on"
          v-model="schedulingFilters.releasedOn"
          type="date"
          class="w-44"
        />
      </div>
      <div class="grid gap-1">
        <span class="text-sm font-medium">失效</span>
        <NvSelect v-model="historyInvalidated">
          <NvSelectTrigger class="w-36" aria-label="按方案失效筛选"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部方案</NvSelectItem>
            <NvSelectItem value="true">已失效</NvSelectItem>
            <NvSelectItem value="false">未失效</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </div>
      <p class="pb-2 text-sm text-muted-foreground">发布时间从新到旧，未发布方案排在后面</p>
    </div>
    <NvDataTable
      class="min-w-0"
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="plansTotal"
      @update:page="page = $event"
      @update:page-size="pageSize = String($event)"
      :columns="columns"
      :rows="actionablePlans"
      :row-key="rowKey"
      :loading="plansPending"
      :searchable="false"
      :column-settings="false"
      :empty-message="hasHistoryFilters ? '没有符合条件的方案' : '还没有排程方案'"
      :error="plansError"
      error-message="没有取到排程方案列表，当前无法判断已有哪些方案。请重试，或稍后再看。"
      @retry="refreshPlans"
    >
      <template #empty>
        <template v-if="hasHistoryFilters">
          <p class="text-sm font-medium text-foreground">没有符合条件的方案</p>
          <p class="max-w-md text-sm text-muted-foreground">
            调整筛选条件，或清空筛选查看历史方案。
          </p>
          <NvButton size="sm" type="button" class="mt-1" @click="clearHistoryFilters"
            >清空筛选</NvButton
          >
        </template>
        <template v-else>
          <p class="text-sm font-medium text-foreground">还没有排程方案</p>
          <p class="max-w-md text-sm text-muted-foreground">
            先在排程总览里挑出要排的工单，生成首版方案后即可在这里查看、对比并发布。
          </p>
          <NvButton size="sm" type="button" class="mt-1" @click="activeView = 'workbench'">
            去排程总览生成方案
          </NvButton>
        </template>
      </template>
      <template #cell-status="{ row }">
        <div class="flex flex-wrap items-center gap-1.5">
          <NvStatusBadge
            :label="schedulingPlanStatusLabel(row.status)"
            :tone="schedulingPlanStatusTone(row.status)"
          />
          <NvStatusBadge v-if="row.isInvalidated" label="已失效" tone="warning" />
        </div>
      </template>
      <template #cell-invalidation="{ row }">
        <span v-if="row.isInvalidated" class="text-sm text-warning-strong">
          {{ describeScheduleInvalidationReason(row.latestInvalidationReasonCode) }}
        </span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-generatedAtUtc="{ row }">
        {{ formatDateTime(row.generatedAtUtc) }}
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-2">
          <NvButton size="sm" variant="outline" type="button" @click="openDetail(row.planId)">
            <EyeIcon aria-hidden="true" />
            明细
          </NvButton>
          <NvButton
            size="sm"
            type="button"
            :disabled="!canRelease(row) || releasePlanPending"
            :title="releaseDisabledReason(row)"
            @click="publish(row)"
          >
            <NvSpinner v-if="releasePlanPending" aria-hidden="true" />
            <SendIcon v-else aria-hidden="true" />
            发布
          </NvButton>
          <NvButton
            v-if="canRevoke(row)"
            size="sm"
            variant="destructive"
            type="button"
            :disabled="revokePlanPending"
            title="撤销该已发布方案，MES 侧回流撤销对应工序排程"
            @click="requestRevoke(row)"
          >
            <Undo2Icon aria-hidden="true" />
            撤销发布
          </NvButton>
        </div>
      </template>
    </NvDataTable>
  </NvTabsContent>
</template>
