<script setup lang="ts">
import type {
  BusinessConsoleRegisterDeviceAssetRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useBusinessWorkshops, useMasterDataResource, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCheckbox,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate, formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '设备台账', requiredPermissions: ['business.masterdata.resources.read'] } })

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

const devices = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('device-asset')
const sites = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('site')
const workshops = useBusinessWorkshops()
const lines = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('production-line')
const workCenters = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('work-center')
const deviceActions = useMasterDataResourceActions('device-asset')

// 列表回传的是 lineCode/workCenterCode（编码）；解析成名称显示（取自产线/工作中心实体，找不到回退编码）。
const siteNameByCode = computed(() => new Map(sites.items.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])))
const workshopNameByCode = computed(() => new Map(workshops.workshops.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])))
const lineNameByCode = computed(() => new Map(lines.items.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])))
const wcNameByCode = computed(() => new Map(workCenters.items.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])))
function siteName(code?: string | null) { return code ? (siteNameByCode.value.get(code) ?? code) : '无' }
function workshopName(code?: string | null) { return code ? (workshopNameByCode.value.get(code) ?? code) : '无' }
function lineName(code?: string | null) { return code ? (lineNameByCode.value.get(code) ?? code) : '无' }
function wcName(code?: string | null) { return code ? (wcNameByCode.value.get(code) ?? code) : '无' }

const keyword = ref('')
const page = ref(1)
const pageSize = ref('10')
const createOpen = ref(false)
const createShowErrors = ref(false)
// 编辑态：null=新建，否则=正在编辑的设备编码（编码不可改）。
const editingCode = shallowRef<string | null>(null)
const editLoading = shallowRef(false)
const createForm = reactive({
  code: '',
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
  components: [] as DeviceComponentForm[],
})

const columns: NvDataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '设备编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '设备名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'siteCode', header: '工厂', width: 'w-28', accessor: (r) => siteName(r.siteCode) },
  { key: 'workshopCode', header: '车间', width: 'w-28', accessor: (r) => workshopName(r.workshopCode) },
  { key: 'lineCode', header: '所属产线', width: 'w-32', accessor: (r) => lineName(r.lineCode) },
  { key: 'stationCode', header: '工位', width: 'w-28', accessor: (r) => r.stationCode ?? '无' },
  { key: 'warrantyExpiresOn', header: '保修到期', width: 'w-28', accessor: (r) => formatDate(r.warrantyExpiresOn) },
  { key: 'supplierPartnerCode', header: '供应商', width: 'w-28', accessor: (r) => r.supplierPartnerCode ?? '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '更新时间', width: 'w-40', accessor: (r) => formatDateTime(r.snapshotVersion) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

function deviceDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '设备编码', value: row.code ?? '' },
    { label: '设备名称', value: row.displayName ?? '' },
    { label: '所属工厂', value: siteName(row.siteCode) },
    { label: '所属车间', value: workshopName(row.workshopCode) },
    { label: '所属产线', value: lineName(row.lineCode) },
    { label: '所属工作中心', value: wcName(row.workCenterCode) },
    { label: '所属工位', value: row.stationCode ?? '' },
    { label: '购置日期', value: formatDate(row.purchaseDate) },
    { label: '购置成本', value: formatMoney(row.purchaseCost, row.purchaseCurrencyCode) },
    { label: '保修到期', value: formatDate(row.warrantyExpiresOn) },
    { label: '供应商', value: row.supplierPartnerCode ?? '' },
    { label: '父设备', value: row.parentDeviceId ?? '' },
    { label: '退役日期', value: formatDate(row.retiredOn) },
  ]
}

