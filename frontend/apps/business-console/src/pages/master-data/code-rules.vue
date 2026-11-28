<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvDataTable, NvPageHeader, NvStatusBadge, NvToolbar } from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import CodeRuleDetailDialog from './components/CodeRuleDetailDialog.vue'
import CodeRuleVersionDialog from './components/CodeRuleVersionDialog.vue'
import { useCodeRulesPage } from './useCodeRulesPage'

definePage({
  meta: {
    requiresAuth: true,
    title: '编码规则',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

const {
  CHANGE_REASON_MAX_LENGTH,
  CHECKSUM_ALGORITHM_OPTIONS,
  DATE_FORMAT_OPTIONS,
  RESET_OPTIONS,
  SCOPE_OPTIONS,
  SEGMENT_TYPE_OPTIONS,
  TRANSFORM_OPTIONS,
  addSegment,
  canSubmit,
  changeReasonError,
  changeReasonValid,
  columns,
  createPending,
  displayNameValid,
  editingCurrentVersion,
  editingRuleKey,
  form,
  formOpen,
  formPreview,
  formPreviewPending,
  formatSegments,
  listErrorMessage,
  onFormOpenChange,
  openCreate,
  openView,
  page,
  pageSize,
  pagedRules,
  search,
  filteredRules,
  previewFormSample,
  previewViewSample,
  refresh,
  removeSegment,
  ruleStatusLabel,
  ruleStatusTone,
  rules,
  rulesPending,
  scopeLabel,
  segmentErrors,
  segmentParams,
  segmentTypeLabel,
  sequenceCountError,
  showErrors,
  submitForm,
  versions,
  versionsError,
  versionsPending,
  viewOpen,
  viewPreview,
  viewPreviewPending,
  viewSegments,
  viewTarget,
} = useCodeRulesPage()
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="编码规则"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${rules.length} 条规则`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="rulesPending"
          @click="refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvToolbar v-model:search="search" search-placeholder="搜索规则键、名称" />

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="filteredRules.length"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :searchable="false"
      :column-settings="false"
      :columns="columns"
      :rows="pagedRules"
      row-key="ruleKey"
      :loading="rulesPending"
      empty-message="暂无编码规则。"
    >
      <template #cell-scope="{ row }">{{ scopeLabel(row.scope) }}</template>
      <template #cell-format="{ row }">
        <span class="text-muted-foreground">{{ formatSegments(row.segments) }}</span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :label="ruleStatusLabel(row)" :tone="ruleStatusTone(row)" />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-1">
          <NvButton type="button" variant="ghost" size="sm" @click="openView(row)">查看</NvButton>
          <NvButton type="button" variant="ghost" size="sm" @click="openCreate(row)"
            >新建版本</NvButton
          >
        </div>
      </template>
    </NvDataTable>

    <!-- 查看 Sheet 抽屉 -->
    <CodeRuleDetailDialog
      v-model:view-open="viewOpen"
      :preview-view-sample="previewViewSample"
      :rule-status-label="ruleStatusLabel"
      :rule-status-tone="ruleStatusTone"
      :scope-label="scopeLabel"
      :segment-params="segmentParams"
      :segment-type-label="segmentTypeLabel"
      :versions="versions"
      :versions-error="versionsError"
      :versions-pending="versionsPending"
      :view-preview="viewPreview"
      :view-preview-pending="viewPreviewPending"
      :view-segments="viewSegments"
      :view-target="viewTarget"
    />

    <!-- 新建版本 Dialog -->
    <CodeRuleVersionDialog
      :-c-h-a-n-g-e_-r-e-a-s-o-n_-m-a-x_-l-e-n-g-t-h="CHANGE_REASON_MAX_LENGTH"
      :-c-h-e-c-k-s-u-m_-a-l-g-o-r-i-t-h-m_-o-p-t-i-o-n-s="CHECKSUM_ALGORITHM_OPTIONS"
      :-d-a-t-e_-f-o-r-m-a-t_-o-p-t-i-o-n-s="DATE_FORMAT_OPTIONS"
      :-r-e-s-e-t_-o-p-t-i-o-n-s="RESET_OPTIONS"
      :-s-c-o-p-e_-o-p-t-i-o-n-s="SCOPE_OPTIONS"
      :-s-e-g-m-e-n-t_-t-y-p-e_-o-p-t-i-o-n-s="SEGMENT_TYPE_OPTIONS"
      :-t-r-a-n-s-f-o-r-m_-o-p-t-i-o-n-s="TRANSFORM_OPTIONS"
      :add-segment="addSegment"
      :can-submit="canSubmit"
      :change-reason-error="changeReasonError"
      :change-reason-valid="changeReasonValid"
      :create-pending="createPending"
      :display-name-valid="displayNameValid"
      :editing-current-version="editingCurrentVersion"
      :editing-rule-key="editingRuleKey"
      :form="form"
      :form-open="formOpen"
      :form-preview="formPreview"
      :form-preview-pending="formPreviewPending"
      :on-form-open-change="onFormOpenChange"
      :preview-form-sample="previewFormSample"
      :remove-segment="removeSegment"
      :segment-errors="segmentErrors"
      :sequence-count-error="sequenceCountError"
      :show-errors="showErrors"
      :submit-form="submitForm"
    />
  </BusinessLayout>
</template>
