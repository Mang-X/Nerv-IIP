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
  useBusinessPartners,
  useCreateMasterDataResource,
  useMasterDataResourceActions,
} from '@/composables/useBusinessMasterData'
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
  NvEntityPicker,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
} from '@nerv-iip/ui'
import { PlusIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

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
const sites = useBusinessMasterDataResources('site')
const devices = useBusinessMasterDataResources('device-asset')

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
})
const assetClassOptions = computed(() =>
  codeOptions(
    assetClassCatalog.resources.value.filter((row) => row.active !== false),
    form.assetClassCode,
  ),
)

// 供应商只列带 supplier 角色的业务伙伴（伙伴可同时是客户与供应商，按角色包含关系筛）。
const { partners, partnersPending } = useBusinessPartners()
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

const currencyValidationMessage = computed(() => {
  const code = form.purchaseCurrencyCode.trim()
  if (!code) return ''
  return /^[a-z]{3}$/i.test(code) ? '' : '币种必须是 3 位字母编码。'
})
const componentValidationMessage = computed(() => {
  const invalid = form.components.find(
    (component) => isComponentReady(component) && componentQuantity(component) <= 0,
  )
  return invalid ? '部件数量必须大于 0。' : ''
})
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
    !currencyValidationMessage.value &&
    !componentValidationMessage.value,
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
  const currency = form.purchaseCurrencyCode.trim()
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
    purchaseCurrencyCode: currency ? currency.toUpperCase() : undefined,
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
    <NvDialogContent class="sm:max-w-2xl" @close-auto-focus="returnFocus">
      <NvDialogHeader>
        <NvDialogTitle>{{ editingCode ? `编辑设备 · ${editingCode}` : '新建设备' }}</NvDialogTitle>
        <NvDialogDescription class="sr-only">{{
          editingCode ? `设备 ${editingCode}` : '新建设备档案'
        }}</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submit">
        <CarriedContextSummary
          v-if="editingCode"
          label="设备标识"
          :items="[{ label: '设备编码', value: editingCode }]"
        />
        <p v-if="showErrors && !formValid" class="text-sm text-destructive" role="alert">
          请检查标红字段后再提交。
        </p>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="showErrors && !isNonEmpty(form.model)">
            <NvFieldLabel for="dev-model"
              >设备型号 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="dev-model" v-model="form.model" autocomplete="off" required />
          </NvField>
          <NvField :data-invalid="showErrors && !isNonEmpty(form.manufacturer)">
            <NvFieldLabel for="dev-maker"
              >制造商 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="dev-maker" v-model="form.manufacturer" autocomplete="off" required />
          </NvField>
          <NvField :data-invalid="showErrors && !isNonEmpty(form.serialNo)">
            <NvFieldLabel for="dev-serial"
              >出厂序列号 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="dev-serial" v-model="form.serialNo" autocomplete="off" required />
          </NvField>
          <NvField :data-invalid="showErrors && !isNonEmpty(form.assetClassCode)">
            <NvFieldLabel for="dev-class"
              >设备类别 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSearchSelect
              id="dev-class"
              v-model="form.assetClassCode"
              :options="assetClassOptions"
              placeholder="选择设备类别"
              :loading="assetClassCatalog.resourcesPending.value"
              empty-text="数据字典还没有设备类别，请先在「数据字典」维护"
              aria-label="设备类别"
            />
            <!-- 取值来源（非显而易见），保留一行。 -->
            <NvFieldDescription>取自「数据字典」的设备类别。</NvFieldDescription>
          </NvField>
          <NvField :data-invalid="showErrors && !isNonEmpty(form.criticality)">
            <NvFieldLabel for="dev-criticality"
              >关键度 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="form.criticality">
              <NvSelectTrigger id="dev-criticality"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in CRITICALITY_OPTIONS" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField :data-invalid="showErrors && !isNonEmpty(form.siteCode)">
            <NvFieldLabel for="dev-site"
              >所属工厂 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="dev-site"
              directory-type="site"
              creatable
              :model-value="form.siteCode"
              :invalid="showErrors && !isNonEmpty(form.siteCode)"
              @update:model-value="setLevel('siteCode', $event)"
            />
          </NvField>
          <NvField :data-invalid="showErrors && !isNonEmpty(form.workshopCode)">
            <NvFieldLabel for="dev-workshop"
              >所属车间 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="dev-workshop"
              directory-type="workshop"
              creatable
              :parent="{ siteCode: form.siteCode }"
              :create-context="{ siteCode: form.siteCode }"
              :model-value="form.workshopCode"
              :invalid="showErrors && !isNonEmpty(form.workshopCode)"
              @update:model-value="setLevel('workshopCode', $event)"
            />
          </NvField>
          <NvField :data-invalid="showErrors && !isNonEmpty(form.lineCode)">
            <NvFieldLabel for="dev-line"
              >所属产线 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="dev-line"
              directory-type="production-line"
              creatable
              :parent="{ siteCode: form.siteCode, workshopCode: form.workshopCode }"
              :create-context="{ siteCode: form.siteCode, workshopCode: form.workshopCode }"
              :model-value="form.lineCode"
              :invalid="showErrors && !isNonEmpty(form.lineCode)"
              @update:model-value="setLevel('lineCode', $event)"
            />
          </NvField>
          <NvField :data-invalid="showErrors && !isNonEmpty(form.workCenterCode)">
            <NvFieldLabel for="dev-wc"
              >所属工作中心 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="dev-wc"
              v-model="form.workCenterCode"
              directory-type="work-center"
              creatable
              :parent="{ lineCode: form.lineCode }"
              :create-context="{ siteCode: form.siteCode, lineCode: form.lineCode }"
              :invalid="showErrors && !isNonEmpty(form.workCenterCode)"
            />
          </NvField>
          <NvField :data-invalid="showErrors && !isNonEmpty(form.stationCode)">
            <NvFieldLabel for="dev-station"
              >所属工位 <span class="text-destructive">*</span></NvFieldLabel
            >
            <DirectoryPicker
              id="dev-station"
              v-model="form.stationCode"
              directory-type="station"
              creatable
              :parent="{ lineCode: form.lineCode }"
              :create-context="{ lineCode: form.lineCode }"
              :invalid="showErrors && !isNonEmpty(form.stationCode)"
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="dev-purchase-date">购置日期</NvFieldLabel>
            <NvInput id="dev-purchase-date" v-model="form.purchaseDate" type="date" />
          </NvField>
          <NvField>
            <NvFieldLabel for="dev-purchase-cost">购置成本</NvFieldLabel>
            <NvInput
              id="dev-purchase-cost"
              v-model="form.purchaseCost"
              type="number"
              min="0"
              step="0.01"
            />
          </NvField>
          <NvField :data-invalid="showErrors && Boolean(currencyValidationMessage)">
            <NvFieldLabel for="dev-currency">币种</NvFieldLabel>
            <NvInput
              id="dev-currency"
              v-model="form.purchaseCurrencyCode"
              autocomplete="off"
              maxlength="3"
            />
            <NvFieldDescription v-if="showErrors && currencyValidationMessage">{{
              currencyValidationMessage
            }}</NvFieldDescription>
          </NvField>
          <NvField>
            <NvFieldLabel for="dev-warranty">保修到期</NvFieldLabel>
            <NvInput id="dev-warranty" v-model="form.warrantyExpiresOn" type="date" />
          </NvField>
          <NvField>
            <NvFieldLabel for="dev-supplier">供应商</NvFieldLabel>
            <NvEntityPicker
              id="dev-supplier"
              v-model="form.supplierPartnerCode"
              :options="supplierOptions"
              title="选择供应商"
              placeholder="可留空"
              source-text="数据来自业务伙伴（供应商角色）"
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
              source-text="数据来自设备台账（已排除本机）"
              empty-text="暂无可挂靠的设备"
              :loading="devices.resourcesPending.value"
              aria-label="父设备"
              clearable
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="dev-retired">退役日期</NvFieldLabel>
            <NvInput id="dev-retired" v-model="form.retiredOn" type="date" />
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
              :data-invalid="
                showErrors && isComponentReady(component) && componentQuantity(component) <= 0
              "
            >
              <NvFieldLabel :for="`dev-component-qty-${index}`">数量</NvFieldLabel>
              <NvInput
                :id="`dev-component-qty-${index}`"
                v-model="component.quantity"
                type="number"
                min="0.001"
                step="0.001"
              />
              <NvFieldDescription
                v-if="
                  showErrors && isComponentReady(component) && componentQuantity(component) <= 0
                "
                >必须大于 0。</NvFieldDescription
              >
            </NvField>
            <NvField orientation="horizontal" class="items-center gap-2 self-end pb-2">
              <NvCheckbox :id="`dev-component-critical-${index}`" v-model="component.critical" />
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
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
          <NvButton type="submit" :disabled="saving || editLoading">
            <Spinner v-if="saving" aria-hidden="true" />
            {{ editingCode ? '保存修改' : '保存设备' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
