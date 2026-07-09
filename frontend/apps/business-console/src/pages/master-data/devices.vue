<script setup lang="ts">
import type {
  BusinessConsoleRegisterDeviceAssetRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import {
  useMasterDataResource,
  useMasterDataResourceActions,
} from '@/composables/useBusinessMasterData'
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
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备台账',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

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
}

const devices = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('device-asset')
const lines = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('production-line')
const workCenters = useMasterDataResource<BusinessConsoleRegisterDeviceAssetRequest>('work-center')
const deviceActions = useMasterDataResourceActions('device-asset')

// 列表回传的是 lineCode/workCenterCode（编码）；解析成名称显示（取自产线/工作中心实体，找不到回退编码）。
const lineNameByCode = computed(
  () => new Map(lines.items.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])),
)
const wcNameByCode = computed(
  () => new Map(workCenters.items.value.map((r) => [r.code ?? '', r.displayName ?? r.code ?? ''])),
)
function lineName(code?: string | null) {
  return code ? (lineNameByCode.value.get(code) ?? code) : '无'
}
function wcName(code?: string | null) {
  return code ? (wcNameByCode.value.get(code) ?? code) : '无'
}

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

const columns: NvDataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '设备编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '设备名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'lineCode', header: '所属产线', width: 'w-32', accessor: (r) => lineName(r.lineCode) },
  {
    key: 'workCenterCode',
    header: '所属工作中心',
    width: 'w-36',
    accessor: (r) => wcName(r.workCenterCode),
  },
  { key: 'active', header: '状态', width: 'w-24' },
  {
    key: 'snapshotVersion',
    header: '更新时间',
    width: 'w-40',
    accessor: (r) => formatDateTime(r.snapshotVersion),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

function deviceDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '设备编码', value: row.code ?? '' },
    { label: '设备名称', value: row.displayName ?? '' },
    { label: '所属产线', value: lineName(row.lineCode) },
    { label: '所属工作中心', value: wcName(row.workCenterCode) },
  ]
}

const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return devices.items.value
  return devices.items.value.filter((row) =>
    [row.code, row.displayName, row.snapshotVersion].some((value) =>
      (value ?? '').toLowerCase().includes(kw),
    ),
  )
})
const canCreateDevice = computed(() =>
  [
    createForm.model,
    createForm.manufacturer,
    createForm.serialNo,
    createForm.assetClassCode,
    createForm.lineCode,
    createForm.workCenterCode,
    createForm.criticality,
  ].every(isNonEmpty),
)
const listErrorMessage = computed(() => formatError(devices.error.value))

