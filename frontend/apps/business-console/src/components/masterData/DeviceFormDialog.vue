<script setup lang="ts">
/**
 * 设备的新建 / 编辑弹窗。设备台账页和表单里的设备选择器（`DirectoryPicker creatable`）共用。
 *
 * 每次打开都是全新实例（调用方递增 `key`），所以表单只在 setup 里按 `editing` 初始化一次：
 * 不传是新建，传一行是编辑（拉全字段详情回填，编码不可改）。
 */
import type {
  BusinessConsoleRegisterDeviceAssetRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import type { DirectoryCreatedItem } from '@/components/business/directoryCreators'
import {
  useBusinessMasterDataResources,
  useCreateMasterDataResource,
  useMasterDataResourceActions,
} from '@/composables/master-data'
import { useReturnFocusOnClose } from '@/composables/shared/useReturnFocusOnClose'
import { currencyOptionsIncluding } from '@/data/currencyReference'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  NvButton,
  NvCheckbox,
  NvDatePicker,
  NvDialog,
  NvDialogBody,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldDescription,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
} from '@nerv-iip/ui'
import { PlusIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { i18n } from '@/i18n'

import { useFirstInvalidField } from './useFirstInvalidField'

const { formElement, focusFirstInvalidField } = useFirstInvalidField()
const locale = i18n.global.locale

const props = defineProps<{
  /** 要编辑的设备行；不传即新建。 */
  editing?: BusinessConsoleResourceItem
}>()
const open = defineModel<boolean>('open', { required: true })
const emit = defineEmits<{ created: [item: DirectoryCreatedItem] }>()

const CRITICALITY_OPTIONS = [
  { value: 'high', label: '高（关键设备）' },
  { value: 'medium', label: '中' },
  { value: 'low', label: '低' },
]
const DEVICE_DEFAULTS = {
  capacityUomCode: 'pcs',
  criticality: 'medium',
  maintainable: true,
  telemetryEnabled: false,
  purchaseCurrencyCode: 'CNY',
}
interface DeviceComponentForm {
  componentCode: string
  componentName: string
  quantity: string
  critical: boolean
}

const context = useBusinessContextStore()
const returnFocus = useReturnFocusOnClose()
const creation =
  useCreateMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('device-asset')
const deviceActions = useMasterDataResourceActions('device-asset')
const sites = useBusinessMasterDataResources('site', { all: true })
const devices = useBusinessMasterDataResources('device-asset', { all: true })

const editingCode = props.editing?.code ?? null
const showErrors = shallowRef(false)
const editLoading = shallowRef(false)
const form = reactive({
  model: '',
  manufacturer: '',
  serialNo: '',
  assetClassCode: '',
  siteCode: '',
  workshopCode: '',
  lineCode: '',
  workCenterCode: '',
  stationCode: '',
  purchaseDate: '',
  purchaseCost: '',
  purchaseCurrencyCode: DEVICE_DEFAULTS.purchaseCurrencyCode,
  warrantyExpiresOn: '',
  supplierPartnerCode: '',
  parentDeviceId: '',
  retiredOn: '',
  criticality: DEVICE_DEFAULTS.criticality,
  maintainable: DEVICE_DEFAULTS.maintainable,
  components: [emptyComponent()] as DeviceComponentForm[],
})

if (props.editing) {
  void loadForEdit(props.editing)
} else {
  // 缺省值：工厂只有一个时自动选中，不让用户为唯一选项再点一次（从选择器打开时工厂列表可能还在路上）。
  watch(
    () => (sites.resources.value.length === 1 ? sites.resources.value[0]!.code : undefined),
    (onlySiteCode) => {
      if (onlySiteCode && !form.siteCode) form.siteCode = onlySiteCode
    },
    { immediate: true },
  )
}

// ── 三个原本手输的编码字段，改为从真实目录里选 ─────────────────
// 候选取目录里的行（编码作值、名称作显示）；当前值不在候选里（已停用、或不在取回的这一批）时补到最前，
// 按编码显示，免得回填后显示成未选。
function codeOptions(rows: BusinessConsoleResourceItem[], current: string) {
  const options = rows
    .filter((row) => !!row.code)
    .map((row) => ({
      value: row.code as string,
      label: row.displayName || (row.code as string),
      hint: row.code ?? undefined,
    }))
  const code = current.trim()
  if (code && !options.some((option) => option.value === code)) {
    return [{ value: code, label: code, hint: undefined }, ...options]
  }
  return options
}

// 设备类别取数据字典 `asset-class` CodeSet；字典为空时给空态引导，不编造码值。
const assetClassCatalog = useBusinessMasterDataResources('reference-data', {
  codeSet: 'asset-class',
  all: true,
})
const assetClassOptions = computed(() =>
  codeOptions(
    assetClassCatalog.resources.value.filter((row) => row.active !== false),
    form.assetClassCode,
  ),
)

// 供应商只列带 supplier 角色的业务伙伴（伙伴可同时是客户与供应商，按角色包含关系筛）。
const suppliers = useBusinessMasterDataResources('business-partner', { all: true })
suppliers.filters.partnerType = 'supplier'
const { resources: partners, resourcesPending: partnersPending } = suppliers
const supplierOptions = computed(() =>
  codeOptions(
    partners.value
      .filter((row) => row.active !== false)
      .filter((row) =>
        [row.partnerType, ...(row.partnerRoles ?? [])]
          .map((role) => (role ?? '').trim())
          .includes('supplier'),
      ),
    form.supplierPartnerCode,
  ),
)

// 父设备来自设备台账本身，且必须排除正在编辑的这台——设备不能挂在自己名下。
const parentDeviceOptions = computed(() =>
  codeOptions(
    devices.resources.value.filter((row) => row.code !== editingCode),
    form.parentDeviceId,
  ),
)

// 层级字段逐级收窄：改了上级，下级原先选的值可能已不在新上级下，一律清空让用户重选
// （选择器按上级收窄后，留着旧值会在界面上显示成未选、提交时却仍带着它）。
// 工作中心和工位都挂在产线下、彼此不是上下级。
const LOWER_LEVELS = {
  siteCode: ['workshopCode', 'lineCode', 'workCenterCode', 'stationCode'],
  workshopCode: ['lineCode', 'workCenterCode', 'stationCode'],
  lineCode: ['workCenterCode', 'stationCode'],
} as const
function setLevel(level: keyof typeof LOWER_LEVELS, value: string) {
  if (form[level] === value) return
  form[level] = value
  for (const lower of LOWER_LEVELS[level]) form[lower] = ''
}

function isInvalidDecimal(value: string, minimum: number, precision: number) {
  if (String(value).trim() === '') return false
  const amount = Number(value)
  return (
    !Number.isFinite(amount) || amount < minimum || Number(amount.toFixed(precision)) !== amount
  )
}

const purchaseCostInvalid = computed(() => isInvalidDecimal(form.purchaseCost, 0, 2))

const formValid = computed(
  () =>
    [
      form.model,
      form.manufacturer,
      form.serialNo,
      form.assetClassCode,
      form.siteCode,
      form.workshopCode,
      form.lineCode,
      form.workCenterCode,
      form.stationCode,
      form.criticality,
    ].every(isNonEmpty) &&
    !purchaseCostInvalid.value &&
    form.components.every((component) => !isInvalidDecimal(component.quantity, 0.001, 3)),
)
const saving = computed(() => creation.pending.value || deviceActions.updatePending.value)

function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function emptyComponent(): DeviceComponentForm {
  return { componentCode: '', componentName: '', quantity: '1', critical: false }
}
async function loadForEdit(row: BusinessConsoleResourceItem) {
  editLoading.value = true
  try {
    const d = await deviceActions.fetchDetail(row.code!)
    Object.assign(form, {
      model: d?.model ?? '',
      manufacturer: d?.manufacturer ?? '',
      serialNo: d?.serialNo ?? '',
      assetClassCode: d?.assetClassCode ?? '',
      siteCode: d?.siteCode ?? row.siteCode ?? '',
      workshopCode: d?.workshopCode ?? row.workshopCode ?? '',
      lineCode: d?.lineCode ?? row.lineCode ?? '',
      workCenterCode: d?.workCenterCode ?? row.workCenterCode ?? '',
      stationCode: d?.stationCode ?? row.stationCode ?? '',
      purchaseDate: d?.purchaseDate ?? '',
      purchaseCost: d?.purchaseCost == null ? '' : String(d.purchaseCost),
      purchaseCurrencyCode: d?.purchaseCurrencyCode ?? DEVICE_DEFAULTS.purchaseCurrencyCode,
      warrantyExpiresOn: d?.warrantyExpiresOn ?? row.warrantyExpiresOn ?? '',
      supplierPartnerCode: d?.supplierPartnerCode ?? row.supplierPartnerCode ?? '',
      parentDeviceId: d?.parentDeviceId ?? row.parentDeviceId ?? '',
      retiredOn: d?.retiredOn ?? row.retiredOn ?? '',
      criticality: d?.criticality ?? DEVICE_DEFAULTS.criticality,
      maintainable: d?.maintainable ?? DEVICE_DEFAULTS.maintainable,
    })
    if (d?.components?.length) {
      form.components = d.components.map((c) => ({
        componentCode: c.componentCode ?? '',
        componentName: c.componentName ?? '',
        quantity: c.quantity == null ? '1' : String(c.quantity),
        critical: c.critical ?? false,
      }))
    }
  } finally {
    editLoading.value = false
  }
}
// type=number / type=date 的输入框回来的可能是数字，统一按文本处理。
function optionalText(value: string | number) {
  return String(value).trim() || undefined
}
function optionalNumber(value: string | number) {
  const trimmed = String(value).trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}
function isComponentReady(component: DeviceComponentForm) {
  return component.componentCode.trim().length > 0 && component.componentName.trim().length > 0
}
function componentQuantity(component: DeviceComponentForm) {
  return optionalNumber(component.quantity) ?? 1
}
function devicePayload() {
  return {
    model: form.model.trim(),
    manufacturer: form.manufacturer.trim(),
    serialNo: form.serialNo.trim(),
    assetClassCode: form.assetClassCode.trim(),
    lineCode: form.lineCode.trim(),
    workCenterCode: form.workCenterCode.trim(),
    siteCode: form.siteCode.trim(),
    workshopCode: form.workshopCode.trim(),
    stationCode: form.stationCode.trim(),
    purchaseDate: optionalText(form.purchaseDate),
    purchaseCost: optionalNumber(form.purchaseCost),
    purchaseCurrencyCode: form.purchaseCurrencyCode,
    warrantyExpiresOn: optionalText(form.warrantyExpiresOn),
    supplierPartnerCode: optionalText(form.supplierPartnerCode),
    parentDeviceId: optionalText(form.parentDeviceId),
    retiredOn: optionalText(form.retiredOn),
    components: form.components.filter(isComponentReady).map((component) => ({
      componentCode: component.componentCode.trim(),
      componentName: component.componentName.trim(),
      quantity: componentQuantity(component),
      critical: component.critical,
    })),
    capacityUomCode: DEVICE_DEFAULTS.capacityUomCode,
    criticality: form.criticality,
    maintainable: form.maintainable,
    telemetryEnabled: DEVICE_DEFAULTS.telemetryEnabled,
  }
}
function addComponent() {
  form.components.push(emptyComponent())
}
function removeComponent(index: number) {
  form.components.splice(index, 1)
  if (form.components.length === 0) form.components.push(emptyComponent())
}
async function submit() {
  if (!formValid.value) {
    showErrors.value = true
    await focusFirstInvalidField()
    return
  }
  const payload = devicePayload()
  try {
    if (editingCode) {
      await deviceActions.update(editingCode, { name: payload.model, ...payload })
      notifySuccess(`设备「${payload.model}」已更新。`)
    } else {
      const response = await creation.create({
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        ...payload,
      })
      const created = response.data!
      notifySuccess(`设备「${payload.model}」已登记。`)
      emit('created', { code: created.code!, name: created.displayName || payload.model })
    }
    open.value = false
  } catch (error) {
    notifyOperationFailure('保存设备失败', error, '保存设备失败，请稍后重试。')
  }
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent scrollable class="sm:max-w-2xl" @close-auto-focus="returnFocus">
      <NvDialogHeader>
        <NvDialogTitle>{{ editingCode ? `编辑设备 · ${editingCode}` : '新建设备' }}</NvDialogTitle>
        <NvDialogDescription class="sr-only">{{
          editingCode ? `设备 ${editingCode}` : '新建设备档案'
        }}</NvDialogDescription>
      </NvDialogHeader>
      <form ref="formElement" novalidate @submit.prevent="submit">
        <NvDialogBody>
          <div class="grid gap-4">
            <CarriedContextSummary
              v-if="editingCode"
              label="设备标识"
              :items="[{ label: '设备编码', value: editingCode }]"
            />
            <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
              <NvField :data-invalid="showErrors && !isNonEmpty(form.model)">
                <NvFieldLabel for="dev-model"
                  >设备型号 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvInput
                  id="dev-model"
                  :aria-describedby="
                    showErrors && !isNonEmpty(form.model) ? 'dev-model-error' : undefined
                  "
                  :aria-invalid="showErrors && !isNonEmpty(form.model)"
                  :invalid="showErrors && !isNonEmpty(form.model)"
                  v-model="form.model"
                  autocomplete="off"
                  required
                />
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.model)"
                  id="dev-model-error"
                  :errors="['请填写设备型号。']"
                />
              </NvField>
              <NvField :data-invalid="showErrors && !isNonEmpty(form.manufacturer)">
                <NvFieldLabel for="dev-maker"
                  >制造商 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvInput
                  id="dev-maker"
                  :aria-describedby="
                    showErrors && !isNonEmpty(form.manufacturer) ? 'dev-maker-error' : undefined
                  "
                  :aria-invalid="showErrors && !isNonEmpty(form.manufacturer)"
                  :invalid="showErrors && !isNonEmpty(form.manufacturer)"
                  v-model="form.manufacturer"
                  autocomplete="off"
                  required
                />
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.manufacturer)"
                  id="dev-maker-error"
                  :errors="['请填写制造商。']"
                />
              </NvField>
              <NvField :data-invalid="showErrors && !isNonEmpty(form.serialNo)">
                <NvFieldLabel for="dev-serial"
                  >出厂序列号 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvInput
                  id="dev-serial"
                  :aria-describedby="
                    showErrors && !isNonEmpty(form.serialNo) ? 'dev-serial-error' : undefined
                  "
                  :aria-invalid="showErrors && !isNonEmpty(form.serialNo)"
                  :invalid="showErrors && !isNonEmpty(form.serialNo)"
                  v-model="form.serialNo"
                  autocomplete="off"
                  required
                />
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.serialNo)"
                  id="dev-serial-error"
                  :errors="['请填写出厂序列号。']"
                />
              </NvField>
              <NvField :data-invalid="showErrors && !isNonEmpty(form.assetClassCode)">
                <NvFieldLabel for="dev-class"
                  >设备类别 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvSearchSelect
                  id="dev-class"
                  :aria-describedby="
                    showErrors && !isNonEmpty(form.assetClassCode) ? 'dev-class-error' : undefined
                  "
                  :aria-invalid="showErrors && !isNonEmpty(form.assetClassCode)"
                  v-model="form.assetClassCode"
                  :options="assetClassOptions"
                  placeholder="选择设备类别"
                  :loading="assetClassCatalog.resourcesPending.value"
                  empty-text="数据字典还没有设备类别，请先在「数据字典」维护"
                  aria-label="设备类别"
                />
                <!-- 取值来源（非显而易见），保留一行。 -->
                <NvFieldDescription v-if="!showErrors || isNonEmpty(form.assetClassCode)"
                  >取自「数据字典」的设备类别。</NvFieldDescription
                >
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.assetClassCode)"
                  id="dev-class-error"
                  :errors="['请选择设备类别。']"
                />
              </NvField>
              <NvField :data-invalid="showErrors && !isNonEmpty(form.criticality)">
                <NvFieldLabel for="dev-criticality"
                  >关键度 <span class="text-destructive">*</span></NvFieldLabel
                >
                <NvSelect v-model="form.criticality">
                  <NvSelectTrigger
                    id="dev-criticality"
                    :aria-describedby="
                      showErrors && !isNonEmpty(form.criticality)
                        ? 'dev-criticality-error'
                        : undefined
                    "
                    :aria-invalid="showErrors && !isNonEmpty(form.criticality)"
                    ><NvSelectValue
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem
                      v-for="o in CRITICALITY_OPTIONS"
                      :key="o.value"
                      :value="o.value"
                      >{{ o.label }}</NvSelectItem
                    >
                  </NvSelectContent>
                </NvSelect>
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.criticality)"
                  id="dev-criticality-error"
                  :errors="['请选择关键度。']"
                />
              </NvField>
              <NvField :data-invalid="showErrors && !isNonEmpty(form.siteCode)">
                <NvFieldLabel for="dev-site"
                  >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
                >
                <DirectoryPicker
                  id="dev-site"
                  :aria-describedby="
                    showErrors && !isNonEmpty(form.siteCode) ? 'dev-site-error' : undefined
                  "
                  directory-type="site"
                  creatable
                  :model-value="form.siteCode"
                  :invalid="showErrors && !isNonEmpty(form.siteCode)"
                  @update:model-value="setLevel('siteCode', $event)"
                />
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.siteCode)"
                  id="dev-site-error"
                  :errors="['请选择所属工厂。']"
                />
              </NvField>
              <NvField :data-invalid="showErrors && !isNonEmpty(form.workshopCode)">
                <NvFieldLabel for="dev-workshop"
                  >所属车间 <span class="text-destructive">*</span></NvFieldLabel
                >
                <DirectoryPicker
                  id="dev-workshop"
                  :aria-describedby="
                    showErrors && !isNonEmpty(form.workshopCode) ? 'dev-workshop-error' : undefined
                  "
                  directory-type="workshop"
                  creatable
                  :parent="{ siteCode: form.siteCode }"
                  :create-context="{ siteCode: form.siteCode }"
                  :model-value="form.workshopCode"
                  :invalid="showErrors && !isNonEmpty(form.workshopCode)"
                  @update:model-value="setLevel('workshopCode', $event)"
                />
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.workshopCode)"
                  id="dev-workshop-error"
                  :errors="['请选择所属车间。']"
                />
              </NvField>
              <NvField :data-invalid="showErrors && !isNonEmpty(form.lineCode)">
                <NvFieldLabel for="dev-line"
                  >所属产线 <span class="text-destructive">*</span></NvFieldLabel
                >
                <DirectoryPicker
                  id="dev-line"
                  :aria-describedby="
                    showErrors && !isNonEmpty(form.lineCode) ? 'dev-line-error' : undefined
                  "
                  directory-type="production-line"
                  creatable
                  :parent="{ siteCode: form.siteCode, workshopCode: form.workshopCode }"
                  :create-context="{ siteCode: form.siteCode, workshopCode: form.workshopCode }"
                  :model-value="form.lineCode"
                  :invalid="showErrors && !isNonEmpty(form.lineCode)"
                  @update:model-value="setLevel('lineCode', $event)"
                />
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.lineCode)"
                  id="dev-line-error"
                  :errors="['请选择所属产线。']"
                />
              </NvField>
              <NvField :data-invalid="showErrors && !isNonEmpty(form.workCenterCode)">
                <NvFieldLabel for="dev-wc"
                  >所属工作中心 <span class="text-destructive">*</span></NvFieldLabel
                >
                <DirectoryPicker
                  id="dev-wc"
                  :aria-describedby="
                    showErrors && !isNonEmpty(form.workCenterCode) ? 'dev-wc-error' : undefined
                  "
                  v-model="form.workCenterCode"
                  directory-type="work-center"
                  creatable
                  :parent="{ lineCode: form.lineCode }"
                  :create-context="{ siteCode: form.siteCode, lineCode: form.lineCode }"
                  :invalid="showErrors && !isNonEmpty(form.workCenterCode)"
                />
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.workCenterCode)"
                  id="dev-wc-error"
                  :errors="['请选择所属工作中心。']"
                />
              </NvField>
              <NvField :data-invalid="showErrors && !isNonEmpty(form.stationCode)">
                <NvFieldLabel for="dev-station"
                  >所属工位 <span class="text-destructive">*</span></NvFieldLabel
                >
                <DirectoryPicker
                  id="dev-station"
                  :aria-describedby="
                    showErrors && !isNonEmpty(form.stationCode) ? 'dev-station-error' : undefined
                  "
                  v-model="form.stationCode"
                  directory-type="station"
                  creatable
                  :parent="{ lineCode: form.lineCode }"
                  :create-context="{ lineCode: form.lineCode }"
                  :invalid="showErrors && !isNonEmpty(form.stationCode)"
                />
                <NvFieldError
                  v-if="showErrors && !isNonEmpty(form.stationCode)"
                  id="dev-station-error"
                  :errors="['请选择所属工位。']"
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="dev-purchase-date">购置日期</NvFieldLabel>
                <NvDatePicker
                  id="dev-purchase-date"
                  v-model="form.purchaseDate"
                  :locale="locale"
                  clearable
                />
              </NvField>
              <NvField :data-invalid="showErrors && purchaseCostInvalid">
                <NvFieldLabel for="dev-purchase-cost">购置成本</NvFieldLabel>
                <NvInput
                  id="dev-purchase-cost"
                  :aria-invalid="showErrors && purchaseCostInvalid"
                  :invalid="showErrors && purchaseCostInvalid"
                  :aria-describedby="
                    showErrors && purchaseCostInvalid ? 'dev-purchase-cost-error' : undefined
                  "
                  v-model="form.purchaseCost"
                  type="number"
                  min="0"
                  step="0.01"
                />
                <NvFieldError
                  v-if="showErrors && purchaseCostInvalid"
                  id="dev-purchase-cost-error"
                  :errors="['请输入非负且最多两位小数的成本。']"
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="dev-currency">币种</NvFieldLabel>
                <NvSearchSelect
                  id="dev-currency"
                  v-model="form.purchaseCurrencyCode"
                  :options="currencyOptionsIncluding(form.purchaseCurrencyCode)"
                  search-placeholder="搜索币种代码或名称"
                  aria-label="币种"
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="dev-warranty">保修到期</NvFieldLabel>
                <NvDatePicker
                  id="dev-warranty"
                  v-model="form.warrantyExpiresOn"
                  :locale="locale"
                  clearable
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="dev-supplier">供应商</NvFieldLabel>
                <NvEntityPicker
                  id="dev-supplier"
                  v-model="form.supplierPartnerCode"
                  :options="supplierOptions"
                  title="选择供应商"
                  placeholder="可留空"
                  empty-text="暂无供应商，请先在业务伙伴维护"
                  :loading="partnersPending"
                  aria-label="供应商"
                  clearable
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="dev-parent">父设备</NvFieldLabel>
                <NvEntityPicker
                  id="dev-parent"
                  v-model="form.parentDeviceId"
                  :options="parentDeviceOptions"
                  title="选择父设备"
                  placeholder="可留空"
                  empty-text="暂无可挂靠的设备"
                  :loading="devices.resourcesPending.value"
                  aria-label="父设备"
                  clearable
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="dev-retired">退役日期</NvFieldLabel>
                <NvDatePicker
                  id="dev-retired"
                  v-model="form.retiredOn"
                  :locale="locale"
                  clearable
                />
              </NvField>
              <NvField
                orientation="horizontal"
                class="h-fit items-center justify-between gap-3 self-start rounded-lg border px-3 py-2 sm:col-span-2"
              >
                <NvFieldLabel for="dev-maintainable" class="mb-0">纳入维护计划</NvFieldLabel>
                <NvCheckbox id="dev-maintainable" v-model="form.maintainable" />
              </NvField>
            </NvFieldGroup>
            <div class="grid gap-3">
              <div class="flex items-center justify-between gap-3">
                <NvFieldLabel>部件结构</NvFieldLabel>
                <NvButton size="sm" variant="outline" type="button" @click="addComponent">
                  <PlusIcon aria-hidden="true" />
                  添加部件
                </NvButton>
              </div>
              <div
                v-for="(component, index) in form.components"
                :key="index"
                class="grid gap-3 rounded-md border px-3 py-3 sm:grid-cols-[1fr_1fr_6rem_auto_auto]"
              >
                <NvField>
                  <NvFieldLabel :for="`dev-component-code-${index}`">部件编码</NvFieldLabel>
                  <NvInput
                    :id="`dev-component-code-${index}`"
                    v-model="component.componentCode"
                    autocomplete="off"
                  />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`dev-component-name-${index}`">部件名称</NvFieldLabel>
                  <NvInput
                    :id="`dev-component-name-${index}`"
                    v-model="component.componentName"
                    autocomplete="off"
                  />
                </NvField>
                <NvField
                  :data-invalid="showErrors && isInvalidDecimal(component.quantity, 0.001, 3)"
                >
                  <NvFieldLabel :for="`dev-component-qty-${index}`">数量</NvFieldLabel>
                  <NvInput
                    :id="`dev-component-qty-${index}`"
                    :aria-invalid="showErrors && isInvalidDecimal(component.quantity, 0.001, 3)"
                    :aria-describedby="
                      showErrors && isInvalidDecimal(component.quantity, 0.001, 3)
                        ? `dev-component-qty-error-${index}`
                        : undefined
                    "
                    v-model="component.quantity"
                    type="number"
                    min="0.001"
                    step="0.001"
                  />
                  <NvFieldError
                    v-if="showErrors && isInvalidDecimal(component.quantity, 0.001, 3)"
                    :id="`dev-component-qty-error-${index}`"
                    :errors="['请输入大于 0 且最多三位小数的数量。']"
                  />
                </NvField>
                <NvField orientation="horizontal" class="items-center gap-2 self-end pb-2">
                  <NvCheckbox
                    :id="`dev-component-critical-${index}`"
                    v-model="component.critical"
                  />
                  <NvFieldLabel :for="`dev-component-critical-${index}`" class="mb-0"
                    >关键</NvFieldLabel
                  >
                </NvField>
                <NvButton
                  class="self-end"
                  size="icon"
                  variant="ghost"
                  type="button"
                  :aria-label="`删除部件 ${index + 1}`"
                  @click="removeComponent(index)"
                >
                  <Trash2Icon aria-hidden="true" />
                </NvButton>
              </div>
            </div>
          </div>
        </NvDialogBody>
        <NvDialogFooter>
          <div v-if="showErrors && !formValid" class="flex flex-wrap items-center gap-2 sm:mr-auto">
            <p class="text-sm text-destructive" role="alert">请检查标红字段后再提交。</p>
            <NvButton type="button" size="sm" variant="ghost" @click="focusFirstInvalidField">
              定位首个错误
            </NvButton>
          </div>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="saving || editLoading">
            <NvSpinner v-if="saving" aria-hidden="true" />
            {{ editingCode ? '保存修改' : '保存设备' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
