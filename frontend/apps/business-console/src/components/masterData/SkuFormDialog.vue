<script setup lang="ts">
/**
 * 物料的新建 / 编辑弹窗。物料维护页和表单里的物料选择器（`DirectoryPicker creatable`）共用。
 *
 * 每次打开都是全新实例（调用方递增 `key`），所以表单只在 setup 里按 `editing` 初始化一次：
 * 不传是新建，传一行是编辑（拉全字段详情回填，编码不可改）。
 */
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import type { DirectoryCreatedItem } from '@/components/business/directoryCreators'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useCreateSku, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import { useSkuReferenceOptions } from '@/composables/useSkuReferenceOptions'
import { useReturnFocusOnClose } from '@/composables/useReturnFocusOnClose'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  NvButton,
  NvCheckbox,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
} from '@nerv-iip/ui'
import { computed, reactive, shallowRef } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

const props = defineProps<{
  /** 要编辑的物料行；不传即新建。 */
  editing?: BusinessConsoleResourceItem
}>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ created: [item: DirectoryCreatedItem] }>()

const context = useBusinessContextStore()
const returnFocus = useReturnFocusOnClose()
const creation = useCreateSku()
const skuActions = useMasterDataResourceActions('sku')
const {
  productCategoryOptions,
  materialTypeOptions,
  batchPolicyOptions,
  serialPolicyOptions,
  shelfLifePolicyOptions,
  storageConditionOptions,
  barcodeRuleOptions,
  complianceTagOptions,
  baseUomOptions,
  pending: dictionaryPending,
} = useSkuReferenceOptions()

const editingCode = props.editing?.code ?? null
const showErrors = shallowRef(false)
const editLoading = shallowRef(false)
// 默认值取平台中性值（非样板业务词）；产品分类留空，强制用户主动选择。
const form = reactive({
  name: '',
  baseUomCode: 'pcs',
  category: '',
  materialType: 'finished-goods',
  batchTrackingPolicy: 'none',
  serialTrackingPolicy: 'none',
  shelfLifePolicyCode: 'none',
  storageConditionCode: 'ambient',
  defaultBarcodeRuleCode: 'code128',
  qualityRequired: true,
  complianceTags: [] as string[],
})
const idempotencyKey = `sku-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`

