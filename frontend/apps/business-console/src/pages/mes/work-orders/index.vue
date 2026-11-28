<script setup lang="ts">
import WorkOrderTransformationDialog from '@/components/mes/WorkOrderTransformationDialog.vue'
import MesWorkScopeSelect from '@/components/mes/MesWorkScopeSelect.vue'
import ProductionReportDialog from '@/components/mes/ProductionReportDialog.vue'
import SingleOrderSchedulingDialog from '@/components/scheduling/SingleOrderSchedulingDialog.vue'
import WorkOrderDetailSheet from '@/components/mes/WorkOrderDetailSheet.vue'
import WorkOrderDemandChangeBadges from '@/components/mes/WorkOrderDemandChangeBadges.vue'
import OrderUrgencyBadge from '@/components/urgency/OrderUrgencyBadge.vue'
import UrgencyDisplayModeSelect from '@/components/urgency/UrgencyDisplayModeSelect.vue'
import { usePermission } from '@/composables/usePermission'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { computed } from 'vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import {
  CalendarCheckIcon,
  CalendarCogIcon,
  ClipboardCheckIcon,
  ExternalLinkIcon,
  EyeIcon,
  FactoryIcon,
  LockIcon,
  RefreshCwIcon,
} from '@lucide/vue'
import WorkOrderReleaseDialog from './dialogs/WorkOrderReleaseDialog.vue'
import RushWorkOrderDialog from './dialogs/RushWorkOrderDialog.vue'
import { useWorkOrdersPage } from './useWorkOrdersPage'

definePage({
  meta: {
    requiresAuth: true,
    title: '工单与派工',
    requiredPermissions: ['business.mes.work-orders.read'],
  },
})

