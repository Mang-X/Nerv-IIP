<script setup lang="ts">
import PlanningSuggestionRejectDialog from './PlanningSuggestionRejectDialog.vue'
import {
  runHorizonLabel,
  planningStatus,
  suggestionTypeLabel,
  suggestionTypeTone,
  isAcceptableSuggestion,
  reasonLabel,
  sourceTypeLabel,
  isOpen,
  downstreamLabel,
  downstreamStatus,
  formatDate,
  formatQuantity,
  formatPercent,
  inputDegradationLabel,
  explanationRows,
} from './planningFormatters'
import type { BusinessConsolePlanningSuggestionItem } from '@nerv-iip/api-client'
import SingleOrderSchedulingDialog from '@/components/scheduling/SingleOrderSchedulingDialog.vue'
import { inlineErrorMessage } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvSpinner,
  NvStatusBadge,
  NvTabsContent,
} from '@nerv-iip/ui'
import { CalendarCogIcon, CheckIcon, XIcon } from '@lucide/vue'
import { hasBusinessContext } from '@/composables/shared/businessContextBinding'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { shallowRef, type ShallowUnwrapRef } from 'vue'
import type { usePlanningWorkbenchController } from './usePlanningWorkbenchController'

type View = ShallowUnwrapRef<ReturnType<typeof usePlanningWorkbenchController>>

defineProps<{
  acceptPlanningSuggestion: View['acceptPlanningSuggestion']
  acceptSuggestionPending: View['acceptSuggestionPending']
  acceptingSuggestionId: View['acceptingSuggestionId']
  canReadMrp: View['canReadMrp']
  canReadWorkOrders: View['canReadWorkOrders']
  canScheduleSingleOrder: View['canScheduleSingleOrder']
  canSubmitReject: View['canSubmitReject']
  downstreamRoute: View['downstreamRoute']
  downstreamStatuses: View['downstreamStatuses']
  focusPegging: View['focusPegging']
  focusedSuggestionId: View['focusedSuggestionId']
  hasPermission: View['hasPermission']
  hasScope: View['hasScope']
  latestCompletedRun: View['latestCompletedRun']
  mrpRuns: View['mrpRuns']
  mrpRunsError: View['mrpRunsError']
  mrpRunsPending: View['mrpRunsPending']
  openRejectDialog: View['openRejectDialog']
  rejectSuggestionPending: View['rejectSuggestionPending']
  returnLabel: View['returnLabel']
  router: View['router']
  runChoiceLabel: View['runChoiceLabel']
  runRequest: View['runRequest']
  skuLabel: View['skuLabel']
  sourceUnresolved: View['sourceUnresolved']
  submitRejectSuggestion: View['submitRejectSuggestion']
  successorRunLabel: View['successorRunLabel']
  suggestionColumns: View['suggestionColumns']
  suggestionFilters: View['suggestionFilters']
  suggestionPageRows: View['suggestionPageRows']
  suggestionScheduleHint: View['suggestionScheduleHint']
  suggestionStatusOptions: View['suggestionStatusOptions']
  suggestionTypeFilter: View['suggestionTypeFilter']
  suggestionTypeFilterOptions: View['suggestionTypeFilterOptions']
  suggestionWorkOrderId: View['suggestionWorkOrderId']
  suggestionsError: View['suggestionsError']
  suggestionsPending: View['suggestionsPending']
  visibleSuggestions: View['visibleSuggestions']
}>()

const suggestionRunChoice = defineModel<View['suggestionRunChoice']>('suggestionRunChoice', {
  required: true,
})

const suggestionPage = defineModel<View['suggestionPage']>('suggestionPage', { required: true })

const suggestionPageSize = defineModel<View['suggestionPageSize']>('suggestionPageSize', {
  required: true,
})

const scheduleTarget = defineModel<View['scheduleTarget']>('scheduleTarget', { required: true })

const rejectReason = defineModel<View['rejectReason']>('rejectReason', { required: true })

const rejectTarget = defineModel<View['rejectTarget']>('rejectTarget', { required: true })

const cancelTarget = defineModel<View['cancelTarget']>('cancelTarget', { required: true })

const evidenceRow = shallowRef<BusinessConsolePlanningSuggestionItem | null>(null)
const evidenceOpen = shallowRef(false)
let evidenceTrigger: HTMLElement | null = null

function openEvidence(row: BusinessConsolePlanningSuggestionItem, event: MouseEvent) {
  evidenceTrigger = event.currentTarget as HTMLElement
  evidenceRow.value = row
  evidenceOpen.value = true
}

function restoreEvidenceFocus(event: Event) {
  event.preventDefault()
  evidenceTrigger?.focus()
  evidenceTrigger = null
}
</script>

