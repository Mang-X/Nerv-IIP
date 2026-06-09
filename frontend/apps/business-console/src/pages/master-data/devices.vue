<script setup lang="ts">
import type {
  BusinessConsoleRegisterDeviceAssetRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useMasterDataResource, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Checkbox,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  toast,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '设备台账' } })

const CRITICALITY_OPTIONS = [
  { value: 'high', label: '高（关键设备）' },
  { value: 'medium', label: '中' },
  { value: 'low', label: '低' },
]
const DEVICE_DEFAULTS = {
  capacityUomCode: 'PCS',
  criticality: 'medium',
  maintainable: true,
  telemetryEnabled: false,
}

const devices = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('device-asset')
const lines = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('production-line')
const workCenters = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('work-center')
const deviceActions = useMasterDataResourceActions('device-asset')

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
  lineCode: '',
  workCenterCode: '',
  criticality: DEVICE_DEFAULTS.criticality,
  maintainable: DEVICE_DEFAULTS.maintainable,
})

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '设备编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '设备名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '版本', width: 'w-28', accessor: (r) => r.snapshotVersion ?? '无' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

function deviceDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '设备编码', value: row.code ?? '' },
    { label: '设备名称', value: row.displayName ?? '' },
    { label: '所属产线', value: row.lineCode ?? '' },
    { label: '所属工作中心', value: row.workCenterCode ?? '' },
  ]
}

const deviceActionErrorMessage = computed(() => formatError(deviceActions.actionError.value))

