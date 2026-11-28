<script setup lang="ts">
import { usePermission } from '@/composables/usePermission'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { shallowRef } from 'vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import InspectionRecordDetailSheet from '@/components/quality/InspectionRecordDetailSheet.vue'
import FirstArticleInspectionRecords from '@/components/quality/FirstArticleInspectionRecords.vue'
import FirstArticlePlanSheet from '@/components/quality/FirstArticlePlanSheet.vue'
import {
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
  NvToolbar,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, RefreshCwIcon, Settings2Icon } from '@lucide/vue'
import { RouterLink } from 'vue-router'
import InspectionRecordCreateDialog from './components/InspectionRecordCreateDialog.vue'
import { useInspectionPage } from './useInspectionPage'

const { can } = usePermission()

definePage({
  meta: {
    requiresAuth: true,
    title: '检验任务与记录',
    requiredPermissions: ['business.quality.inspection-records.read'],
  },
})

const recordsList = shallowRef<InstanceType<typeof FirstArticleInspectionRecords>>()
const {
  INSPECTION_PLAN_CATEGORY_OPTIONS,
  SOURCE_DOCUMENT_KINDS,
  activateDraftFirstArticlePlan,
  activateFirstArticlePlanPending,
  activeView,
  addCharacteristicRow,
  canCreateRecord,
  canManageInspectionPlans,
  changeSourceType,
  characteristicOptions,
  characteristicSuggestions,
  characteristicsPlanId,
  columns,
  contextWorkOrderId,
  createInspectionRecordPending,
  defectContinuationRoute,
  filters,
  firstArticlePlanSheetOpen,
  inspectionPlanCategoryFilter,
  inspectionPlans,
  inspectionPlansPending,
  inspectionPlansTotal,
  isDraftFirstArticlePlan,
  isInspectionTaskFlow,
  isOperationInspectionFlow,
  isVariableLine,
  listErrorMessage,
  onCharacteristicCodeChange,
  openFirstArticleRecord,
  operationContext,
  operationContextMessage,
  page,
  pageHeaderCount,
  pageSize,
  planCatalog,
  planCharacteristicsError,
  planCharacteristicsPending,
  reasonCatalog,
  recordContextItems,
  recordDetailId,
  recordDetailOpen,
  recordForm,
  recordPlanModel,
  recordSheetOpen,
  refreshInspectionPlans,
  refreshPlanCharacteristics,
  removeCharacteristicRow,
  requiresDispositionReason,
  scanAuditRoute,
  skuCatalog,
  sourceWorkOrderRoute,
  submitBlockers,
  submitInspectionRecord,
  targetInspectionPlanId,
  targetInspectionPlanMissing,
  unitDisplay,
  unitPolicyFor,
  uomCatalog,
  useInspectionPlan,
} = useInspectionPage(() => recordsList.value?.resetFilters())
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="检验任务与记录"
      :breadcrumbs="[{ label: '质量' }]"
      :count="pageHeaderCount"
    >
      <template #actions>
        <NvButton
          v-if="activeView === 'plans' && canManageInspectionPlans"
          size="sm"
          type="button"
          variant="outline"
          @click="firstArticlePlanSheetOpen = true"
        >
          <Settings2Icon aria-hidden="true" />
          配置首件检验方案
        </NvButton>
        <NvButton v-if="defectContinuationRoute" size="sm" type="button" as-child>
          <RouterLink :to="defectContinuationRoute">登记同工序不良</RouterLink>
        </NvButton>
        <NvButton v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="sourceWorkOrderRoute">返回工单 {{ contextWorkOrderId }}</RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="scanAuditRoute">扫码记录</RouterLink>
        </NvButton>
        <NvButton
          v-if="can(P.qualityInspectionRecordsCreate)"
          size="sm"
          type="button"
          @click="recordSheetOpen = true"
        >
          <ClipboardCheckIcon aria-hidden="true" />
          创建检验记录
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="activeView === 'plans' && inspectionPlansPending"
          @click="activeView === 'plans' ? refreshInspectionPlans() : recordsList?.refresh()"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvTabs v-model="activeView">
      <NvTabsList>
        <NvTabsTrigger value="plans">检验方案</NvTabsTrigger>
        <NvTabsTrigger value="first-article-records">检验记录</NvTabsTrigger>
      </NvTabsList>

      <NvTabsContent value="plans" class="grid gap-3">
        <div class="flex items-center justify-between gap-3">
          <div>
            <h2 class="text-lg font-semibold">检验方案</h2>
            <p class="text-sm text-muted-foreground">
              首件检验方案固定关联物料与工序工作中心，草稿方案可在行操作中继续启用。
            </p>
          </div>
        </div>

        <NvToolbar :show-search="false">
          <template #filters>
            <NvSelect v-model="inspectionPlanCategoryFilter">
              <NvSelectTrigger class="h-9 w-36" aria-label="检验类别">
                <NvSelectValue placeholder="全部类别" />
              </NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="all">全部类别</NvSelectItem>
                <NvSelectItem
                  v-for="option in INSPECTION_PLAN_CATEGORY_OPTIONS"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
            <NvInput
              v-model="filters.status"
              class="h-9 w-32"
              placeholder="状态（可选）"
              aria-label="检验状态"
            />
          </template>
        </NvToolbar>

        <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
          {{ listErrorMessage }}
        </p>
        <p v-else-if="targetInspectionPlanMissing" class="text-sm text-warning" role="status">
          未找到检验方案 {{ targetInspectionPlanId }}。请确认该方案是否已归档或无权访问。
        </p>

        <NvDataTable
          manual
          :page="page"
          :page-size="pageSize"
          :total-items="inspectionPlansTotal"
          :columns="columns"
          :rows="inspectionPlans"
          :row-key="(r) => r.id ?? r.code ?? '无'"
          :loading="inspectionPlansPending"
          :searchable="false"
          :column-settings="false"
          empty-message="当前筛选下没有检验方案。可配置首件检验方案，或从工单、收货、检验任务进入创建检验记录。"
          @update:page="page = $event"
          @update:page-size="(v) => (pageSize = String(v))"
        >
          <template #cell-code="{ row }">
            <span class="font-medium">{{ row.code ?? '无' }}</span>
          </template>
          <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
          <template #cell-actions="{ row }">
            <NvRowActions :label="`检验方案操作 ${row.code ?? ''}`">
              <NvDropdownMenuItem
                v-if="canManageInspectionPlans && isDraftFirstArticlePlan(row)"
                :disabled="activateFirstArticlePlanPending"
                @click="activateDraftFirstArticlePlan(row)"
              >
                <Settings2Icon aria-hidden="true" />
                启用首件方案
              </NvDropdownMenuItem>
              <NvDropdownMenuItem
                v-if="can(P.qualityInspectionRecordsCreate)"
                @click="useInspectionPlan(row)"
              >
                <ClipboardCheckIcon aria-hidden="true" />
                创建检验记录
              </NvDropdownMenuItem>
            </NvRowActions>
          </template>
        </NvDataTable>
      </NvTabsContent>

      <NvTabsContent value="first-article-records" class="grid gap-3">
        <FirstArticleInspectionRecords
          ref="recordsList"
          :sku-options="skuCatalog.skuOptions.value"
          :skus-pending="skuCatalog.skusPending.value"
          @open-record="openFirstArticleRecord"
        />
      </NvTabsContent>
    </NvTabs>

    <FirstArticlePlanSheet
      v-if="canManageInspectionPlans"
      v-model:open="firstArticlePlanSheetOpen"
      :organization-id="filters.organizationId"
      :environment-id="filters.environmentId"
      @completed="refreshInspectionPlans"
    />

    <InspectionRecordCreateDialog
      v-if="can(P.qualityInspectionRecordsCreate)"
      v-model:record-sheet-open="recordSheetOpen"
      v-model:record-plan-model="recordPlanModel"
      :-s-o-u-r-c-e_-d-o-c-u-m-e-n-t_-k-i-n-d-s="SOURCE_DOCUMENT_KINDS"
      :add-characteristic-row="addCharacteristicRow"
      :can-create-record="canCreateRecord"
      :change-source-type="changeSourceType"
      :characteristic-options="characteristicOptions"
      :characteristic-suggestions="characteristicSuggestions"
      :characteristics-plan-id="characteristicsPlanId"
      :create-inspection-record-pending="createInspectionRecordPending"
      :is-inspection-task-flow="isInspectionTaskFlow"
      :is-operation-inspection-flow="isOperationInspectionFlow"
      :is-variable-line="isVariableLine"
      :on-characteristic-code-change="onCharacteristicCodeChange"
      :operation-context="operationContext"
      :operation-context-message="operationContextMessage"
      :plan-catalog="planCatalog"
      :plan-characteristics-error="planCharacteristicsError"
      :plan-characteristics-pending="planCharacteristicsPending"
      :reason-catalog="reasonCatalog"
      :record-context-items="recordContextItems"
      :record-form="recordForm"
      :refresh-plan-characteristics="refreshPlanCharacteristics"
      :remove-characteristic-row="removeCharacteristicRow"
      :requires-disposition-reason="requiresDispositionReason"
      :submit-blockers="submitBlockers"
      :submit-inspection-record="submitInspectionRecord"
      :unit-display="unitDisplay"
      :unit-policy-for="unitPolicyFor"
      :uom-catalog="uomCatalog"
    />

    <!-- 来源检验记录只读详情：hold 时间线「来源检验记录」互链带 ?inspectionRecordId= 进入即定位到该记录。 -->
    <InspectionRecordDetailSheet
      v-model:open="recordDetailOpen"
      :record-id="recordDetailId"
      :organization-id="filters.organizationId"
      :environment-id="filters.environmentId"
    />
  </BusinessLayout>
</template>
