<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import SourceDocumentPicker from '@/components/business/SourceDocumentPicker.vue'
import {
  NvButton,
  NvCombobox,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvEntityPicker,
  NvInput,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, PlusIcon, Trash2Icon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useInspectionPage } from '../useInspectionPage'

type View = ShallowUnwrapRef<ReturnType<typeof useInspectionPage>>

defineProps<{
  SOURCE_DOCUMENT_KINDS: View['SOURCE_DOCUMENT_KINDS']
  addCharacteristicRow: View['addCharacteristicRow']
  canCreateRecord: View['canCreateRecord']
  changeSourceType: View['changeSourceType']
  characteristicOptions: View['characteristicOptions']
  characteristicSuggestions: View['characteristicSuggestions']
  characteristicsPlanId: View['characteristicsPlanId']
  createInspectionRecordPending: View['createInspectionRecordPending']
  isInspectionTaskFlow: View['isInspectionTaskFlow']
  isOperationInspectionFlow: View['isOperationInspectionFlow']
  isVariableLine: View['isVariableLine']
  onCharacteristicCodeChange: View['onCharacteristicCodeChange']
  operationContext: View['operationContext']
  operationContextMessage: View['operationContextMessage']
  planCatalog: View['planCatalog']
  planCharacteristicsError: View['planCharacteristicsError']
  planCharacteristicsPending: View['planCharacteristicsPending']
  reasonCatalog: View['reasonCatalog']
  recordContextItems: View['recordContextItems']
  recordForm: View['recordForm']
  refreshPlanCharacteristics: View['refreshPlanCharacteristics']
  removeCharacteristicRow: View['removeCharacteristicRow']
  requiresDispositionReason: View['requiresDispositionReason']
  submitBlockers: View['submitBlockers']
  submitInspectionRecord: View['submitInspectionRecord']
  unitDisplay: View['unitDisplay']
  unitPolicyFor: View['unitPolicyFor']
  uomCatalog: View['uomCatalog']
}>()

const recordSheetOpen = defineModel<View['recordSheetOpen']>('recordSheetOpen', { required: true })

const recordPlanModel = defineModel<View['recordPlanModel']>('recordPlanModel', { required: true })
</script>

