<script setup lang="ts">
import type {
  BusinessConsoleRegisterDeviceAssetRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useBusinessWorkshops, useMasterDataResource, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  CheckboxPro,
  DataTablePro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DialogProTrigger,
  FieldPro,
  FieldProDescription,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  Toolbar,
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

const columns: DataTableProColumn<BusinessConsoleResourceItem>[] = [
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
    createForm.lineCode, createForm.workCenterCode, createForm.stationCode, createForm.criticality].every(isNonEmpty),
)
const listErrorMessage = computed(() => formatError(devices.error.value))

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
function componentPayload(): NonNullable<BusinessConsoleRegisterDeviceAssetRequest['components']> {
  return createForm.components
    .map((component) => ({
      componentCode: component.componentCode.trim(),
      componentName: component.componentName.trim(),
      quantity: optionalNumber(component.quantity) ?? 1,
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
    purchaseCurrencyCode: optionalText(createForm.purchaseCurrencyCode),
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
    <PageHeader title="设备台账" :breadcrumbs="[{ label: '基础数据' }]" :count="`${devices.total.value} 台设备`">
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="devices.pending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="createOpen">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建设备
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>{{ editingCode ? `编辑设备 · ${editingCode}` : '新建设备' }}</DialogProTitle>
              <DialogProDescription>{{ editingCode ? '修改设备档案（编码不可修改）。带 * 为必填项。' : '为产线与工作中心登记一台设备资产。带 * 为必填项。' }}</DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-4" @submit.prevent="submitDevice">
              <p v-if="createShowErrors && !canCreateDevice" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro v-if="editingCode">
                  <FieldProLabel for="dev-code">设备编码</FieldProLabel>
                  <InputPro id="dev-code" :model-value="createForm.code" disabled />
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.model)">
                  <FieldProLabel for="dev-model">设备型号 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="dev-model" v-model="createForm.model" autocomplete="off" required />
                  <FieldProDescription v-if="!editingCode">编码由系统自动生成。</FieldProDescription>
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.manufacturer)">
                  <FieldProLabel for="dev-maker">制造商 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="dev-maker" v-model="createForm.manufacturer" autocomplete="off" required />
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.serialNo)">
                  <FieldProLabel for="dev-serial">出厂序列号 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="dev-serial" v-model="createForm.serialNo" autocomplete="off" required />
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.assetClassCode)">
                  <FieldProLabel for="dev-class">设备类别 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="dev-class" v-model="createForm.assetClassCode" autocomplete="off" required />
                  <FieldProDescription>填写「数据字典」中维护的设备类别编码。</FieldProDescription>
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.criticality)">
                  <FieldProLabel for="dev-criticality">关键度 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="createForm.criticality">
                    <SelectProTrigger id="dev-criticality"><SelectProValue /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="o in CRITICALITY_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.siteCode)">
                  <FieldProLabel for="dev-site">所属工厂 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="createForm.siteCode">
                    <SelectProTrigger id="dev-site"><SelectProValue placeholder="请选择工厂" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="s in sites.items.value" :key="s.code" :value="s.code ?? '__none__'">
                        {{ s.displayName ?? s.code }}
                      </SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.workshopCode)">
                  <FieldProLabel for="dev-workshop">所属车间 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="createForm.workshopCode">
                    <SelectProTrigger id="dev-workshop"><SelectProValue placeholder="请选择车间" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="w in workshopOptions" :key="w.code" :value="w.code ?? '__none__'">
                        {{ w.displayName ?? w.code }}
                      </SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.lineCode)">
                  <FieldProLabel for="dev-line">所属产线 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="createForm.lineCode">
                    <SelectProTrigger id="dev-line"><SelectProValue placeholder="请选择产线" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="l in lineOptions" :key="l.code" :value="l.code ?? '__none__'">
                        {{ l.displayName ?? l.code }}
                      </SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.workCenterCode)">
                  <FieldProLabel for="dev-wc">所属工作中心 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="createForm.workCenterCode">
                    <SelectProTrigger id="dev-wc"><SelectProValue placeholder="请选择工作中心" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="w in workCenterOptions" :key="w.code" :value="w.code ?? '__none__'">
                        {{ w.displayName ?? w.code }}
                      </SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </FieldPro>
                <FieldPro :data-invalid="createShowErrors && !isNonEmpty(createForm.stationCode)">
                  <FieldProLabel for="dev-station">所属工位 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="dev-station" v-model="createForm.stationCode" autocomplete="off" required />
                </FieldPro>
                <FieldPro>
                  <FieldProLabel for="dev-purchase-date">购置日期</FieldProLabel>
                  <InputPro id="dev-purchase-date" v-model="createForm.purchaseDate" type="date" />
                </FieldPro>
                <FieldPro>
                  <FieldProLabel for="dev-purchase-cost">购置成本</FieldProLabel>
                  <InputPro id="dev-purchase-cost" v-model="createForm.purchaseCost" type="number" min="0" step="0.01" />
                </FieldPro>
                <FieldPro>
                  <FieldProLabel for="dev-currency">币种</FieldProLabel>
                  <InputPro id="dev-currency" v-model="createForm.purchaseCurrencyCode" autocomplete="off" maxlength="3" />
                </FieldPro>
                <FieldPro>
                  <FieldProLabel for="dev-warranty">保修到期</FieldProLabel>
                  <InputPro id="dev-warranty" v-model="createForm.warrantyExpiresOn" type="date" />
                </FieldPro>
                <FieldPro>
                  <FieldProLabel for="dev-supplier">供应商编码</FieldProLabel>
                  <InputPro id="dev-supplier" v-model="createForm.supplierPartnerCode" autocomplete="off" />
                </FieldPro>
                <FieldPro>
                  <FieldProLabel for="dev-parent">父设备编码</FieldProLabel>
                  <InputPro id="dev-parent" v-model="createForm.parentDeviceId" autocomplete="off" />
                </FieldPro>
                <FieldPro>
                  <FieldProLabel for="dev-retired">退役日期</FieldProLabel>
                  <InputPro id="dev-retired" v-model="createForm.retiredOn" type="date" />
                </FieldPro>
                <FieldPro orientation="horizontal" class="h-fit items-center justify-between gap-3 self-start rounded-lg border px-3 py-2 sm:col-span-2">
                  <FieldProLabel for="dev-maintainable" class="mb-0">纳入维护计划</FieldProLabel>
                  <CheckboxPro id="dev-maintainable" v-model:checked="createForm.maintainable" />
                </FieldPro>
              </FieldProGroup>
              <div class="grid gap-3">
                <div class="flex items-center justify-between gap-3">
                  <FieldProLabel>部件结构</FieldProLabel>
                  <ButtonPro size="sm" variant="outline" type="button" @click="addComponent">
                    <PlusIcon aria-hidden="true" />
                    添加部件
                  </ButtonPro>
                </div>
                <div v-for="(component, index) in createForm.components" :key="index" class="grid gap-3 rounded-md border px-3 py-3 sm:grid-cols-[1fr_1fr_6rem_auto_auto]">
                  <FieldPro>
                    <FieldProLabel :for="`dev-component-code-${index}`">部件编码</FieldProLabel>
                    <InputPro :id="`dev-component-code-${index}`" v-model="component.componentCode" autocomplete="off" />
                  </FieldPro>
                  <FieldPro>
                    <FieldProLabel :for="`dev-component-name-${index}`">部件名称</FieldProLabel>
                    <InputPro :id="`dev-component-name-${index}`" v-model="component.componentName" autocomplete="off" />
                  </FieldPro>
                  <FieldPro>
                    <FieldProLabel :for="`dev-component-qty-${index}`">数量</FieldProLabel>
                    <InputPro :id="`dev-component-qty-${index}`" v-model="component.quantity" type="number" min="0.001" step="0.001" />
                  </FieldPro>
                  <FieldPro orientation="horizontal" class="items-center gap-2 self-end pb-2">
                    <CheckboxPro :id="`dev-component-critical-${index}`" v-model:checked="component.critical" />
                    <FieldProLabel :for="`dev-component-critical-${index}`" class="mb-0">关键</FieldProLabel>
                  </FieldPro>
                  <ButtonPro class="self-end" size="icon" variant="ghost" type="button" :aria-label="`删除部件 ${index + 1}`" @click="removeComponent(index)">
                    <Trash2Icon aria-hidden="true" />
                  </ButtonPro>
                </div>
              </div>
              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="createOpen = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="devices.createPending.value || deviceActions.updatePending.value || editLoading">
                  <Spinner v-if="devices.createPending.value || deviceActions.updatePending.value" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '保存设备' }}
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <Toolbar v-model:search="keyword" search-placeholder="在当前页内筛选设备编码、名称" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTablePro
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
        <StatusBadgePro :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions :row="row" entity-label="设备" :detail-fields="deviceDetailFields(row)" :actions="deviceActions" @edit="openEdit" />
      </template>
    </DataTablePro>

  </BusinessLayout>
</template>