watch(createOpen, (open) => {
  if (open) createShowErrors.value = false
})
watch([keyword, pageSize], () => {
  page.value = 1
})
watch(
  [page, pageSize],
  () => {
    devices.filters.skip = (page.value - 1) * (Number(pageSize.value) || 10)
    devices.filters.take = Number(pageSize.value) || 10
  },
  { immediate: true },
)

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
  } finally {
    editLoading.value = false
  }
}
async function submitDevice() {
  if (!canCreateDevice.value) {
    createShowErrors.value = true
    return
  }
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
        capacityUomCode: DEVICE_DEFAULTS.capacityUomCode,
        criticality: createForm.criticality,
        maintainable: createForm.maintainable,
        telemetryEnabled: DEVICE_DEFAULTS.telemetryEnabled,
      })
      notifySuccess(`设备「${createForm.model.trim()}」已更新。`)
    } else {
      await devices.create({
        organizationId: devices.filters.organizationId,
        environmentId: devices.filters.environmentId,
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
      notifySuccess(`设备「${createForm.model.trim()}」已登记。`)
    }
    resetCreateForm()
    editingCode.value = null
    createShowErrors.value = false
    createOpen.value = false
  } catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="设备台账"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${devices.total.value} 台设备`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="devices.pending.value"
          @click="refreshAll"
        >
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
              <NvDialogTitle>{{
                editingCode ? `编辑设备 · ${editingCode}` : '新建设备'
              }}</NvDialogTitle>
              <NvDialogDescription>{{
                editingCode
                  ? '修改设备档案（编码不可修改）。带 * 为必填项。'
                  : '为产线与工作中心登记一台设备资产。带 * 为必填项。'
              }}</NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-4" @submit.prevent="submitDevice">
              <p
                v-if="createShowErrors && !canCreateDevice"
                class="text-sm text-destructive"
                role="alert"
              >
                请完整填写带 * 的必填项（已标红）。
              </p>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField v-if="editingCode">
                  <NvFieldLabel for="dev-code">设备编码</NvFieldLabel>
                  <NvInput id="dev-code" :model-value="createForm.code" disabled />
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.model)">
                  <NvFieldLabel for="dev-model"
                    >设备型号 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput id="dev-model" v-model="createForm.model" autocomplete="off" required />
                  <NvFieldDescription v-if="!editingCode">编码由系统自动生成。</NvFieldDescription>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.manufacturer)">
                  <NvFieldLabel for="dev-maker"
                    >制造商 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="dev-maker"
                    v-model="createForm.manufacturer"
                    autocomplete="off"
                    required
                  />
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.serialNo)">
                  <NvFieldLabel for="dev-serial"
                    >出厂序列号 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="dev-serial"
                    v-model="createForm.serialNo"
                    autocomplete="off"
                    required
                  />
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.assetClassCode)">
                  <NvFieldLabel for="dev-class"
                    >设备类别 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="dev-class"
                    v-model="createForm.assetClassCode"
                    autocomplete="off"
                    required
                  />
                  <NvFieldDescription>填写「数据字典」中维护的设备类别编码。</NvFieldDescription>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.criticality)">
                  <NvFieldLabel for="dev-criticality"
                    >关键度 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="createForm.criticality">
                    <NvSelectTrigger id="dev-criticality"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="o in CRITICALITY_OPTIONS"
                        :key="o.value"
                        :value="o.value"
                        >{{ o.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.lineCode)">
                  <NvFieldLabel for="dev-line"
                    >所属产线 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="createForm.lineCode">
                    <NvSelectTrigger id="dev-line"
                      ><NvSelectValue placeholder="请选择产线"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="l in lines.items.value"
                        :key="l.code"
                        :value="l.code ?? '__none__'"
                      >
                        {{ l.displayName ?? l.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="createShowErrors && !isNonEmpty(createForm.workCenterCode)">
                  <NvFieldLabel for="dev-wc"
                    >所属工作中心 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect v-model="createForm.workCenterCode">
                    <NvSelectTrigger id="dev-wc"
                      ><NvSelectValue placeholder="请选择工作中心"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="w in workCenters.items.value"
                        :key="w.code"
                        :value="w.code ?? '__none__'"
                      >
                        {{ w.displayName ?? w.code }}
                      </NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField
                  orientation="horizontal"
                  class="h-fit items-center justify-between gap-3 self-start rounded-lg border px-3 py-2 sm:col-span-2"
                >
                  <NvFieldLabel for="dev-maintainable" class="mb-0">纳入维护计划</NvFieldLabel>
                  <NvCheckbox id="dev-maintainable" v-model:checked="createForm.maintainable" />
                </NvField>
              </NvFieldGroup>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="createOpen = false"
                  >取消</NvButton
                >
                <NvButton
                  type="submit"
                  :disabled="
                    devices.createPending.value || deviceActions.updatePending.value || editLoading
                  "
                >
                  <Spinner
                    v-if="devices.createPending.value || deviceActions.updatePending.value"
                    aria-hidden="true"
                  />
                  {{ editingCode ? '保存修改' : '保存设备' }}
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="在当前页内筛选设备编码、名称" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="devices.total.value"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :searchable="false"
      :column-settings="false"
      :columns="columns"
      :rows="listRows"
      :row-key="rowKey"
      :loading="devices.pending.value"
      empty-message="暂无设备。可清空筛选或新建设备。"
    >
      <template #cell-lineCode="{ row }">{{ lineName(row.lineCode) }}</template>
      <template #cell-workCenterCode="{ row }">{{ wcName(row.workCenterCode) }}</template>
      <template #cell-active="{ row }">
        <NvStatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-actions="{ row }">
        <MasterDataRowActions
          :row="row"
          entity-label="设备"
          :detail-fields="deviceDetailFields(row)"
          :actions="deviceActions"
          @edit="openEdit"
        />
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