<template>
  <NvDialog v-model:open="recordSheetOpen">
    <!-- 高度不再由调用点写死：`NvDialogContent` 已改成遮罩层滚动、本体不定高。
         原先 `max-h-[85vh] overflow-y-auto` 把弹框锁在 765px（900px 视口），
         校验面板一 sticky 就压住第二行特性输入区、第三行「行程」被顶出可视区（#1418）。 -->
    <NvDialogContent class="sm:max-w-3xl">
      <NvDialogHeader>
        <NvDialogTitle>创建检验记录</NvDialogTitle>
        <!-- 检验对象由下方只读区或来源字段呈现；此处仅供读屏播报。 -->
        <NvDialogDescription class="sr-only">
          检验对象：来源单据 {{ recordForm.sourceDocumentId || '未指定' }}，物料
          {{ recordForm.skuCode }}。
        </NvDialogDescription>
      </NvDialogHeader>
      <form class="grid content-start gap-4" @submit.prevent="submitInspectionRecord">
        <!-- 从待检任务行进入：来源单据 / 物料 / 数量 / 批次 全部由任务带出，只读呈现。 -->
        <CarriedContextSummary
          v-if="isInspectionTaskFlow || isOperationInspectionFlow"
          label="检验对象"
          :items="recordContextItems"
        />

        <div
          v-if="isOperationInspectionFlow && operationContextMessage"
          class="flex items-center gap-2 text-sm"
          aria-live="polite"
        >
          <NvSpinner v-if="operationContext.pending.value" aria-hidden="true" />
          {{ operationContextMessage }}
          <NvButton
            v-if="!operationContext.pending.value"
            type="button"
            variant="outline"
            size="sm"
            @click="operationContext.refresh"
          >
            重试检验对象
          </NvButton>
        </div>
        <NvFieldGroup v-if="!isInspectionTaskFlow" class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="record-plan">检验方案</NvFieldLabel>
            <NvEntityPicker
              id="record-plan"
              v-model="recordPlanModel"
              :options="planCatalog.inspectionPlanOptions.value"
              title="选择检验方案"
              placeholder="选择检验方案"
              empty-text="当前范围内没有检验方案"
              :loading="planCatalog.inspectionPlansPending.value"
              clearable
              aria-label="检验方案"
            />
          </NvField>
          <NvField v-if="!isOperationInspectionFlow">
            <NvFieldLabel>来源类型</NvFieldLabel>
            <NvSelect :model-value="recordForm.sourceType" @update:model-value="changeSourceType">
              <NvSelectTrigger aria-label="来源类型"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="operation">工序</NvSelectItem>
                <NvSelectItem value="receiving">收货</NvSelectItem>
                <NvSelectItem value="final">终检</NvSelectItem>
                <NvSelectItem value="maintenance">维修</NvSelectItem>
                <NvSelectItem value="customer-return">客户退货</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField v-if="!isOperationInspectionFlow">
            <NvFieldLabel>来源服务</NvFieldLabel>
            <NvSelect v-model="recordForm.sourceService">
              <NvSelectTrigger aria-label="来源服务"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="mes-operation">MES 工序</NvSelectItem>
                <NvSelectItem value="inventory">库存</NvSelectItem>
                <NvSelectItem value="wms">WMS</NvSelectItem>
                <NvSelectItem value="mes">MES</NvSelectItem>
                <NvSelectItem value="erp">ERP</NvSelectItem>
                <NvSelectItem value="maintenance">维修</NvSelectItem>
                <NvSelectItem value="purchase-receipt">采购收货</NvSelectItem>
                <NvSelectItem value="customer-return">客户退货</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField v-if="!isOperationInspectionFlow">
            <NvFieldLabel for="record-source-document">来源单据</NvFieldLabel>
            <SourceDocumentPicker
              id="record-source-document"
              v-model="recordForm.sourceDocumentId"
              :kind="SOURCE_DOCUMENT_KINDS[recordForm.sourceType]"
            />
          </NvField>
          <NvField v-if="!isOperationInspectionFlow">
            <NvFieldLabel for="record-sku">SKU</NvFieldLabel>
            <DirectoryPicker
              id="record-sku"
              v-model="recordForm.skuCode"
              directory-type="material"
              creatable
              placeholder="选择 SKU"
              aria-label="SKU"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="record-quantity">检验数量</NvFieldLabel>
            <NvInput
              id="record-quantity"
              v-model="recordForm.inspectedQuantity"
              inputmode="decimal"
              min="0.000001"
              required
              step="any"
              type="number"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="record-batch">批次</NvFieldLabel>
            <DirectoryPicker
              id="record-batch"
              v-model="recordForm.batchNo"
              directory-type="batch"
              :sku-code="recordForm.skuCode"
              clearable
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="record-serial">序列号</NvFieldLabel>
            <DirectoryPicker
              id="record-serial"
              v-model="recordForm.serialNo"
              directory-type="serial"
              :sku-code="recordForm.skuCode"
              clearable
            />
          </NvField>
        </NvFieldGroup>

        <div class="grid gap-2">
          <div class="flex items-center justify-between">
            <h3 class="text-sm font-semibold text-foreground">检验特性</h3>
            <NvButton size="sm" variant="outline" type="button" @click="addCharacteristicRow">
              <PlusIcon aria-hidden="true" />
              添加行
            </NvButton>
          </div>
          <p
            v-if="planCharacteristicsError"
            class="flex items-center gap-2 text-sm text-destructive"
            role="alert"
          >
            检验特性与规格加载失败，请重试后再检验。
            <NvButton size="sm" variant="outline" type="button" @click="refreshPlanCharacteristics">
              重试
            </NvButton>
          </p>
          <p
            v-else-if="planCharacteristicsPending"
            class="text-sm text-muted-foreground"
            role="status"
          >
            正在加载检验特性与规格…
          </p>
          <p
            v-else-if="characteristicsPlanId && characteristicSuggestions.length"
            class="text-sm text-muted-foreground"
          >
            特性编码建议来自该检验方案的特性清单，也可直接录入计划外特性。
          </p>
          <p v-else-if="characteristicsPlanId" class="text-sm text-muted-foreground">
            该检验方案暂无特性清单，请直接录入特性编码。
          </p>
          <div class="grid gap-2">
            <div
              v-for="(line, index) in recordForm.resultLines"
              :key="index"
              class="grid gap-2 rounded-lg border p-3 md:grid-cols-[1fr_140px_1fr_110px_auto]"
            >
              <NvField>
                <NvFieldLabel :for="`characteristic-code-${index}`">检验特性</NvFieldLabel>
                <!-- 方案有特性清单时用「只能选」的选择器：框里显中文特性名，英文编码只作副信息；
                     清单为空（计划外特性）才退回自由录入的编码输入框。 -->
                <NvSearchSelect
                  v-if="characteristicOptions.length"
                  :id="`characteristic-code-${index}`"
                  :model-value="line.characteristicCode"
                  :options="characteristicOptions"
                  placeholder="选择检验特性"
                  search-placeholder="搜索特性名称 / 编码…"
                  empty-text="特性清单中无匹配项"
                  :aria-label="`第 ${index + 1} 个检验特性`"
                  @update:model-value="(value) => onCharacteristicCodeChange(line, value)"
                />
                <NvCombobox
                  v-else
                  :id="`characteristic-code-${index}`"
                  :model-value="line.characteristicCode"
                  :suggestions="characteristicSuggestions"
                  placeholder="录入特性编码"
                  empty-text="特性清单中无匹配项"
                  @update:model-value="(value) => onCharacteristicCodeChange(line, value)"
                />
                <NvFieldDescription v-if="line.characteristicCode">
                  编码：{{ line.characteristicCode }}
                </NvFieldDescription>
              </NvField>
              <NvField>
                <NvFieldLabel>结果</NvFieldLabel>
                <NvSelect v-model="line.result">
                  <NvSelectTrigger :aria-label="`第 ${index + 1} 个特性结果`"
                    ><NvSelectValue
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem value="passed">合格</NvSelectItem>
                    <NvSelectItem value="failed">不合格</NvSelectItem>
                    <NvSelectItem value="conditional-release">让步放行</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <!-- 计量型特性录数值测量值（提交 measuredValue，语义对齐 PDA 数字键盘）；
                   计数型仍录文本实测值（observedValue）。 -->
              <NvField v-if="isVariableLine(line)">
                <NvFieldLabel :for="`measured-value-${index}`">
                  测量值{{ line.unitCode ? `（${line.unitCode}）` : '' }}
                </NvFieldLabel>
                <NvInput
                  :id="`measured-value-${index}`"
                  v-model="line.measuredValue"
                  type="number"
                  inputmode="decimal"
                  step="any"
                  required
                />
                <NvFieldDescription v-if="line.specification">
                  规格：{{ line.specification }}
                </NvFieldDescription>
              </NvField>
              <NvField v-else>
                <NvFieldLabel :for="`observed-value-${index}`">实测值</NvFieldLabel>
                <NvInput :id="`observed-value-${index}`" v-model="line.observedValue" required />
                <NvFieldDescription v-if="line.specification">
                  规格：{{ line.specification }}
                </NvFieldDescription>
              </NvField>
              <!-- 单位由特性的量纲派生：计划内特性只读带出，计划外特性才回落到计量单位目录。 -->
              <NvField>
                <NvFieldLabel :for="`unit-code-${index}`">单位</NvFieldLabel>
                <p
                  v-if="unitPolicyFor(line).mode === 'derived'"
                  :id="`unit-code-${index}`"
                  class="flex h-9 items-center text-sm font-medium text-foreground"
                >
                  {{ unitDisplay(line.unitCode) }}
                </p>
                <p
                  v-else-if="unitPolicyFor(line).mode === 'dimensionless'"
                  :id="`unit-code-${index}`"
                  class="flex h-9 items-center text-sm text-muted-foreground"
                >
                  无量纲
                </p>
                <NvEntityPicker
                  v-else
                  :id="`unit-code-${index}`"
                  v-model="line.unitCode"
                  :options="uomCatalog.uomOptions.value"
                  title="选择计量单位"
                  placeholder="选择单位"
                  :loading="uomCatalog.uomsPending.value"
                  clearable
                  :aria-label="`第 ${index + 1} 个特性单位`"
                />
                <NvFieldDescription v-if="unitPolicyFor(line).mode === 'derived'">
                  来自检验方案，不可更改
                </NvFieldDescription>
              </NvField>
              <NvField class="md:col-span-2">
                <NvFieldLabel :for="`defect-reason-${index}`">缺陷原因</NvFieldLabel>
                <NvSearchSelect
                  :id="`defect-reason-${index}`"
                  v-model="line.defectReason"
                  :options="reasonCatalog.defectReasonOptions.value"
                  placeholder="选择缺陷原因"
                  empty-text="原因码目录里没有匹配项"
                  :loading="reasonCatalog.reasonsPending.value"
                  :aria-label="`第 ${index + 1} 个特性缺陷原因`"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`defect-quantity-${index}`">缺陷数量</NvFieldLabel>
                <NvInput
                  :id="`defect-quantity-${index}`"
                  v-model="line.defectQuantity"
                  inputmode="decimal"
                  type="number"
                />
              </NvField>
              <div class="flex items-end justify-end">
                <NvButton
                  size="icon-sm"
                  variant="ghost"
                  type="button"
                  @click="removeCharacteristicRow(index)"
                >
                  <Trash2Icon />
                  <span class="sr-only">移除检验特性</span>
                </NvButton>
              </div>
            </div>
          </div>
        </div>

        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField>
            <NvFieldLabel for="record-disposition">
              处置原因
              <span v-if="requiresDispositionReason" class="text-destructive">*</span>
            </NvFieldLabel>
            <!-- 处置原因是记录详情直接展示的处置结论，所以存人读原因名称、不是原因编码。 -->
            <NvSearchSelect
              id="record-disposition"
              v-model="recordForm.dispositionReason"
              :options="reasonCatalog.dispositionReasonOptions.value"
              placeholder="选择处置原因"
              empty-text="原因码目录里没有匹配项"
              :loading="reasonCatalog.reasonsPending.value"
              aria-label="处置原因"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="record-files">附件</NvFieldLabel>
            <NvInput
              id="record-files"
              v-model="recordForm.dispositionAttachmentFileIds"
              placeholder="多个附件用逗号分隔"
            />
          </NvField>
        </NvFieldGroup>

        <!-- 按钮为什么灰，就在按钮边上说清楚：禁用态与这份清单同源，不存在「无红字却提交不了」。
             贴着页脚 sticky，弹框再高也不会把「还差什么」滚出视野（走查 #85 同处）。
             这一整块（校验清单 + 操作按钮）才是 sticky 单元，所以负外边距铺到弹框
             `p-6` 的内边距边缘、底色取 `bg-card` 与弹框同色；此前用 `bg-background`
             会露出一条色差，两侧也会漏出滚动内容（#1418）。 -->
        <div
          class="sticky bottom-0 z-10 -mx-6 -mb-6 grid gap-3 rounded-b-xl bg-card px-6 pt-3 pb-6"
        >
          <div
            v-if="submitBlockers.length"
            id="record-submit-blockers"
            class="grid gap-1 rounded-lg border border-destructive/40 bg-destructive/10 p-3"
            role="alert"
          >
            <p class="text-sm font-medium text-destructive">还差这些才能提交检验记录：</p>
            <ul class="grid list-disc gap-1 pl-5 text-sm text-destructive">
              <li v-for="(blocker, index) in submitBlockers" :key="index">{{ blocker }}</li>
            </ul>
          </div>

          <!-- 外层 div 已经是 sticky 单元，页脚在其内部退回普通流，避免双层 sticky
               与重复的负外边距/圆角/底色叠加。 -->
          <NvDialogFooter class="static mx-0 mt-0 mb-0 rounded-none bg-transparent px-0 pt-0 pb-0">
            <NvButton type="button" variant="outline" @click="recordSheetOpen = false"
              >取消</NvButton
            >
            <NvButton
              type="submit"
              :aria-describedby="submitBlockers.length ? 'record-submit-blockers' : undefined"
              :disabled="createInspectionRecordPending || !canCreateRecord"
            >
              <NvSpinner v-if="createInspectionRecordPending" aria-hidden="true" />
              <ClipboardCheckIcon v-else aria-hidden="true" />
              提交检验记录
            </NvButton>
          </NvDialogFooter>
        </div>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
