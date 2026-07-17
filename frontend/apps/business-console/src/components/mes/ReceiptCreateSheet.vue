<script setup lang="ts">
import {
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
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  Spinner,
} from '@nerv-iip/ui'
import { PackageCheckIcon, RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'

import { useReceiptCreateForm } from '@/composables/mes/useReceiptCreateForm'

// 路由页只负责编排：传入工单上下文与开合，登记表单状态/提交/产出批次全部封装在此组件（Vue best-practices §2）。
const props = defineProps<{
  open: boolean
  organizationId: string
  environmentId: string
  workOrderId: string
  skuId: string
  initialQuantity?: string
}>()
const emit = defineEmits<{
  'update:open': [value: boolean]
  created: []
}>()

const openModel = computed({
  get: () => props.open,
  set: (value: boolean) => emit('update:open', value),
})

const {
  form,
  producedLots,
  producedLotsPending,
  producedLotsError,
  refreshProducedLots,
  producedLotPlaceholder,
  selectedLot,
  canSubmit,
  showErrors,
  invalid,
  createReceiptRequestPending,
  submit,
} = useReceiptCreateForm(
  () => ({
    organizationId: props.organizationId,
    environmentId: props.environmentId,
    workOrderId: props.workOrderId,
    skuId: props.skuId,
    initialQuantity: props.initialQuantity,
  }),
  { onCreated: () => emit('created') },
)

function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
</script>

<template>
  <NvSheet v-model:open="openModel">
    <NvSheetContent class="w-full overflow-y-auto sm:max-w-xl">
      <NvSheetHeader>
        <NvSheetTitle>登记完工入库</NvSheetTitle>
        <NvSheetDescription
          >把完工成品登记入库。工单与成品由报工完成或工单详情带出，选择产出批次后确认入库数量、单位成本和单位。</NvSheetDescription
        >
      </NvSheetHeader>

      <!-- 结果一律走 toast（成功/失败/超量均 notifySuccess·notifyError）：Sheet 内不留常驻结果条。
           成功后重置表单留在原地支持高频连录，失败保持打开可修正重提。 -->
      <form class="grid content-start gap-4 p-4" @submit.prevent="submit">
        <NvFieldGroup class="grid gap-3">
          <NvField>
            <NvFieldLabel for="receipt-work-order">工单号</NvFieldLabel>
            <NvInput id="receipt-work-order" :model-value="workOrderId" readonly required />
          </NvField>
          <NvField>
            <NvFieldLabel for="receipt-sku">成品</NvFieldLabel>
            <NvInput id="receipt-sku" :model-value="skuId" readonly required />
          </NvField>
          <NvField>
            <NvFieldLabel for="receipt-produced-lot">产出批次</NvFieldLabel>
            <NvSelect
              v-model="form.producedLotNo"
              :disabled="producedLotsPending || producedLots.length === 0"
            >
              <NvSelectTrigger
                id="receipt-produced-lot"
                class="w-full"
                aria-label="选择产出批次"
                :data-invalid="showErrors && invalid.producedLotNo ? '' : undefined"
              >
                <NvSelectValue :placeholder="producedLotPlaceholder" />
              </NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="lot in producedLots"
                  :key="lot.producedLotNo"
                  :value="lot.producedLotNo"
                  >{{ lot.producedLotNo }}（剩余可入库
                  {{ formatQuantity(lot.remainingQuantity) }}）</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
            <!-- 加载失败（网络/权限/服务）：区别于真实空态，给出重试出口而非误显示为「暂无」被永久拦截。 -->
            <div
              v-if="!producedLotsPending && producedLotsError"
              class="flex items-center gap-2 text-xs text-destructive"
              role="alert"
            >
              <span>产出批次加载失败，请重试。</span>
              <NvButton size="sm" type="button" variant="outline" @click="refreshProducedLots">
                <RefreshCwIcon aria-hidden="true" />
                重试
              </NvButton>
            </div>
            <!-- 后端强制引用工单真实产出批次：无产出报工时明确引导先报工，而非让操作员盲提交后才 500。 -->
            <p
              v-else-if="!producedLotsPending && producedLots.length === 0"
              class="text-xs text-muted-foreground"
            >
              该工单暂无可入库的产出批次，请先在报工中登记产出（良品）后再登记入库。
            </p>
          </NvField>
          <NvField>
            <NvFieldLabel for="receipt-quantity">入库数量</NvFieldLabel>
            <NvInput
              id="receipt-quantity"
              v-model="form.quantity"
              inputmode="decimal"
              min="0.000001"
              step="0.000001"
              :max="selectedLot?.remainingQuantity"
              required
              type="number"
              :data-invalid="showErrors && invalid.quantity ? '' : undefined"
            />
            <!-- 剩余可入库量提示：数量不得超过所选批次剩余（后端按批次上限拒绝，前端闭环）。 -->
            <p v-if="selectedLot" class="text-xs text-muted-foreground">
              该批次剩余可入库 {{ formatQuantity(selectedLot.remainingQuantity) }}。
            </p>
          </NvField>
          <NvField>
            <NvFieldLabel for="receipt-unit-cost">单位成本</NvFieldLabel>
            <NvInput
              id="receipt-unit-cost"
              v-model="form.unitCost"
              inputmode="decimal"
              min="0.000001"
              step="0.000001"
              required
              type="number"
              :data-invalid="showErrors && invalid.unitCost ? '' : undefined"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="receipt-uom">单位</NvFieldLabel>
            <NvInput
              id="receipt-uom"
              v-model="form.uomCode"
              required
              :data-invalid="showErrors && invalid.uomCode ? '' : undefined"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="receipt-requested-at">登记时间</NvFieldLabel>
            <NvInput
              id="receipt-requested-at"
              v-model="form.requestedAtUtc"
              required
              type="datetime-local"
              :data-invalid="showErrors && invalid.requestedAtUtc ? '' : undefined"
            />
          </NvField>
        </NvFieldGroup>

        <!-- 点提交才标红：必填/超量未过给顶部汇总 + 字段红框，不发请求（create-dialog 硬规则）。 -->
        <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
          {{
            showErrors && invalid.quantity && selectedLot
              ? '入库数量需大于 0 且不超过该批次剩余可入库量（已标红）。'
              : '请完整填写带 * 的必填项（已标红）。'
          }}
        </p>

        <NvSheetFooter>
          <NvButton type="button" variant="outline" @click="openModel = false">取消</NvButton>
          <NvButton type="submit" :disabled="createReceiptRequestPending">
            <Spinner v-if="createReceiptRequestPending" aria-hidden="true" />
            <PackageCheckIcon v-else aria-hidden="true" />
            提交入库登记
          </NvButton>
        </NvSheetFooter>
      </form>
    </NvSheetContent>
  </NvSheet>
</template>
