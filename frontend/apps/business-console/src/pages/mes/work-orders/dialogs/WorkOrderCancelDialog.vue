<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import { readFaceText } from '@/utils/readFace'
import {
  NvAlertDialog,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
} from '@nerv-iip/ui'
import { XCircleIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useWorkOrderDetailPage } from '../useWorkOrderDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useWorkOrderDetailPage>>

defineProps<{
  cancelReasons: View['CANCEL_REASONS']
  reasonMaxLength: View['REASON_MAX_LENGTH']
  canSubmitCancel: View['canSubmitCancel']
  cancelContextItems: View['cancelContextItems']
  cancelForm: View['cancelForm']
  cancelPreviewError: View['cancelPreviewError']
  cancelPreviewPending: View['cancelPreviewPending']
  cancelWorkOrderPending: View['cancelWorkOrderPending']
  cancellableReceiptCount: View['cancellableReceiptCount']
  cancellableTaskCount: View['cancellableTaskCount']
  finalReasonLength: View['finalReasonLength']
  formatQuantity: View['formatQuantity']
  hasCompensation: View['hasCompensation']
  lineSideReturnRows: View['lineSideReturnRows']
  remarkMaxLength: View['remarkMaxLength']
  requiresRemark: View['requiresRemark']
  reservationRows: View['reservationRows']
  resolveSkuLabel: View['resolveSkuLabel']
  retryCancelPreview: View['retryCancelPreview']
  returnableQuantityOf: View['returnableQuantityOf']
  showReasonLengthHint: View['showReasonLengthHint']
  submitCancel: View['submitCancel']
  unreturnableNoLotRows: View['unreturnableNoLotRows']
  workOrderLabel: View['workOrderLabel']
}>()

const cancelOpen = defineModel<View['cancelOpen']>('cancelOpen', { required: true })
</script>