const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return devices.items.value
  return devices.items.value.filter((row) =>
    [row.code, row.displayName, row.snapshotVersion].some((value) => (value ?? '').toLowerCase().includes(kw)),
  )
})
const canCreateDevice = computed(() =>
  [createForm.code, createForm.model, createForm.manufacturer, createForm.serialNo,
    createForm.assetClassCode, createForm.lineCode, createForm.workCenterCode, createForm.criticality].every(isNonEmpty),
)
const createErrorMessage = computed(() => formatError(devices.createError.value))
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
function refreshAll() {
  void devices.refresh()
  void lines.refresh()
  void workCenters.refresh()
}
function resetCreateForm() {
  Object.assign(createForm, {
    code: '', model: '', manufacturer: '', serialNo: '', assetClassCode: '',
    lineCode: '', workCenterCode: '', criticality: DEVICE_DEFAULTS.criticality, maintainable: DEVICE_DEFAULTS.maintainable,
  })
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
      lineCode: d?.lineCode ?? row.lineCode ?? '',
      workCenterCode: d?.workCenterCode ?? row.workCenterCode ?? '',
      criticality: d?.criticality ?? DEVICE_DEFAULTS.criticality,
      maintainable: d?.maintainable ?? DEVICE_DEFAULTS.maintainable,
    })
  }
  finally {
    editLoading.value = false
  }
}
async function submitDevice() {
  if (!canCreateDevice.value) {
    createShowErrors.value = true
    return
  }
  if (editingCode.value) {
    await deviceActions.update(editingCode.value, {
      name: createForm.model.trim(),
      model: createForm.model.trim(),
      manufacturer: createForm.manufacturer.trim(),
      serialNo: createForm.serialNo.trim(),
      assetClassCode: createForm.assetClassCode.trim(),
      lineCode: createForm.lineCode.trim(),
      workCenterCode: createForm.workCenterCode.trim(),
      capacityUomCode: DEVICE_DEFAULTS.capacityUomCode,
      criticality: createForm.criticality,
      maintainable: createForm.maintainable,
      telemetryEnabled: DEVICE_DEFAULTS.telemetryEnabled,
    })
    toast.success(`设备「${createForm.model.trim()}」已更新。`)
  }
  else {
    await devices.create({
      organizationId: devices.filters.organizationId,
      environmentId: devices.filters.environmentId,
      code: createForm.code.trim(),
      model: createForm.model.trim(),
      manufacturer: createForm.manufacturer.trim(),
      serialNo: createForm.serialNo.trim(),
      assetClassCode: createForm.assetClassCode.trim(),
      lineCode: createForm.lineCode.trim(),
      workCenterCode: createForm.workCenterCode.trim(),
      capacityUomCode: DEVICE_DEFAULTS.capacityUomCode,
      criticality: createForm.criticality,
      maintainable: createForm.maintainable,
      telemetryEnabled: DEVICE_DEFAULTS.telemetryEnabled,
    })
    toast.success(`设备「${createForm.model.trim()}」已登记。`)
  }
  resetCreateForm()
  editingCode.value = null
  createShowErrors.value = false
  createOpen.value = false
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="设备台账" :breadcrumbs="[{ label: '基础数据' }]" :count="`${devices.total.value} 台设备`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="devices.pending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Dialog v-model:open="createOpen">
          <DialogTrigger as-child>
            <Button size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建设备
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{{ editingCode ? `编辑设备 · ${editingCode}` : '新建设备' }}</DialogTitle>
              <DialogDescription>{{ editingCode ? '修改设备档案（编码不可修改）。带 * 为必填项。' : '为产线与工作中心登记一台设备资产。带 * 为必填项。' }}</DialogDescription>
            </DialogHeader>
            <form class="grid gap-4" @submit.prevent="submitDevice">
              <p v-if="createErrorMessage" class="text-sm text-destructive" role="alert">{{ createErrorMessage }}</p>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.code)">
                  <FieldLabel for="dev-code">设备编码 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="dev-code" v-model="createForm.code" autocomplete="off" :disabled="!!editingCode" required />
                </Field>
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.model)">
                  <FieldLabel for="dev-model">设备型号 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="dev-model" v-model="createForm.model" autocomplete="off" required />
                </Field>
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.manufacturer)">
                  <FieldLabel for="dev-maker">制造商 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="dev-maker" v-model="createForm.manufacturer" autocomplete="off" required />
                </Field>
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.serialNo)">
                  <FieldLabel for="dev-serial">出厂序列号 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="dev-serial" v-model="createForm.serialNo" autocomplete="off" required />
                </Field>
                <Field :data-invalid="createShowErrors && !isNonEmpty(createForm.assetClassCode)">
                  <FieldLabel for="dev-class">设备类别 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="dev-class" v-model="createForm.assetClassCode" autocomplete="off" required />
                  <FieldDescription>填写「数据字典」中维护的设备类别编码。</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel for="dev-criticality">关键度 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.criticality">
                    <SelectTrigger id="dev-criticality"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in CRITICALITY_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="dev-line">所属产线 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.lineCode">
                    <SelectTrigger id="dev-line"><SelectValue placeholder="请选择产线" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="l in lines.items.value" :key="l.code" :value="l.code ?? ''">
                        {{ l.displayName ?? l.code }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="dev-wc">所属工作中心 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="createForm.workCenterCode">
                    <SelectTrigger id="dev-wc"><SelectValue placeholder="请选择工作中心" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="w in workCenters.items.value" :key="w.code" :value="w.code ?? ''">
                        {{ w.displayName ?? w.code }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </Field>
                <Field orientation="horizontal" class="h-fit items-center justify-between gap-3 self-start rounded-lg border px-3 py-2 sm:col-span-2">
                  <FieldLabel for="dev-maintainable" class="mb-0">纳入维护计划</FieldLabel>
                  <Checkbox id="dev-maintainable" v-model:checked="createForm.maintainable" />
                </Field>
              </FieldGroup>
              <DialogFooter>
                <Button type="button" variant="outline" @click="createOpen = false">取消</Button>
                <Button type="submit" :disabled="devices.createPending.value || deviceActions.updatePending.value || editLoading">
                  <Spinner v-if="devices.createPending.value || deviceActions.updatePending.value" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '保存设备' }}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">层级：工厂 → 车间 → 产线 → 工作中心 → 设备（设备归属产线与工作中心）</p>

    <SectionCards :columns="3">
      <SectionCard description="设备数" :value="devices.total.value" hint="后端分页总数" />
      <SectionCard description="关联产线" :value="lines.total.value" hint="可挂载设备的产线" />
      <SectionCard description="关联工作中心" :value="workCenters.total.value" hint="可挂载设备的工作中心" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="在当前页内筛选设备编码、名称" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>
    <p v-else-if="deviceActionErrorMessage" class="text-sm text-destructive" role="alert">{{ deviceActionErrorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="listRows"
      :row-key="rowKey"
      :loading="devices.pending.value"
      empty-message="暂无设备。可清空筛选或新建设备。"
    >
      <template #cell-active="{ row }">
        <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions :row="row" entity-label="设备" :detail-fields="deviceDetailFields(row)" :actions="deviceActions" @edit="openEdit" />
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="devices.total.value" />
  </BusinessLayout>
</template>