<template>
  <NvTabsContent v-if="canReadMrp" value="suggestions" class="grid min-w-0 grid-cols-1 gap-3">
    <div v-if="!sourceUnresolved" class="flex flex-wrap items-center gap-2">
      <NvSelect v-model="suggestionRunChoice">
        <NvSelectTrigger class="h-9 w-72" aria-label="建议运行"
          ><NvSelectValue placeholder="选择运行"
        /></NvSelectTrigger>
        <NvSelectContent>
          <NvSelectItem v-if="latestCompletedRun" value="latest"
            >最近完成 · {{ runHorizonLabel(latestCompletedRun) }}</NvSelectItem
          >
          <NvSelectItem
            v-for="run in mrpRuns.filter((item) => item.status?.toLowerCase() === 'completed')"
            :key="run.runId"
            :value="run.runId ?? ''"
            >{{ runChoiceLabel(run) }}</NvSelectItem
          >
        </NvSelectContent>
      </NvSelect>
      <NvSelect v-model="suggestionTypeFilter.type">
        <NvSelectTrigger class="h-9 w-44" aria-label="建议分型"
          ><NvSelectValue placeholder="全部类型"
        /></NvSelectTrigger>
        <NvSelectContent>
          <NvSelectItem v-for="o in suggestionTypeFilterOptions" :key="o.value" :value="o.value">{{
            o.label
          }}</NvSelectItem>
        </NvSelectContent>
      </NvSelect>
      <span class="text-sm text-muted-foreground"> 生产建议 → MES · 采购建议 → ERP </span>
      <div class="ms-auto flex items-center gap-2">
        <NvSelect v-model="suggestionFilters.status">
          <NvSelectTrigger class="h-9 w-32" aria-label="建议状态"
            ><NvSelectValue placeholder="建议状态"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="o in suggestionStatusOptions" :key="o.value" :value="o.value">{{
              o.label
            }}</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </div>
    </div>
    <div
      v-if="sourceUnresolved"
      role="status"
      class="grid justify-items-start gap-2 text-sm text-muted-foreground"
    >
      {{
        suggestionsError || mrpRunsError
          ? '来源读取失败，请刷新后重试。'
          : suggestionsPending || mrpRunsPending
            ? '正在定位来源建议…'
            : !hasBusinessContext(runRequest)
              ? '请先选择组织与环境。'
              : '当前可读范围内未定位到来源建议或运行。'
      }}
      <NvButton as-child size="sm" variant="outline">
        <RouterLink to="/planning">{{ returnLabel }}</RouterLink>
      </NvButton>
    </div>
    <NvDataTable
      v-else
      :key="focusedSuggestionId"
      :columns="suggestionColumns"
      :rows="suggestionPageRows"
      manual
      v-model:page="suggestionPage"
      :page-size="suggestionPageSize"
      @update:page-size="suggestionPageSize = Number($event)"
      :total-items="visibleSuggestions.length"
      :row-class="
        (row: BusinessConsolePlanningSuggestionItem) =>
          row.suggestionId === focusedSuggestionId ? 'bg-primary/10' : ''
      "
      row-key="suggestionId"
      :loading="suggestionsPending"
      :error="suggestionsError"
      :error-message="inlineErrorMessage(suggestionsError, '读取建议失败。')"
      :awaiting-scope="!hasScope"
      awaiting-scope-message="请先选择组织与环境。"
      :searchable="false"
      :column-settings="false"
      :empty-message="
        latestCompletedRun ? '该次运行在当前状态下没有计划建议。' : '尚无已完成的 MRP 运行。'
      "
    >
      <template #cell-suggestionType="{ row }">
        <NvStatusBadge
          :label="suggestionTypeLabel(row.suggestionType)"
          :tone="suggestionTypeTone(row.suggestionType)"
        />
      </template>
      <template #cell-skuCode="{ row }">
        <div class="flex flex-col gap-0.5 whitespace-normal wrap-anywhere">
          <span>{{ skuLabel(row.skuCode) }}</span>
          <span v-if="row.skuCode" class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
        </div>
      </template>
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.quantity, row.uomCode) }}</span></template
      >
      <template #cell-requiredDate="{ row }">{{ formatDate(row.requiredDate) }}</template>
      <template #cell-reasonCode="{ row }">
        <div class="grid justify-items-start gap-1 whitespace-normal">
          <NvStatusBadge
            v-if="row.netRequirementExplanation"
            :label="sourceTypeLabel(row.netRequirementExplanation.primarySourceType)"
            tone="info"
          />
          <span class="text-sm">{{ reasonLabel(row.reasonCode) }}</span>
          <NvButton
            v-if="row.netRequirementExplanation || row.suggestionId"
            size="sm"
            type="button"
            variant="ghost"
            class="px-1"
            aria-haspopup="dialog"
            @click="openEvidence(row, $event)"
            >查看净算依据</NvButton
          >
        </div>
      </template>
      <template #cell-downstream="{ row }">
        <div
          v-if="row.downstreamDocumentId"
          class="grid gap-1 text-sm whitespace-normal wrap-anywhere"
        >
          <div class="grid gap-1">
            <span>{{ downstreamLabel(row.downstreamService, row.downstreamDocumentType) }} ·</span>
            <NvButton
              v-if="suggestionWorkOrderId(row) && canReadWorkOrders"
              size="sm"
              type="button"
              variant="ghost"
              class="h-auto justify-start px-0 text-start whitespace-normal wrap-anywhere"
              @click="
                router.push(
                  downstreamRoute(
                    row.downstreamService,
                    row.downstreamDocumentType,
                    row.downstreamDocumentId,
                  ),
                )
              "
              >{{ row.downstreamDocumentId }}</NvButton
            >
            <span v-else>{{ row.downstreamDocumentId }}</span>
          </div>
          <NvStatusBadge
            v-if="
              row.suggestionType === 'planned-work-order' ||
              row.suggestionType === 'planned-purchase'
            "
            :label="
              row.suggestionId && downstreamStatuses[row.suggestionId]
                ? downstreamStatus(downstreamStatuses[row.suggestionId]).label
                : '状态读取中'
            "
            :tone="
              downstreamStatus(row.suggestionId ? downstreamStatuses[row.suggestionId] : undefined)
                .tone
            "
          />
        </div>
        <span v-else class="text-sm text-muted-foreground">未承接</span>
      </template>
      <template #cell-status="{ row }"
        ><div class="grid gap-1">
          <NvStatusBadge
            :label="planningStatus(row.status).label"
            :tone="planningStatus(row.status).tone"
          />
          <span v-if="row.supersededByRunId" class="text-xs text-muted-foreground">
            由
            {{ successorRunLabel(row.supersededByRunId) }} 替代
          </span>
        </div></template
      >
      <template #cell-actions="{ row }">
        <div
          v-if="isOpen(row.status) && hasPermission(P.planningSuggestionsManage)"
          class="flex flex-wrap items-center justify-end gap-1"
        >
          <NvButton
            v-if="isAcceptableSuggestion(row.suggestionType)"
            size="sm"
            type="button"
            variant="outline"
            :disabled="acceptingSuggestionId === row.suggestionId"
            @click="acceptPlanningSuggestion(row)"
          >
            <NvSpinner v-if="acceptingSuggestionId === row.suggestionId" aria-hidden="true" />
            <CheckIcon v-else aria-hidden="true" />
            接受
          </NvButton>
          <span v-else class="text-sm text-muted-foreground">异常待处理</span>
          <NvButton
            size="sm"
            type="button"
            variant="ghost"
            :aria-label="`拒绝计划建议 ${skuLabel(row.skuCode)}`"
            :disabled="rejectSuggestionPending"
            @click="openRejectDialog(row)"
          >
            <XIcon aria-hidden="true" />
            拒绝
          </NvButton>
        </div>
        <!-- 已承接成 MES 工单的行：可直接对这一张单排产（新建只含该单的方案）。 -->
        <div v-else-if="!isOpen(row.status)" class="flex flex-wrap items-center justify-end gap-1">
          <NvButton
            v-if="suggestionWorkOrderId(row)"
            size="sm"
            type="button"
            variant="outline"
            data-testid="planning-suggestion-schedule-single"
            :disabled="!canScheduleSingleOrder"
            :title="
              canScheduleSingleOrder
                ? `对工单 ${suggestionWorkOrderId(row)} 排产（新建只含该单的方案）`
                : '当前账号没有排产管理权限'
            "
            @click="scheduleTarget = row"
          >
            <CalendarCogIcon aria-hidden="true" />
            对该单排产
          </NvButton>
          <span v-else-if="suggestionScheduleHint(row)" class="text-sm text-muted-foreground">
            {{ suggestionScheduleHint(row) }}
          </span>
        </div>
      </template>
    </NvDataTable>

    <NvSheet v-model:open="evidenceOpen">
      <NvSheetContent
        size="lg"
        class="[&>[data-slot=nv-sheet-close]]:z-20"
        @close-auto-focus="restoreEvidenceFocus"
      >
        <NvSheetHeader>
          <NvSheetTitle>净算依据 · {{ skuLabel(evidenceRow?.skuCode) }}</NvSheetTitle>
          <NvSheetDescription>{{ reasonLabel(evidenceRow?.reasonCode) }}</NvSheetDescription>
        </NvSheetHeader>
        <div v-if="evidenceRow" class="grid gap-3">
          <div
            v-if="evidenceRow.netRequirementExplanation"
            class="grid gap-2 rounded-md border border-border/70 bg-muted/30 p-2 text-xs"
          >
            <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
              <span class="font-medium text-foreground">{{
                ['negative-availability', 'overdue-receipt'].includes(
                  evidenceRow.suggestionType ?? '',
                )
                  ? '例外说明'
                  : '净需求公式'
              }}</span>
              <span
                class="font-mono whitespace-pre-wrap wrap-anywhere tabular-nums text-muted-foreground"
                >{{
                  evidenceRow.netRequirementExplanation.formula?.replaceAll(
                    'scrap/yield',
                    '废品率 / 良率',
                  ) || '—'
                }}</span
              >
            </div>
            <div class="grid grid-cols-2 gap-1 sm:grid-cols-4">
              <div
                v-for="item in explanationRows(evidenceRow)"
                :key="item.label"
                class="min-w-0 rounded border border-border/60 bg-background px-2 py-1"
              >
                <div class="text-muted-foreground">{{ item.label }}</div>
                <div class="tabular-nums text-foreground">{{ item.value ?? 0 }}</div>
              </div>
            </div>
            <div class="flex flex-wrap gap-x-4 gap-y-1 text-muted-foreground">
              <span
                >废品率 {{ formatPercent(evidenceRow.netRequirementExplanation.scrapRate) }}</span
              >
              <span>良率 {{ formatPercent(evidenceRow.netRequirementExplanation.yieldRate) }}</span>
              <span
                v-if="
                  evidenceRow.netRequirementExplanation.primarySourceType === 'component' &&
                  (evidenceRow.netRequirementExplanation.scrapRate ||
                    evidenceRow.netRequirementExplanation.yieldRate !== 1)
                "
                >废品率 / 良率已计入组件毛需求</span
              >
              <span v-if="evidenceRow.netRequirementExplanation.uomConversions?.length"
                >单位 {{ evidenceRow.netRequirementExplanation.uomConversions.join('；') }}</span
              >
              <span v-if="evidenceRow.netRequirementExplanation.degradationSources?.length"
                >退化
                {{
                  inputDegradationLabel(evidenceRow.netRequirementExplanation.degradationSources)
                }}</span
              >
            </div>
          </div>
          <p v-if="!evidenceRow.netRequirementExplanation" class="text-muted-foreground">
            此建议未保存净算明细。
          </p>
          <NvButton
            v-if="evidenceRow.suggestionId && evidenceRow.runId"
            size="sm"
            type="button"
            variant="outline"
            class="justify-self-start"
            @click="
              evidenceOpen = false;
              focusPegging(evidenceRow)
            "
            >定位追溯</NvButton
          >
        </div>
      </NvSheetContent>
    </NvSheet>

    <SingleOrderSchedulingDialog
      v-if="scheduleTarget"
      :open="true"
      :work-order-id="suggestionWorkOrderId(scheduleTarget)"
      :context-label="`计划建议 · ${skuLabel(scheduleTarget.skuCode)}`"
      @update:open="
        (value: boolean) => {
          if (!value) scheduleTarget = null
        }
      "
    />

    <PlanningSuggestionRejectDialog
      v-if="hasPermission(P.planningSuggestionsManage)"
      v-model:reject-target="rejectTarget"
      v-model:reject-reason="rejectReason"
      :sku-label="skuLabel"
      :reject-suggestion-pending="rejectSuggestionPending"
      :can-submit-reject="canSubmitReject"
      :submit-reject-suggestion="submitRejectSuggestion"
    />
    <NvDialog
      v-if="hasPermission(P.planningSuggestionsManage)"
      :open="!!cancelTarget"
      @update:open="
        (open) => {
          if (!open) cancelTarget = null
        }
      "
    >
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>确认取消在途单据</NvDialogTitle>
          <NvDialogDescription
            >接受此建议会向 ERP 或 MES 下发取消命令。请确认取消
            {{ skuLabel(cancelTarget?.skuCode) }} 的在途供应。</NvDialogDescription
          >
        </NvDialogHeader>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="cancelTarget = null">返回</NvButton>
          <NvButton
            type="button"
            :disabled="acceptSuggestionPending"
            @click="cancelTarget && acceptPlanningSuggestion(cancelTarget)"
            >确认取消并接受</NvButton
          >
        </NvDialogFooter>
      </NvDialogContent>
    </NvDialog>
  </NvTabsContent>
</template>