const workshopOptions = computed(() =>
  workshops.workshops.value.filter((w) => !createForm.siteCode || (w.siteCode ?? '') === createForm.siteCode),
)
const lineOptions = computed(() =>
  lines.items.value.filter((l) =>
    (!createForm.siteCode || (l.siteCode ?? '') === createForm.siteCode)
    && (!createForm.workshopCode || (l.workshopCode ?? '') === createForm.workshopCode),
  ),
)
const workCenterOptions = computed(() =>
  workCenters.items.value.filter((w) => !createForm.lineCode || (w.lineCode ?? '') === createForm.lineCode),
)
const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return devices.items.value
  return devices.items.value.filter((row) =>
    [row.code, row.displayName, row.snapshotVersion].some((value) => (value ?? '').toLowerCase().includes(kw)),
  )
})
const canCreateDevice = computed(() =>
  [createForm.model, createForm.manufacturer, createForm.serialNo,
    createForm.assetClassCode, createForm.siteCode, createForm.workshopCode,
    createForm.lineCode, createForm.workCenterCode, createForm.stationCode, createForm.criticality].every(isNonEmpty)
  && !currencyValidationMessage.value
  && !componentValidationMessage.value,
)
const listErrorMessage = computed(() => formatError(devices.error.value))
const currencyValidationMessage = computed(() => {
  const code = createForm.purchaseCurrencyCode.trim()
  if (!code) return ''
  return /^[a-z]{3}$/i.test(code) ? '' : '币种必须是 3 位字母编码。'
})
const componentValidationMessage = computed(() => {
  const invalid = createForm.components.find((component) => isComponentReady(component) && componentQuantity(component) <= 0)
  return invalid ? '部件数量必须大于 0。' : ''
})

watch(createOpen, (open) => { if (open) createShowErrors.value = false })
watch([keyword, pageSize], () => { page.value = 1 })
watch([page, pageSize], () => {
  devices.filters.skip = (page.value - 1) * (Number(pageSize.value) || 10)
  devices.filters.take = Number(pageSize.value) || 10
}, { immediate: true })

