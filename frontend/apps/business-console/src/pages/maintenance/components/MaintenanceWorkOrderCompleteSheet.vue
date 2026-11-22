<script setup lang="ts">
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import { CURRENCY_OPTIONS } from '@/data/currencyReference'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import {
  NvButton,
  NvEntityPicker,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSearchSelect,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  NvSpinner,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, Trash2Icon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useMaintenanceWorkOrdersPage } from '../useMaintenanceWorkOrdersPage'

type View = ShallowUnwrapRef<ReturnType<typeof useMaintenanceWorkOrdersPage>>

defineProps<{
  addSpareRow: View['addSpareRow']
  applySpareRowSku: View['applySpareRowSku']
  autoSparePartCost: View['autoSparePartCost']
  canManageWorkOrders: View['canManageWorkOrders']
  completeCarriedItems: View['completeCarriedItems']
  completeErrorMessage: View['completeErrorMessage']
  completeForm: View['completeForm']
  completeReasonMessage: View['completeReasonMessage']
  completeReasonOptions: View['completeReasonOptions']
  completeReasonState: View['completeReasonState']
  completeReasonsTotal: View['completeReasonsTotal']
  completeSiteCode: View['completeSiteCode']
  completeTarget: View['completeTarget']
  completeWorkOrderPending: View['completeWorkOrderPending']
  openReasonCreate: View['openReasonCreate']
  removeSpareRow: View['removeSpareRow']
  resultOptions: View['resultOptions']
  round2: View['round2']
  spareRows: View['spareRows']
  submitComplete: View['submitComplete']
  uomOptions: View['uomOptions']
  uomsPending: View['uomsPending']
  workOrderNo: View['workOrderNo']
}>()

const completeOpen = defineModel<View['completeOpen']>('completeOpen', { required: true })

const completeReasonKeyword = defineModel<View['completeReasonKeyword']>('completeReasonKeyword', {
  required: true,
})

const sparePartCostDisplay = defineModel<View['sparePartCostDisplay']>('sparePartCostDisplay', {
  required: true,
})
</script>