<template>
  <!-- 取消工单确认（含补偿预览区），A1 §2 破坏性动作原因必填 -->
  <NvAlertDialog v-model:open="cancelOpen">
    <NvAlertDialogContent class="sm:max-w-lg">
      <NvAlertDialogHeader>
        <NvAlertDialogTitle>取消工单 · {{ workOrderLabel }}</NvAlertDialogTitle>
        <!-- 破坏性动作：保留一句后果陈述，明细由下方补偿预览给出。 -->
        <NvAlertDialogDescription>
          取消后将释放预留、生成退料指引，并取消未完成的入库请求与工序任务，操作不可撤销。
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>

      <!-- 工单 / 状态 / 物料 / 计划数量由本页带出，只读呈现，取消时不需要再核对别处。 -->
      <CarriedContextSummary label="取消对象" :items="cancelContextItems" />

      <section aria-label="补偿预览" class="grid gap-3 rounded-lg border bg-muted/30 p-3 text-sm">
        <span class="font-semibold text-foreground">补偿预览</span>

        <div v-if="cancelPreviewPending" class="flex items-center gap-2 text-muted-foreground">
          <NvSpinner aria-hidden="true" />
          <span>正在加载补偿预览…</span>
        </div>
        <div
          v-else-if="cancelPreviewError"
          class="grid gap-2 rounded-md border border-destructive/40 bg-destructive/5 p-2"
        >
          <p class="text-destructive">
            补偿预览加载失败，为避免误操作已禁用确认。请重试后再确认取消。
          </p>
          <NvButton
            type="button"
            variant="outline"
            size="sm"
            class="justify-self-start"
            @click="retryCancelPreview"
          >
            重试
          </NvButton>
        </div>
        <template v-else>
          <div class="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <div class="rounded-md border bg-background p-2">
              <p class="text-xs text-muted-foreground">预留释放</p>
              <p class="text-lg font-semibold tabular-nums text-foreground">
                {{ reservationRows.length }}
              </p>
            </div>
            <div class="rounded-md border bg-background p-2">
              <p class="text-xs text-muted-foreground">退料指引</p>
              <p class="text-lg font-semibold tabular-nums text-foreground">
                {{ lineSideReturnRows.length }}
              </p>
            </div>
            <div class="rounded-md border bg-background p-2">
              <p class="text-xs text-muted-foreground">完工入库请求</p>
              <p class="text-lg font-semibold tabular-nums text-foreground">
                {{ cancellableReceiptCount }}
              </p>
            </div>
            <div class="rounded-md border bg-background p-2">
              <p class="text-xs text-muted-foreground">工序任务</p>
              <p class="text-lg font-semibold tabular-nums text-foreground">
                {{ cancellableTaskCount }}
              </p>
            </div>
          </div>

          <div
            v-if="unreturnableNoLotRows.length"
            class="grid gap-1 rounded-md border border-destructive/40 bg-destructive/5 p-2"
          >
            <p class="text-xs font-medium text-destructive">
              以下已收料申请缺少物料批次，无法退料；请先补录批次或消耗后再取消整单。
            </p>
            <ul class="max-h-24 divide-y overflow-y-auto rounded-md border bg-background">
              <li
                v-for="row in unreturnableNoLotRows"
                :key="`nolot-${row.requestId}`"
                class="flex items-center justify-between gap-2 px-2 py-1"
              >
                <span class="truncate">{{
                  resolveSkuLabel(row.materialCode ?? row.materialId)
                }}</span>
                <span class="shrink-0 tabular-nums">{{
                  formatQuantity(returnableQuantityOf(row))
                }}</span>
              </li>
            </ul>
          </div>

          <div v-if="reservationRows.length" class="grid gap-1">
            <p class="text-xs font-medium text-muted-foreground">将释放的预留（物料 / 数量）</p>
            <ul class="max-h-28 divide-y overflow-y-auto rounded-md border bg-background">
              <li
                v-for="row in reservationRows"
                :key="`res-${row.requestId}`"
                class="flex items-center justify-between gap-2 px-2 py-1"
              >
                <span class="truncate"
                  >{{ resolveSkuLabel(row.materialCode ?? row.materialId)
                  }}<span v-if="row.materialLotId" class="text-muted-foreground">
                    · {{ readFaceText(row.materialLotId, '未指定批次') }}</span
                  ></span
                >
                <span class="shrink-0 tabular-nums">{{
                  formatQuantity(row.requestedQuantity)
                }}</span>
              </li>
            </ul>
          </div>

          <div v-if="lineSideReturnRows.length" class="grid gap-1">
            <p class="text-xs font-medium text-muted-foreground">待退回线边物料（物料 / 数量）</p>
            <ul class="max-h-28 divide-y overflow-y-auto rounded-md border bg-background">
              <li
                v-for="row in lineSideReturnRows"
                :key="`ret-${row.requestId}`"
                class="flex items-center justify-between gap-2 px-2 py-1"
              >
                <span class="truncate"
                  >{{ resolveSkuLabel(row.materialCode ?? row.materialId)
                  }}<span v-if="row.materialLotId" class="text-muted-foreground">
                    · {{ readFaceText(row.materialLotId, '未指定批次') }}</span
                  ></span
                >
                <span class="shrink-0 tabular-nums">{{
                  formatQuantity(returnableQuantityOf(row))
                }}</span>
              </li>
            </ul>
          </div>

          <p v-if="!hasCompensation" class="text-muted-foreground">
            该工单当前无可释放的预留或待退回线边物料，取消仅流转工单状态。
          </p>
        </template>
      </section>

      <NvFieldGroup class="grid gap-3">
        <NvField>
          <NvFieldLabel for="cancel-reason">
            取消原因 <span class="text-destructive">*</span>
          </NvFieldLabel>
          <NvSelect v-model="cancelForm.reasonCode">
            <NvSelectTrigger id="cancel-reason" aria-label="取消原因">
              <NvSelectValue placeholder="选择取消原因" />
            </NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem
                v-for="option in cancelReasons"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </NvSelectItem>
            </NvSelectContent>
          </NvSelect>
        </NvField>
        <NvField>
          <NvFieldLabel for="cancel-remark">
            备注 <span v-if="requiresRemark" class="text-destructive">*</span>
          </NvFieldLabel>
          <NvInput
            id="cancel-remark"
            v-model="cancelForm.remark"
            :maxlength="remarkMaxLength"
            placeholder="补充取消说明"
          />
          <!-- 字数只在接近/超过上限时提示：平时是噪音，超限时才是决策信息（后端限 500 字）。 -->
          <p
            v-if="showReasonLengthHint"
            class="text-xs"
            :class="
              finalReasonLength > reasonMaxLength ? 'text-destructive' : 'text-muted-foreground'
            "
          >
            取消原因（含标签）共 {{ finalReasonLength }} / {{ reasonMaxLength }} 字符
          </p>
        </NvField>
      </NvFieldGroup>

      <NvAlertDialogFooter>
        <NvButton
          type="button"
          variant="outline"
          :disabled="cancelWorkOrderPending"
          @click="cancelOpen = false"
        >
          返回
        </NvButton>
        <NvButton
          type="button"
          variant="destructive"
          :disabled="!canSubmitCancel || cancelWorkOrderPending"
          @click="submitCancel"
        >
          <NvSpinner v-if="cancelWorkOrderPending" aria-hidden="true" />
          <XCircleIcon v-else aria-hidden="true" />
          确认取消工单
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