const {
  canCreateRush,
  canManageWorkOrders,
  canReportOrder,
  canSubmitRelease,
  clearReleaseIntent,
  columns,
  createRushWorkOrderPending,
  displayMode,
  formatDateTime,
  formatQuantity,
  isTerminalWorkOrderStatus,
  keyword,
  listErrorMessage,
  mergeButtonDisabled,
  mergeButtonTitle,
  mergeDialogOpen,
  mergeError,
  mergeIdempotencyKey,
  mergePending,
  mergeResult,
  mergeSources,
  mergeState,
  mergeUnitUnavailable,
  openMergeDialog,
  openOrderDetail,
  openOrderSheet,
  openReleaseDialog,
  openReport,
  openReportFromSheet,
  openSchedule,
  operationTasksPending,
  orderUrgencies,
  page,
  pageSize,
  pagedWorkOrders,
  productionVersionLabel,
  productionVersionsPending,
  refreshUrgency,
  refreshWorkOrders,
  releaseBlocker,
  releaseDialogOpen,
  releaseIntent,
  releaseIntentOrder,
  releasePreflightPending,
  releaseRetroactiveNotice,
  releaseValidationMessage,
  releaseWarningsConfirmed,
  releaseWorkOrderPending,
  reportContext,
  reportSheetOpen,
  resetFilters,
  resolveSku,
  resolveWorkCenter,
  retryMergeReadback,
  router,
  rowKey,
  rushForm,
  rushOperationTaskOptions,
  rushSheetOpen,
  rushVersionOptions,
  scheduleDisabledReason,
  scheduleOpen,
  scheduleTarget,
  selectedWorkOrderIds,
  sheetWorkOrderId,
  sort,
  statusFilter,
  statusLabel,
  statusOptions,
  submitMerge,
  submitReleaseWorkOrder,
  submitRushWorkOrder,
  workCenterFilter,
  workCenterOptions,
  workOrderReadScopeMessage,
  workOrdersError,
  workOrdersPending,
  workOrdersTotal,
} = useWorkOrdersPage()
const { can } = usePermission()
const canOpenPlans = computed(() => can(P.mesPlansRead))
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="工单与派工"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${workOrdersTotal} 个工单`"
    >
      <template #actions>
        <NvButton
          v-if="canOpenPlans"
          size="sm"
          type="button"
          variant="outline"
          @click="router.push('/mes/plans')"
        >
          <CalendarCheckIcon aria-hidden="true" />
          生产计划
        </NvButton>
        <NvButton
          v-if="canManageWorkOrders"
          size="sm"
          type="button"
          variant="outline"
          @click="rushSheetOpen = true"
        >
          <FactoryIcon aria-hidden="true" />
          创建急单
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="workOrdersPending"
          @click="refreshWorkOrders"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <p
      v-if="workOrderReadScopeMessage"
      class="text-sm text-destructive"
      role="alert"
      data-testid="work-order-read-scope-message"
    >
      {{ workOrderReadScopeMessage }}
    </p>

    <NvToolbar v-model:search="keyword" search-placeholder="搜索工单、物料、生产版本">
      <template #filters>
        <MesWorkScopeSelect permission-code="business.mes.work-orders.read" />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="工单状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in statusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="workCenterFilter">
          <NvSelectTrigger class="h-9 w-40" aria-label="工作中心"
            ><NvSelectValue placeholder="全部工作中心"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部工作中心</NvSelectItem>
            <NvSelectItem
              v-for="option in workCenterOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
      <template #actions>
        <UrgencyDisplayModeSelect v-model="displayMode" />
        <NvButton type="button" variant="ghost" size="sm" @click="resetFilters">重置</NvButton>
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="workOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedWorkOrders"
      :row-key="rowKey"
      :client-sort="false"
      :loading="workOrdersPending"
      :error="workOrdersError"
      :error-message="listErrorMessage"
      empty-message="当前筛选下没有工单。正常生产请先进入生产计划转工单，急单只处理临时插单。"
      :searchable="false"
      :column-settings="false"
      selectable
      v-model:selected="selectedWorkOrderIds"
      @retry="refreshWorkOrders"
    >
      <template #bulk-actions>
        <NvButton
          type="button"
          size="sm"
          variant="outline"
          :disabled="mergeButtonDisabled"
          :title="mergeButtonTitle"
          data-testid="open-merge-work-orders"
          @click="openMergeDialog"
        >
          合并选中工单
        </NvButton>
        <span
          v-if="mergeUnitUnavailable"
          class="text-xs text-destructive"
          role="alert"
          data-testid="merge-unit-unavailable"
        >
          选中的工单未返回单位信息，无法确认数量单位；请刷新列表后重试。
        </span>
      </template>
      <template #cell-workOrderId="{ row }">
        <RouterLink
          v-if="row.workOrderId"
          :to="`/mes/work-orders/${encodeURIComponent(row.workOrderId)}`"
          class="flex flex-col gap-0.5 text-left"
        >
          <span class="font-medium text-brand underline-offset-4 hover:underline">{{
            row.workOrderNo ?? row.workOrderId
          }}</span>
          <span class="text-xs text-muted-foreground">{{
            resolveSku(row.skuCode ?? row.skuId) ?? '无'
          }}</span>
        </RouterLink>
        <div v-else class="flex flex-col gap-0.5">
          <span class="font-medium text-muted-foreground">无编号</span>
          <span class="text-xs text-muted-foreground">{{
            resolveSku(row.skuCode ?? row.skuId) ?? '无'
          }}</span>
        </div>
      </template>
      <template #cell-status="{ row }">
        <div class="flex items-center gap-1.5">
          <NvStatusBadge :value="row.status" :label="statusLabel(row.status)" />
          <WorkOrderDemandChangeBadges
            :has-changed-demand="row.hasChangedDemand"
            :has-cancelled-demand="row.hasCancelledDemand"
          />
          <!-- 质量保留锁定标记：与工单生命周期状态无关，来源为活跃 quality hold（#886）。 -->
          <LockIcon
            v-if="row.hasActiveQualityHold"
            class="size-3.5 text-destructive"
            aria-label="存在有效质量保留"
            title="存在有效质量保留，需处理后才能放行或开工"
          />
        </div>
      </template>
      <template #cell-urgency="{ row }">
        <span v-if="isTerminalWorkOrderStatus(row.status)" class="text-xs text-muted-foreground">
          已结束
        </span>
        <OrderUrgencyBadge
          v-else
          :order-reference="row.workOrderId ?? ''"
          :mode="displayMode"
          :urgency="
            row.workOrderId ? orderUrgencies.byReference.value.get(row.workOrderId) : undefined
          "
          :source-unavailable="orderUrgencies.error?.value != null"
          @refresh="refreshUrgency"
        />
      </template>
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template
      >
      <template #cell-completedQuantity="{ row }"
        ><span class="tabular-nums">{{
          row.completedQuantity == null
            ? '—'
            : row.completedQuantity.toLocaleString(undefined, { maximumFractionDigits: 6 })
        }}</span></template
      >
      <template #cell-dueUtc="{ row }">{{ formatDateTime(row.dueUtc) }}</template>
      <template #cell-operationCount="{ row }">
        <div class="grid gap-1">
          <!-- 一行四段斜杠拼接读不出主次：第一行给「第几道 · 在哪做」，第二行只给状态。
               工序任务号（WO-…-OP-10）此前每道都印一遍——左边〈工单〉列已经有同一个
               WO-…，前缀完全重复，8 道工序就把真正有用的「第几道 · 在哪做」淹了，
               行高涨到约 320px 没法扫读。工序号不是这一格要回答的问题。 -->
          <div
            v-for="task in row.operationTasks ?? []"
            :key="task.operationTaskId ?? `${row.workOrderId}-${task.operationSequence}`"
            class="grid gap-0.5"
          >
            <span class="text-xs font-medium text-foreground">
              第 {{ task.operationSequence ?? '—' }} 道 ·
              {{
                task.workCenterName ??
                resolveWorkCenter(task.workCenterCode ?? task.workCenterId) ??
                '未指定工作中心'
              }}
            </span>
            <span class="text-xs text-muted-foreground">{{ statusLabel(task.status) }}</span>
          </div>
          <span v-if="!row.operationTasks?.length" class="text-xs text-muted-foreground"
            >暂无工序任务</span
          >
        </div>
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`工单操作 ${row.workOrderId ?? ''}`">
          <!-- 详情 / 工序 / 齐套 / 派工 / 状态流转全部收进行内抽屉，不再逐个跳页丢上下文。 -->
          <NvDropdownMenuItem :disabled="!row.workOrderId" @click="openOrderSheet(row)">
            <EyeIcon aria-hidden="true" />
            工单详情与工序
          </NvDropdownMenuItem>
          <NvDropdownMenuItem :disabled="!canReportOrder(row)" @click="openReport(row)">
            <ClipboardCheckIcon aria-hidden="true" />
            {{ canReportOrder(row) ? '生产报工' : '暂无工序，不能报工' }}
          </NvDropdownMenuItem>
          <NvDropdownMenuItem
            :aria-label="`下达工单 ${row.workOrderNo || row.workOrderId || ''}`"
            :disabled="releasePreflightPending || Boolean(releaseBlocker(row))"
            :title="
              releasePreflightPending ? '正在确认下达条件' : (releaseBlocker(row) ?? '下达当前工单')
            "
            @click="openReleaseDialog(row)"
          >
            <FactoryIcon aria-hidden="true" />
            {{ releaseBlocker(row) ? `不能下达：${releaseBlocker(row)}` : '下达工单' }}
          </NvDropdownMenuItem>
          <NvDropdownMenuItem
            :disabled="Boolean(scheduleDisabledReason(row))"
            :title="scheduleDisabledReason(row) || '对当前工单生成排程方案'"
            @click="openSchedule(row)"
          >
            <CalendarCogIcon aria-hidden="true" />
            对该单排产
          </NvDropdownMenuItem>
          <NvDropdownMenuSeparator />
          <NvDropdownMenuItem :disabled="!row.workOrderId" @click="openOrderDetail(row)">
            <ExternalLinkIcon aria-hidden="true" />
            打开完整详情页
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <SingleOrderSchedulingDialog
      v-if="scheduleOpen && scheduleTarget"
      v-model:open="scheduleOpen"
      :work-order-id="scheduleTarget.workOrderId"
      :context-label="`MES 工单 ${scheduleTarget.workOrderNo ?? scheduleTarget.workOrderId}`"
      @scheduled="refreshWorkOrders"
    />

    <WorkOrderTransformationDialog
      v-model:open="mergeDialogOpen"
      mode="merge"
      :state="mergeState"
      :pending="mergePending"
      :sources="mergeSources"
      :result="mergeResult"
      :error-message="mergeError"
      :idempotency-key="mergeIdempotencyKey"
      @submit="submitMerge"
      @retry-readback="retryMergeReadback"
    />

    <WorkOrderReleaseDialog
      v-model:release-dialog-open="releaseDialogOpen"
      v-model:release-warnings-confirmed="releaseWarningsConfirmed"
      :can-submit-release="canSubmitRelease"
      :clear-release-intent="clearReleaseIntent"
      :production-version-label="productionVersionLabel"
      :release-intent="releaseIntent"
      :release-intent-order="releaseIntentOrder"
      :release-retroactive-notice="releaseRetroactiveNotice"
      :release-validation-message="releaseValidationMessage"
      :release-work-order-pending="releaseWorkOrderPending"
      :status-label="statusLabel"
      :submit-release-work-order="submitReleaseWorkOrder"
    />

    <RushWorkOrderDialog
      v-if="canManageWorkOrders"
      v-model:rush-sheet-open="rushSheetOpen"
      :can-create-rush="canCreateRush"
      :create-rush-work-order-pending="createRushWorkOrderPending"
      :operation-tasks-pending="operationTasksPending"
      :production-versions-pending="productionVersionsPending"
      :rush-form="rushForm"
      :rush-operation-task-options="rushOperationTaskOptions"
      :rush-version-options="rushVersionOptions"
      :submit-rush-work-order="submitRushWorkOrder"
    />

    <!-- 报工「带出式录入」样板：上下文只能由所选工单行带出，弹窗自身不提供工单/工序挑选入口。 -->
    <ProductionReportDialog
      v-model:open="reportSheetOpen"
      :context="reportContext"
      @reported="refreshWorkOrders"
    />

    <WorkOrderDetailSheet v-model:work-order-id="sheetWorkOrderId" @report="openReportFromSheet" />
  </BusinessLayout>
</template>