<template>
  <NvSheet v-model:open="completeOpen">
    <NvSheetContent class="flex w-full flex-col overflow-y-auto sm:max-w-xl">
      <NvSheetHeader>
        <NvSheetTitle>完成维护工单</NvSheetTitle>
        <!-- 工单事实已在下方只读区完整呈现；此处仅供读屏播报。 -->
        <NvSheetDescription class="sr-only">
          {{ completeTarget ? workOrderNo(completeTarget) : '完成维护工单' }} 的完工登记。
        </NvSheetDescription>
      </NvSheetHeader>
      <form class="grid gap-4 px-4 pb-4" @submit.prevent="submitComplete">
        <CarriedContextSummary label="完工工单" :items="completeCarriedItems" />

        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="mwo-result">维护结果</NvFieldLabel>
            <NvSearchSelect
              id="mwo-result"
              v-model="completeForm.result"
              :options="resultOptions"
              aria-label="维护结果"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-reason">停机原因</NvFieldLabel>
            <NvEntityPicker
              id="mwo-reason"
              v-model="completeForm.downtimeReasonCode"
              v-model:search="completeReasonKeyword"
              title="停机原因"
              :options="completeReasonOptions"
              :server-search="true"
              :total-count="completeReasonsTotal"
              :loading="completeReasonState === 'loading'"
              :empty-text="completeReasonMessage"
              placeholder="请选择停机原因"
              aria-label="停机原因"
            />
            <NvButton
              v-if="canManageWorkOrders"
              type="button"
              variant="link"
              size="sm"
              class="h-auto justify-start px-0"
              @click="openReasonCreate('complete')"
            >
              没有合适的原因？新增停机原因
            </NvButton>
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-minutes">停机时长（分钟）</NvFieldLabel>
            <NvInput
              id="mwo-minutes"
              v-model="completeForm.downtimeMinutes"
              type="number"
              min="0"
              step="1"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-actual-labor">实际工时（分钟）</NvFieldLabel>
            <NvInput
              id="mwo-actual-labor"
              v-model="completeForm.actualLaborMinutes"
              type="number"
              min="0"
              step="1"
              placeholder="可选"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-actual-technician">实际执行技师</NvFieldLabel>
            <!-- #897 完工登记实际技师；keep-out-of-range 保留预填的建单指派技师（即便不在当前检索页）。 -->
            <WorkerSelect
              id="mwo-actual-technician"
              v-model="completeForm.actualTechnicianUserId"
              keep-out-of-range
              placeholder="请选择实际执行技师"
            />
          </NvField>
        </NvFieldGroup>

        <div class="grid gap-2">
          <div class="flex items-center justify-between">
            <span class="text-sm font-medium">更换备件</span>
            <NvButton type="button" variant="outline" size="sm" @click="addSpareRow">
              <PlusIcon aria-hidden="true" />
              添加一行
            </NvButton>
          </div>
          <!-- 列宽按**抽屉的 512px** 算，不是按视口。`sm:` 是视口断点：视口 1366px 时
               规则照样生效，可这一行实际待在 md 档抽屉里——原来的固定列
               5rem+8rem+6rem=19rem，加间距与删除按钮吃掉约 368px，留给 `1fr` 的物料
               只剩 60 来 px，选择器被压成「选…」，最该看清的字段反而最窄。
               收窄固定列并给物料 minmax 下限：512px 下物料仍有约 132px，
               容器更宽时它继续吃掉多余空间。 -->
          <div
            v-for="row in spareRows"
            :key="row.id"
            :data-testid="`spare-row-${row.id}`"
            class="grid items-end gap-2 rounded-md border p-3 sm:grid-cols-[minmax(7rem,1fr)_4rem_7rem_5rem_auto]"
          >
            <NvField>
              <NvFieldLabel :for="`spare-sku-${row.id}`">物料</NvFieldLabel>
              <DirectoryPicker
                :id="`spare-sku-${row.id}`"
                :model-value="row.skuCode"
                directory-type="material"
                creatable
                placeholder="选择备件物料"
                aria-label="备件物料"
                @update:model-value="
                  (value: string) => {
                    row.skuCode = value
                    applySpareRowSku(row)
                  }
                "
              />
            </NvField>
            <NvField>
              <NvFieldLabel :for="`spare-qty-${row.id}`">数量</NvFieldLabel>
              <NvInput
                :id="`spare-qty-${row.id}`"
                v-model="row.quantity"
                type="number"
                min="1"
                step="1"
              />
            </NvField>
            <NvField>
              <NvFieldLabel :for="`spare-uom-${row.id}`">单位</NvFieldLabel>
              <NvEntityPicker
                :id="`spare-uom-${row.id}`"
                v-model="row.uomCode"
                :options="uomOptions"
                title="选择单位"
                placeholder="跟随物料"
                empty-text="暂无计量单位，请先在基础数据维护单位"
                :loading="uomsPending"
                clearable
                aria-label="备件单位"
              />
            </NvField>
            <NvField>
              <NvFieldLabel :for="`spare-cost-${row.id}`">单价</NvFieldLabel>
              <NvInput
                :id="`spare-cost-${row.id}`"
                v-model="row.unitCost"
                type="number"
                min="0"
                step="any"
                placeholder="可选"
              />
            </NvField>
            <NvButton
              type="button"
              variant="ghost"
              size="icon"
              aria-label="移除该备件"
              @click="removeSpareRow(row.id)"
            >
              <Trash2Icon aria-hidden="true" />
            </NvButton>
            <!-- 领出库位：只列设备所在工厂下已登记的库位，库存按工厂 + 库位扣减（#3902）。 -->
            <NvField class="sm:col-span-full">
              <NvFieldLabel :for="`spare-location-${row.id}`">领出库位</NvFieldLabel>
              <DirectoryPicker
                :id="`spare-location-${row.id}`"
                v-model="row.locationCode"
                directory-type="location"
                creatable
                :form-site-code="completeSiteCode"
                site-missing-text="设备未登记所属工厂，请先在设备台账维护所属工厂"
                placeholder="选择领出库位"
                aria-label="领出库位"
              />
            </NvField>
          </div>
        </div>

        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="mwo-spare-cost">备件成本汇总</NvFieldLabel>
            <NvInput
              id="mwo-spare-cost"
              v-model="sparePartCostDisplay"
              type="number"
              min="0"
              step="any"
              :placeholder="`自动合计 ${round2(autoSparePartCost)}`"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-external-cost">外委费用</NvFieldLabel>
            <NvInput
              id="mwo-external-cost"
              v-model="completeForm.externalServiceCostAmount"
              type="number"
              min="0"
              step="any"
              placeholder="可选"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="mwo-currency">币种</NvFieldLabel>
            <NvSearchSelect
              id="mwo-currency"
              v-model="completeForm.costCurrencyCode"
              :options="CURRENCY_OPTIONS"
              search-placeholder="搜索币种代码或名称"
              aria-label="币种"
            />
          </NvField>
        </NvFieldGroup>

        <NvFieldError v-if="completeErrorMessage" :errors="[completeErrorMessage]" />

        <NvSheetFooter class="px-0">
          <NvButton type="button" variant="outline" @click="completeOpen = false">取消</NvButton>
          <NvButton type="submit" :disabled="completeWorkOrderPending">
            <NvSpinner v-if="completeWorkOrderPending" aria-hidden="true" />
            <CheckCircle2Icon v-else aria-hidden="true" />
            完成工单
          </NvButton>
        </NvSheetFooter>
      </form>
    </NvSheetContent>
  </NvSheet>
</template>
