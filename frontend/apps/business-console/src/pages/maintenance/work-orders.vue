<script setup lang="ts">
import DowntimeReasonFormSheet from '@/components/maintenance/DowntimeReasonFormSheet.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvMetricCard,
  NvMetricStrip,
  NvPageHeader,
  NvRowActions,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import MaintenanceWorkOrderCreateSheet from './components/MaintenanceWorkOrderCreateSheet.vue'
import MaintenanceExpectedRestoreSheet from './components/MaintenanceExpectedRestoreSheet.vue'
import MaintenanceWorkOrderCompleteSheet from './components/MaintenanceWorkOrderCompleteSheet.vue'
import { useMaintenanceWorkOrdersPage } from './useMaintenanceWorkOrdersPage'

definePage({
  meta: {
    requiresAuth: true,
    title: '维护工单',
    requiredPermissions: ['business.maintenance.work-orders.read'],
  },
})

const {
  addSpareRow,
  alarmOptions,
  alarmsPending,
  applySpareRowSku,
  autoSparePartCost,
  canComplete,
  canManageWorkOrders,
  canUpdateExpectedRestore,
  columns,
  completeCarriedItems,
  completeErrorMessage,
  completeForm,
  completeOpen,
  completeReasonKeyword,
  completeReasonMessage,
  completeReasonOptions,
  completeReasonState,
  completeReasonsTotal,
  completeSiteCode,
  completeTarget,
  completeWorkOrderPending,
  createCarried,
  createCarriedItems,
  createDeviceModel,
  createErrorMessage,
  createForm,
  createOpen,
  createWorkOrderPending,
  deviceLabel,
  directoryMessage,
  directoryState,
  downtimeReasonOptions,
  downtimeReasons,
  expectedRestoreAtLocal,
  expectedRestoreOpen,
  expectedRestorePending,
  expectedRestoreScope,
  expectedRestoreScopeKey,
  expectedRestoreScopeOptions,
  expectedRestoreScopes,
  expectedRestoreTarget,
  highPriorityPending,
  knownReasonCodes,
  listErrorMessage,
  onReasonSaved,
  openComplete,
  openCreate,
  openExpectedRestore,
  openReasonCreate,
  page,
  pageSize,
  pendingCount,
  priorityLabel,
  priorityOptions,
  reasonFormOpen,
  reasonFormSession,
  reasonKeyword,
  reasonsTotal,
  refreshWorkOrders,
  removeSpareRow,
  resultOptions,
  round2,
  router,
  rowKey,
  sparePartCostDisplay,
  spareRows,
  submitComplete,
  submitCreate,
  submitExpectedRestore,
  uomOptions,
  uomsPending,
  warrantyStatusLabel,
  workOrderNo,
  workOrders,
  workOrdersPending,
  workOrdersTotal,
  workerOptions,
  workersPending,
} = useMaintenanceWorkOrdersPage()
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="维护工单"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="`${workOrdersTotal} 张维护工单`"
    >
      <template #actions>
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
        <NvButton v-if="canManageWorkOrders" size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建维护工单
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
      <!-- 高优先待执行只出现在右侧告警卡（它带「看可用窗口」的行动出口）；
           这里再列一格同名同值的读数纯属重复，故只留总量与待执行两格。 -->
      <NvMetricStrip
        :cells="[
          {
            key: 'total',
            label: '维护工单',
            value: workOrdersTotal,
            unit: '张',
            meta: '当前业务范围内全部维护工单',
          },
          {
            key: 'pending',
            label: '待派工执行',
            value: pendingCount,
            unit: '张',
            meta: '尚未完工，需要排人排窗口',
          },
        ]"
      />
      <NvMetricCard
        variant="alert"
        label="高优先待执行"
        :value="highPriorityPending"
        unit="张"
        :tone="highPriorityPending > 0 ? 'warning' : 'neutral'"
        :status="
          highPriorityPending > 0
            ? { label: '需优先排程', tone: 'warning' }
            : { label: '无积压', tone: 'success' }
        "
        :foot-start="
          highPriorityPending > 0
            ? '高优先工单会占用设备可用窗口，先排这些再排常规保养。'
            : '当前没有高优先级的待执行维护工单。'
        "
        :action="highPriorityPending > 0 ? { label: '看可用窗口' } : undefined"
        @action="router.push({ path: '/maintenance/availability' })"
      />
    </div>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="workOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="workOrders"
      :row-key="rowKey"
      :loading="workOrdersPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无维护工单。设备报警或巡检发现异常时在此开单。"
    >
      <template #cell-warrantyStatus="{ row }"
        ><NvStatusBadge
          :value="row.warrantyStatus"
          :label="warrantyStatusLabel(row.warrantyStatus)"
      /></template>
      <template #cell-priority="{ row }"
        ><NvStatusBadge :value="row.priority" :label="priorityLabel(row.priority)"
      /></template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`维护工单操作 ${workOrderNo(row)}`">
          <NvDropdownMenuItem
            :disabled="!canUpdateExpectedRestore(row)"
            @click="openExpectedRestore(row)"
          >
            更新预计恢复时间
          </NvDropdownMenuItem>
          <NvDropdownMenuItem :disabled="!canComplete(row)" @click="openComplete(row)">
            <CheckCircle2Icon aria-hidden="true" />
            完成工单
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <!-- 新建维护工单：设备/优先级/开单人/报警/技师/预估工时/设备占用原因（7 字段）→ 侧滑 Sheet（A1 §1）。 -->
    <MaintenanceWorkOrderCreateSheet
      v-model:create-open="createOpen"
      v-model:create-device-model="createDeviceModel"
      v-model:reason-keyword="reasonKeyword"
      :alarm-options="alarmOptions"
      :alarms-pending="alarmsPending"
      :can-manage-work-orders="canManageWorkOrders"
      :create-carried="createCarried"
      :create-carried-items="createCarriedItems"
      :create-error-message="createErrorMessage"
      :create-form="createForm"
      :create-work-order-pending="createWorkOrderPending"
      :directory-message="directoryMessage"
      :directory-state="directoryState"
      :downtime-reason-options="downtimeReasonOptions"
      :downtime-reasons="downtimeReasons"
      :open-reason-create="openReasonCreate"
      :priority-options="priorityOptions"
      :reasons-total="reasonsTotal"
      :submit-create="submitCreate"
      :worker-options="workerOptions"
      :workers-pending="workersPending"
    />

    <MaintenanceExpectedRestoreSheet
      v-model:expected-restore-open="expectedRestoreOpen"
      v-model:expected-restore-scope-key="expectedRestoreScopeKey"
      v-model:expected-restore-at-local="expectedRestoreAtLocal"
      :device-label="deviceLabel"
      :expected-restore-pending="expectedRestorePending"
      :expected-restore-scope="expectedRestoreScope"
      :expected-restore-scope-options="expectedRestoreScopeOptions"
      :expected-restore-scopes="expectedRestoreScopes"
      :expected-restore-target="expectedRestoreTarget"
      :submit-expected-restore="submitExpectedRestore"
      :work-order-no="workOrderNo"
    />

    <!-- 完成维护工单：结果/停机/工时 + 备件动态行 + 成本汇总 → 侧滑 Sheet（A1 §1）。 -->
    <MaintenanceWorkOrderCompleteSheet
      v-model:complete-open="completeOpen"
      v-model:complete-reason-keyword="completeReasonKeyword"
      v-model:spare-part-cost-display="sparePartCostDisplay"
      :add-spare-row="addSpareRow"
      :apply-spare-row-sku="applySpareRowSku"
      :auto-spare-part-cost="autoSparePartCost"
      :can-manage-work-orders="canManageWorkOrders"
      :complete-carried-items="completeCarriedItems"
      :complete-error-message="completeErrorMessage"
      :complete-form="completeForm"
      :complete-reason-message="completeReasonMessage"
      :complete-reason-options="completeReasonOptions"
      :complete-reason-state="completeReasonState"
      :complete-reasons-total="completeReasonsTotal"
      :complete-site-code="completeSiteCode"
      :complete-target="completeTarget"
      :complete-work-order-pending="completeWorkOrderPending"
      :open-reason-create="openReasonCreate"
      :remove-spare-row="removeSpareRow"
      :result-options="resultOptions"
      :round2="round2"
      :spare-rows="spareRows"
      :submit-complete="submitComplete"
      :uom-options="uomOptions"
      :uoms-pending="uomsPending"
      :work-order-no="workOrderNo"
    />
    <DowntimeReasonFormSheet
      v-if="reasonFormSession"
      :key="reasonFormSession"
      v-model:open="reasonFormOpen"
      :existing-codes="knownReasonCodes"
      @saved="onReasonSaved"
    />
  </BusinessLayout>
</template>