const hasRequiredDictionaryOptions = computed(() =>
  [
    productCategoryOptions.value,
    materialTypeOptions.value,
    batchPolicyOptions.value,
    serialPolicyOptions.value,
    shelfLifePolicyOptions.value,
    storageConditionOptions.value,
    barcodeRuleOptions.value,
    baseUomOptions.value,
  ].every((options) => options.length > 0),
)
function inOptions(options: readonly { value: string }[], value: string) {
  return options.some((option) => option.value === value)
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
// 字典化字段必须取自对应「实时选项」（实时为空时已回退常量），防止默认值/旧值漂移后提交字典里不存在的码值。
const formValid = computed(
  () =>
    !dictionaryPending.value &&
    hasRequiredDictionaryOptions.value &&
    isNonEmpty(form.name) &&
    inOptions(baseUomOptions.value, form.baseUomCode) &&
    inOptions(productCategoryOptions.value, form.category) &&
    inOptions(materialTypeOptions.value, form.materialType) &&
    inOptions(batchPolicyOptions.value, form.batchTrackingPolicy) &&
    inOptions(serialPolicyOptions.value, form.serialTrackingPolicy) &&
    inOptions(shelfLifePolicyOptions.value, form.shelfLifePolicyCode) &&
    inOptions(storageConditionOptions.value, form.storageConditionCode) &&
    inOptions(barcodeRuleOptions.value, form.defaultBarcodeRuleCode),
)
const saving = computed(() => creation.pending.value || skuActions.updatePending.value)

if (props.editing) void loadForEdit(props.editing)

async function loadForEdit(row: BusinessConsoleResourceItem) {
  editLoading.value = true
  try {
    const d = await skuActions.fetchDetail(row.code!)
    Object.assign(form, {
      name: d?.name ?? row.displayName ?? '',
      baseUomCode: d?.baseUomCode || 'pcs',
      category: d?.category ?? '',
      materialType: d?.materialType ?? form.materialType,
      batchTrackingPolicy: d?.batchTrackingPolicy ?? form.batchTrackingPolicy,
      serialTrackingPolicy: d?.serialTrackingPolicy ?? form.serialTrackingPolicy,
      shelfLifePolicyCode: d?.shelfLifePolicyCode ?? form.shelfLifePolicyCode,
      storageConditionCode: d?.storageConditionCode ?? form.storageConditionCode,
      defaultBarcodeRuleCode: d?.defaultBarcodeRuleCode ?? form.defaultBarcodeRuleCode,
      qualityRequired: d?.qualityRequired ?? true,
    })
  } finally {
    editLoading.value = false
  }
}

function setComplianceTag(code: string, checked: boolean) {
  if (checked && !form.complianceTags.includes(code)) {
    form.complianceTags.push(code)
    return
  }
  if (!checked) {
    form.complianceTags = form.complianceTags.filter((tag) => tag !== code)
  }
}
// 物料字段（编辑/新建共用），编辑时随 update 一并提交（编码不可改）。
function fieldPatch() {
  return {
    name: form.name.trim(),
    baseUomCode: form.baseUomCode.trim(),
    category: form.category.trim(),
    materialType: form.materialType.trim(),
    batchTrackingPolicy: form.batchTrackingPolicy.trim(),
    serialTrackingPolicy: form.serialTrackingPolicy.trim(),
    shelfLifePolicyCode: form.shelfLifePolicyCode.trim(),
    storageConditionCode: form.storageConditionCode.trim(),
    defaultBarcodeRuleCode: form.defaultBarcodeRuleCode.trim(),
    qualityRequired: form.qualityRequired,
  }
}
async function submit() {
  if (!formValid.value) {
    showErrors.value = true
    return
  }
  try {
    if (editingCode) {
      await skuActions.update(editingCode, fieldPatch())
      notifySuccess(`物料「${form.name.trim()}」已更新。`)
    } else {
      const name = form.name.trim()
      const response = await creation.create({
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        ...fieldPatch(),
        complianceTags: form.complianceTags.length ? form.complianceTags : undefined,
        idempotencyKey,
      })
      const code = response.data!.code!
      notifySuccess(`物料「${name}」已创建，编号 ${code}。`)
      emit('created', { code, name })
    }
    open.value = false
  } catch (error) {
    notifyOperationFailure('保存物料失败', error, '保存物料失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-3xl" @close-auto-focus="returnFocus">
      <NvDialogHeader>
        <NvDialogTitle>{{ editingCode ? `编辑物料 · ${editingCode}` : '新建物料' }}</NvDialogTitle>
        <NvDialogDescription class="sr-only">
          {{ editingCode ? `物料 ${editingCode}` : '新建物料档案' }}
        </NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-5" @submit.prevent="submit">
        <CarriedContextSummary
          v-if="editingCode"
          label="物料标识"
          :items="[{ label: '物料编号', value: editingCode }]"
        />
        <p v-if="showErrors && !formValid" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>

        <FormSectionTitle>基础信息</FormSectionTitle>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="showErrors && !isNonEmpty(form.name)">
            <NvFieldLabel for="sku-name"
              >物料名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput
              id="sku-name"
              v-model="form.name"
              autocomplete="off"
              aria-required="true"
              required
            />
          </NvField>
          <NvField :data-invalid="showErrors && !inOptions(productCategoryOptions, form.category)">
            <NvFieldLabel for="sku-category"
              >产品分类 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="form.category">
              <NvSelectTrigger id="sku-category"
                ><NvSelectValue placeholder="请选择分类"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in productCategoryOptions"
                  :key="option.value"
                  :value="option.value"
                  >{{ option.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField :data-invalid="showErrors && !inOptions(materialTypeOptions, form.materialType)">
            <NvFieldLabel>物料类型 <span class="text-destructive">*</span></NvFieldLabel>
            <NvSelect v-model="form.materialType">
              <NvSelectTrigger aria-label="物料类型"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in materialTypeOptions"
                  :key="option.value"
                  :value="option.value"
                  >{{ option.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
        </NvFieldGroup>

        <FormSectionTitle>单位与追踪</FormSectionTitle>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="showErrors && !inOptions(baseUomOptions, form.baseUomCode)">
            <NvFieldLabel for="sku-uom"
              >基本单位 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="form.baseUomCode">
              <NvSelectTrigger id="sku-uom"
                ><NvSelectValue placeholder="请选择单位"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in baseUomOptions"
                  :key="option.value"
                  :value="option.value"
                  >{{ option.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField class="self-start">
            <NvFieldLabel>质检要求</NvFieldLabel>
            <label
              for="sku-quality"
              class="flex h-9 cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 text-sm"
            >
              <span>投产前需质检</span>
              <NvCheckbox id="sku-quality" v-model="form.qualityRequired" />
            </label>
          </NvField>
          <NvField
            :data-invalid="showErrors && !inOptions(batchPolicyOptions, form.batchTrackingPolicy)"
          >
            <NvFieldLabel>批次追踪 <span class="text-destructive">*</span></NvFieldLabel>
            <NvSelect v-model="form.batchTrackingPolicy">
              <NvSelectTrigger aria-label="批次追踪"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in batchPolicyOptions"
                  :key="option.value"
                  :value="option.value"
                  >{{ option.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField
            :data-invalid="showErrors && !inOptions(serialPolicyOptions, form.serialTrackingPolicy)"
          >
            <NvFieldLabel>序列号追踪 <span class="text-destructive">*</span></NvFieldLabel>
            <NvSelect v-model="form.serialTrackingPolicy">
              <NvSelectTrigger aria-label="序列号追踪"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in serialPolicyOptions"
                  :key="option.value"
                  :value="option.value"
                  >{{ option.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
        </NvFieldGroup>

        <FormSectionTitle>存储与条码</FormSectionTitle>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField
            :data-invalid="
              showErrors && !inOptions(shelfLifePolicyOptions, form.shelfLifePolicyCode)
            "
          >
            <NvFieldLabel for="sku-shelf"
              >保质期管理 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="form.shelfLifePolicyCode">
              <NvSelectTrigger id="sku-shelf"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in shelfLifePolicyOptions"
                  :key="option.value"
                  :value="option.value"
                  >{{ option.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField
            :data-invalid="
              showErrors && !inOptions(storageConditionOptions, form.storageConditionCode)
            "
          >
            <NvFieldLabel for="sku-storage"
              >存储条件 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="form.storageConditionCode">
              <NvSelectTrigger id="sku-storage"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in storageConditionOptions"
                  :key="option.value"
                  :value="option.value"
                  >{{ option.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField
            :data-invalid="
              showErrors && !inOptions(barcodeRuleOptions, form.defaultBarcodeRuleCode)
            "
          >
            <NvFieldLabel for="sku-barcode"
              >默认条码规则 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="form.defaultBarcodeRuleCode">
              <NvSelectTrigger id="sku-barcode"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in barcodeRuleOptions"
                  :key="option.value"
                  :value="option.value"
                  >{{ option.label }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField class="sm:col-span-2">
            <NvFieldLabel>质量/合规标签</NvFieldLabel>
            <div class="grid gap-2 rounded-md border p-3 sm:grid-cols-3">
              <label
                v-for="option in complianceTagOptions"
                :key="option.value"
                class="flex items-center gap-2 text-sm"
              >
                <NvCheckbox
                  :model-value="form.complianceTags.includes(option.value)"
                  @update:model-value="setComplianceTag(option.value, $event === true)"
                />
                {{ option.label }}
              </label>
            </div>
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton
            type="submit"
            :disabled="saving || editLoading || dictionaryPending || !formValid"
          >
            <Spinner v-if="saving || dictionaryPending" aria-hidden="true" />
            {{ editingCode ? '保存修改' : '保存物料' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