function rowKey(item: BusinessConsoleResourceItem) {
  return `${item.resourceType ?? 'device-asset'}:${item.code || item.displayName || ''}`
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function formatMoney(value?: number | null, currency?: string | null) {
  if (value == null) return '无'
  const code = currency?.trim() || DEVICE_DEFAULTS.purchaseCurrencyCode
  try {
    return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: code, maximumFractionDigits: 2 }).format(value)
  }
  catch {
    return `${value.toFixed(2)} ${code}`
  }
}
function refreshAll() {
  void devices.refresh()
  void sites.refresh()
  void workshops.refreshWorkshops()
  void lines.refresh()
  void workCenters.refresh()
}
function emptyComponent(): DeviceComponentForm {
  return { componentCode: '', componentName: '', quantity: '1', critical: false }
}
function resetCreateForm() {
  Object.assign(createForm, {
    code: '', model: '', manufacturer: '', serialNo: '', assetClassCode: '',
    siteCode: '', workshopCode: '', lineCode: '', workCenterCode: '', stationCode: '',
    purchaseDate: '', purchaseCost: '', purchaseCurrencyCode: DEVICE_DEFAULTS.purchaseCurrencyCode,
    warrantyExpiresOn: '', supplierPartnerCode: '', parentDeviceId: '', retiredOn: '',
    criticality: DEVICE_DEFAULTS.criticality, maintainable: DEVICE_DEFAULTS.maintainable,
  })
  createForm.components.splice(0, createForm.components.length, emptyComponent())
}
function openCreate() {
  editingCode.value = null
  resetCreateForm()
  createShowErrors.value = false
  createOpen.value = true
}
async function openEdit(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  editingCode.value = row.code
  createShowErrors.value = false
  editLoading.value = true
  createOpen.value = true
  try {
    const d = await deviceActions.fetchDetail(row.code)
    Object.assign(createForm, {
      code: row.code,
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
    createForm.components.splice(0, createForm.components.length, ...(d?.components?.length
      ? d.components.map((c) => ({
          componentCode: c.componentCode ?? '',
          componentName: c.componentName ?? '',
          quantity: c.quantity == null ? '1' : String(c.quantity),
          critical: c.critical ?? false,
        }))
      : [emptyComponent()]))
  }
  finally {
    editLoading.value = false
  }
}
function optionalText(value: string | number | null | undefined) {
  const trimmed = value == null ? '' : String(value).trim()
  return trimmed || undefined
}
function optionalNumber(value: string | number | null | undefined) {
  const trimmed = value == null ? '' : String(value).trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}
function normalizedCurrencyCode(value: string) {
  const code = value.trim()
  return code ? code.toUpperCase() : undefined
}
function isComponentReady(component: DeviceComponentForm) {
  return component.componentCode.trim().length > 0 && component.componentName.trim().length > 0
}
function componentQuantity(component: DeviceComponentForm) {
  return optionalNumber(component.quantity) ?? 1
}
function componentPayload(): NonNullable<BusinessConsoleRegisterDeviceAssetRequest['components']> {
  return createForm.components
    .map((component) => ({
      componentCode: component.componentCode.trim(),
      componentName: component.componentName.trim(),
      quantity: componentQuantity(component),
      critical: component.critical,
    }))
    .filter((component) => component.componentCode.length > 0 && component.componentName.length > 0)
}
function deviceLedgerPayload() {
  const components = componentPayload()
  return {
    siteCode: createForm.siteCode.trim(),
    workshopCode: createForm.workshopCode.trim(),
    stationCode: createForm.stationCode.trim(),
    purchaseDate: optionalText(createForm.purchaseDate),
    purchaseCost: optionalNumber(createForm.purchaseCost),
    purchaseCurrencyCode: normalizedCurrencyCode(createForm.purchaseCurrencyCode),
    warrantyExpiresOn: optionalText(createForm.warrantyExpiresOn),
    supplierPartnerCode: optionalText(createForm.supplierPartnerCode),
    parentDeviceId: optionalText(createForm.parentDeviceId),
    retiredOn: optionalText(createForm.retiredOn),
    components,
  }
}
function addComponent() {
  createForm.components.push(emptyComponent())
}
function removeComponent(index: number) {
  createForm.components.splice(index, 1)
  if (createForm.components.length === 0) createForm.components.push(emptyComponent())
}
async function submitDevice() {
  if (!canCreateDevice.value) {
    createShowErrors.value = true
    return
  }
  const ledger = deviceLedgerPayload()
  try {
    if (editingCode.value) {
      await deviceActions.update(editingCode.value, {
        name: createForm.model.trim(),
        model: createForm.model.trim(),
        manufacturer: createForm.manufacturer.trim(),
        serialNo: createForm.serialNo.trim(),
        assetClassCode: createForm.assetClassCode.trim(),
        lineCode: createForm.lineCode.trim(),
        workCenterCode: createForm.workCenterCode.trim(),
        ...ledger,
        capacityUomCode: DEVICE_DEFAULTS.capacityUomCode,
        criticality: createForm.criticality,
        maintainable: createForm.maintainable,
        telemetryEnabled: DEVICE_DEFAULTS.telemetryEnabled,
      })
      notifySuccess(`设备「${createForm.model.trim()}」已更新。`)
    }
    else {
      await devices.create({
        organizationId: devices.filters.organizationId,
        environmentId: devices.filters.environmentId,
        model: createForm.model.trim(),
        manufacturer: createForm.manufacturer.trim(),
        serialNo: createForm.serialNo.trim(),
        assetClassCode: createForm.assetClassCode.trim(),
        lineCode: createForm.lineCode.trim(),
        workCenterCode: createForm.workCenterCode.trim(),
        ...ledger,
        capacityUomCode: DEVICE_DEFAULTS.capacityUomCode,
        criticality: createForm.criticality,
        maintainable: createForm.maintainable,
        telemetryEnabled: DEVICE_DEFAULTS.telemetryEnabled,
      })
      notifySuccess(`设备「${createForm.model.trim()}」已登记。`)
    }
    resetCreateForm()
    editingCode.value = null
    createShowErrors.value = false
    createOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="设备台账" :breadcrumbs="[{ label: '基础数据' }]" :count="`${devices.total.value} 台设备`">
      <template #actions>
        <NvButton size="sm" variant="outline" type="button" :disabled="devices.pending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="createOpen">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建设备
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-2xl">
            <NvDialogHeader>
              <NvDialogTitle>{{ editingCode ? `编辑设备 · ${editingCode}` : '新建设备' }}</NvDialogTitle>
              <NvDialogDescription>{{ editingCode ? '修改设备档案（编码不可修改）。带 * 为必填项。' : '为产线与工作中心登记一台设备资产。带 * 为必填项。' }}</NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-4" @submit.prevent="submitDevice">
              <p v-if="createShowErrors && !canCreateDevice" class="text-sm text-destructive" role="alert">请检查标红字段后再提交。</p>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField v-if="editingCode">
                  <NvFieldLabel for="dev-code">设备编码</NvFieldLabel>
                  <NvInput id="dev-code" :model-value="createForm.code" disabled />
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.model)">
                  <NvFieldLabel for="dev-model">设备型号 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvInput id="dev-model" v-model="createForm.model" autocomplete="off" required />
                  <NvFieldDescription v-if="!editingCode">编码由系统自动生成。</NvFieldDescription>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.manufacturer)">
                  <NvFieldLabel for="dev-maker">制造商 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvInput id="dev-maker" v-model="createForm.manufacturer" autocomplete="off" required />
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.serialNo)">
                  <NvFieldLabel for="dev-serial">出厂序列号 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvInput id="dev-serial" v-model="createForm.serialNo" autocomplete="off" required />
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.assetClassCode)">
                  <NvFieldLabel for="dev-class">设备类别 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvInput id="dev-class" v-model="createForm.assetClassCode" autocomplete="off" required />
                  <NvFieldDescription>填写「数据字典」中维护的设备类别编码。</NvFieldDescription>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.criticality)">
                  <NvFieldLabel for="dev-criticality">关键度 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="createForm.criticality">
                    <NvSelectTrigger id="dev-criticality"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem v-for="o in CRITICALITY_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.siteCode)">
                  <NvFieldLabel for="dev-site">所属工厂 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="createForm.siteCode">
                    <NvSelectTrigger id="dev-site"><NvSelectValue placeholder="请选择工厂" /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem v-for="s in sites.items.value" :key="s.code" :value="s.code ?? '__none__'">
                        {{ s.displayName ?? s.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.workshopCode)">
                  <NvFieldLabel for="dev-workshop">所属车间 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="createForm.workshopCode">
                    <NvSelectTrigger id="dev-workshop"><NvSelectValue placeholder="请选择车间" /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem v-for="w in workshopOptions" :key="w.code" :value="w.code ?? '__none__'">
                        {{ w.displayName ?? w.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.lineCode)">
                  <NvFieldLabel for="dev-line">所属产线 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="createForm.lineCode">
                    <NvSelectTrigger id="dev-line"><NvSelectValue placeholder="请选择产线" /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem v-for="l in lineOptions" :key="l.code" :value="l.code ?? '__none__'">
                        {{ l.displayName ?? l.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.workCenterCode)">
                  <NvFieldLabel for="dev-wc">所属工作中心 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="createForm.workCenterCode">
                    <NvSelectTrigger id="dev-wc"><NvSelectValue placeholder="请选择工作中心" /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem v-for="w in workCenterOptions" :key="w.code" :value="w.code ?? '__none__'">
                        {{ w.displayName ?? w.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.stationCode)">
                  <NvFieldLabel for="dev-station">所属工位 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvInput id="dev-station" v-model="createForm.stationCode" autocomplete="off" required />
                </NvField>
                <NvField>
                  <NvFieldLabel for="dev-purchase-date">购置日期</NvFieldLabel>
                  <NvInput id="dev-purchase-date" v-model="createForm.purchaseDate" type="date" />
                </NvField>
                <NvField>
                  <NvFieldLabel for="dev-purchase-cost">购置成本</NvFieldLabel>
                  <NvInput id="dev-purchase-cost" v-model="createForm.purchaseCost" type="number" min="0" step="0.01" />
                </NvField>
                <NvField :data-invalid="createShowErrors && Boolean(currencyValidationMessage)">
                  <NvFieldLabel for="dev-currency">币种</NvFieldLabel>
                  <NvInput id="dev-currency" v-model="createForm.purchaseCurrencyCode" autocomplete="off" maxlength="3" />
                  <NvFieldDescription v-if="createShowErrors && currencyValidationMessage">{{ currencyValidationMessage }}</NvFieldDescription>
                </NvField>
                <NvField>
                  <NvFieldLabel for="dev-warranty">保修到期</NvFieldLabel>
                  <NvInput id="dev-warranty" v-model="createForm.warrantyExpiresOn" type="date" />
                </NvField>
                <NvField>
                  <NvFieldLabel for="dev-supplier">供应商编码</NvFieldLabel>
                  <NvInput id="dev-supplier" v-model="createForm.supplierPartnerCode" autocomplete="off" />
                </NvField>
                <NvField>
                  <NvFieldLabel for="dev-parent">父设备编码</NvFieldLabel>
                  <NvInput id="dev-parent" v-model="createForm.parentDeviceId" autocomplete="off" />
                </NvField>
                <NvField>
                  <NvFieldLabel for="dev-retired">退役日期</NvFieldLabel>
                  <NvInput id="dev-retired" v-model="createForm.retiredOn" type="date" />
                </NvField>
                <NvField orientation="horizontal" class="h-fit items-center justify-between gap-3 self-start rounded-lg border px-3 py-2 sm:col-span-2">
                  <NvFieldLabel for="dev-maintainable" class="mb-0">纳入维护计划</NvFieldLabel>
                  <NvCheckbox id="dev-maintainable" v-model:checked="createForm.maintainable" />
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
                <div v-for="(component, index) in createForm.components" :key="index" class="grid gap-3 rounded-md border px-3 py-3 sm:grid-cols-[1fr_1fr_6rem_auto_auto]">
                  <NvField>
                    <NvFieldLabel :for="`dev-component-code-${index}`">部件编码</NvFieldLabel>
                    <NvInput :id="`dev-component-code-${index}`" v-model="component.componentCode" autocomplete="off" />
                  </NvField>
                  <NvField>
                    <NvFieldLabel :for="`dev-component-name-${index}`">部件名称</NvFieldLabel>
                    <NvInput :id="`dev-component-name-${index}`" v-model="component.componentName" autocomplete="off" />
                  </NvField>
                  <NvField :data-invalid="createShowErrors && isComponentReady(component) && componentQuantity(component) <= 0">
                    <NvFieldLabel :for="`dev-component-qty-${index}`">数量</NvFieldLabel>
                    <NvInput :id="`dev-component-qty-${index}`" v-model="component.quantity" type="number" min="0.001" step="0.001" />
                    <NvFieldDescription v-if="createShowErrors && isComponentReady(component) && componentQuantity(component) <= 0">必须大于 0。</NvFieldDescription>
                  </NvField>
                  <NvField orientation="horizontal" class="items-center gap-2 self-end pb-2">
                    <NvCheckbox :id="`dev-component-critical-${index}`" v-model:checked="component.critical" />
                    <NvFieldLabel :for="`dev-component-critical-${index}`" class="mb-0">关键</NvFieldLabel>
                  </NvField>
                  <NvButton class="self-end" size="icon" variant="ghost" type="button" :aria-label="`删除部件 ${index + 1}`" @click="removeComponent(index)">
                    <Trash2Icon aria-hidden="true" />
                  </NvButton>
                </div>
              </div>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="createOpen = false">取消</NvButton>
                <NvButton type="submit" :disabled="devices.createPending.value || deviceActions.updatePending.value || editLoading">
                  <Spinner v-if="devices.createPending.value || deviceActions.updatePending.value" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '保存设备' }}
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="在当前页内筛选设备编码、名称" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="devices.total.value"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :searchable="false" :column-settings="false"
      :columns="columns"
      :rows="listRows"
      :row-key="rowKey"
      :loading="devices.pending.value"
      empty-message="暂无设备。可清空筛选或新建设备。"
    >
      <template #cell-siteCode="{ row }">{{ siteName(row.siteCode) }}</template>
      <template #cell-workshopCode="{ row }">{{ workshopName(row.workshopCode) }}</template>
      <template #cell-lineCode="{ row }">{{ lineName(row.lineCode) }}</template>
      <template #cell-stationCode="{ row }">{{ row.stationCode ?? '无' }}</template>
      <template #cell-warrantyExpiresOn="{ row }">{{ formatDate(row.warrantyExpiresOn) }}</template>
      <template #cell-active="{ row }">
        <NvStatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions :row="row" entity-label="设备" :detail-fields="deviceDetailFields(row)" :actions="deviceActions" @edit="openEdit" />
      </template>
    </NvDataTable>

  </BusinessLayout>
</template>
