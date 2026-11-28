<script setup lang="ts">
import {
  createBusinessConsoleQualityMeasuringDevice,
  recordBusinessConsoleQualityMeasuringDeviceCalibration,
} from '@nerv-iip/api-client'
import { hasBusinessContext } from '@/composables/shared/businessContextBinding'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  calibrationRemainingText,
  calibrationStatePresentation,
  measuringDeviceStatusLabel,
  useQualityCalibrationRecords,
  useQualityMeasuringDevices,
  type QualityCalibrationRecordItem,
  type QualityMeasuringDeviceItem,
} from '@/composables/quality/useBusinessQualityLedgers'
import { usePagedList } from '@/composables/shared/usePagedList'
import { formatDate, formatDateTime } from '@/utils/format'
import {
  friendlyErrorMessage,
  notifyOperationFailure,
  notifySuccess,
  notifyWarning,
} from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvFieldLabel,
  NvField,
  NvDialogTitle,
  NvDialogHeader,
  NvDialogFooter,
  NvDialogDescription,
  NvDialogContent,
  NvDialog,
  NvMetricCard,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import {
  CalendarClockIcon,
  CircleSlashIcon,
  RefreshCwIcon,
  ShieldCheckIcon,
  TriangleAlertIcon,
  XIcon,
} from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '计量与校准',
    requiredPermissions: ['business.quality.inspection-records.read'],
  },
})

const {
  filters: deviceFilters,
  measuringDevices,
  measuringDevicesError,
  measuringDevicesPending,
  measuringDevicesTotal,
  measuringDeviceCurrentCount,
  measuringDeviceWarningCount,
  measuringDeviceOverdueCount,
  measuringDeviceUnavailableCount,
  refreshMeasuringDevices,
} = useQualityMeasuringDevices()
const {
  filters: recordFilters,
  calibrationRecords,
  calibrationRecordsError,
  calibrationRecordsPending,
  calibrationRecordsTotal,
  refreshCalibrationRecords,
} = useQualityCalibrationRecords()

const devicePaging = usePagedList(deviceFilters, {
  initialPageSize: '20',
  resetOn: [
    () => deviceFilters.calibrationState,
    () => deviceFilters.status,
    () => deviceFilters.keyword,
  ],
})
const recordPaging = usePagedList(recordFilters, {
  initialPageSize: '20',
  resetOn: [() => recordFilters.measuringDeviceId, () => recordFilters.keyword],
})

const calibrationStateOptions = [
  { label: '全部校准状态', value: 'all' },
  { label: '有效期内', value: 'current' },
  { label: '临近到期', value: 'warning' },
  { label: '已过期', value: 'overdue' },
  { label: '不参与校准', value: 'unavailable' },
]
const deviceStatusOptions = [
  { label: '全部器具状态', value: 'all' },
  { label: '在用', value: 'in-use' },
  { label: '送检中', value: 'calibration' },
  { label: '停用', value: 'disabled' },
  { label: '报废', value: 'retired' },
]

const calibrationStateFilter = computed({
  get: () => deviceFilters.calibrationState || 'all',
  set: (value: string) => {
    deviceFilters.calibrationState = value === 'all' ? undefined : value
  },
})
const deviceStatusFilter = computed({
  get: () => deviceFilters.status || 'all',
  set: (value: string) => {
    deviceFilters.status = value === 'all' ? undefined : value
  },
})
const deviceKeyword = computed({
  get: () => deviceFilters.keyword ?? '',
  set: (value: string) => {
    deviceFilters.keyword = value.trim() ? value : undefined
  },
})
const recordKeyword = computed({
  get: () => recordFilters.keyword ?? '',
  set: (value: string) => {
    recordFilters.keyword = value.trim() ? value : undefined
  },
})

// 点器具行即把下方流水收窄到该器具；再点一次「全部器具」放开。
const selectedDevice = shallowRef<QualityMeasuringDeviceItem>()
watch(selectedDevice, (device) => {
  recordFilters.measuringDeviceId = device?.measuringDeviceId || undefined
})
function selectDevice(device: QualityMeasuringDeviceItem) {
  selectedDevice.value =
    selectedDevice.value?.measuringDeviceId === device.measuringDeviceId ? undefined : device
}
const selectedDeviceLabel = computed(
  () => selectedDevice.value?.deviceCode?.trim() || selectedDevice.value?.measuringDeviceId || '',
)

const deviceErrorMessage = computed(() =>
  measuringDevicesError.value
    ? friendlyErrorMessage(measuringDevicesError.value, '计量器具台账加载失败，请稍后重试。')
    : '',
)
const recordErrorMessage = computed(() =>
  calibrationRecordsError.value
    ? friendlyErrorMessage(calibrationRecordsError.value, '校准记录加载失败，请稍后重试。')
    : '',
)

const deviceColumns: NvDataTableColumn<QualityMeasuringDeviceItem>[] = [
  { key: 'deviceCode', header: '器具编码', width: 'w-28', cellClass: 'font-medium' },
  {
    key: 'deviceType',
    header: '类别',
    width: 'w-20',
    accessor: (row) => row.deviceType?.trim() || '未分类',
  },
  {
    key: 'accuracy',
    header: '规格与精度',
    width: 'w-24',
    accessor: (row) => row.accuracy?.trim() || '未登记',
  },
  {
    key: 'calibrationIntervalDays',
    header: '检定周期',
    align: 'end',
    width: 'w-20',
    accessor: (row) =>
      typeof row.calibrationIntervalDays === 'number' ? `${row.calibrationIntervalDays} 天` : '无',
  },
  { key: 'status', header: '器具状态', width: 'w-20' },
  {
    key: 'lastCalibratedAtUtc',
    header: '末次校准',
    width: 'w-24',
    accessor: (row) => formatDate(row.lastCalibratedAtUtc),
  },
  {
    key: 'calibrationDueAtUtc',
    header: '下次到期',
    width: 'w-24',
    accessor: (row) => formatDate(row.calibrationDueAtUtc),
  },
  { key: 'calibrationState', header: '校准状态', width: 'w-24' },
  {
    key: 'daysUntilDue',
    header: '剩余天数',
    align: 'end',
    width: 'w-20',
    accessor: (row) => calibrationRemainingText(row.daysUntilDue),
  },
  {
    key: 'latestCalibrationNo',
    header: '末次证书号',
    width: 'w-32',
    cellClass: 'whitespace-normal wrap-anywhere',
    accessor: (row) => row.latestCalibrationNo?.trim() || '无',
  },
  {
    key: 'latestCalibrationProvider',
    header: '校准机构',
    width: 'w-36',
    cellClass: 'whitespace-normal wrap-anywhere',
    accessor: (row) => row.latestCalibrationProvider?.trim() || '无',
  },
]

const recordColumns: NvDataTableColumn<QualityCalibrationRecordItem>[] = [
  { key: 'calibrationNo', header: '证书号', cellClass: 'font-medium' },
  { key: 'deviceCode', header: '器具编码' },
  { key: 'deviceType', header: '器具类别', accessor: (row) => row.deviceType?.trim() || '未分类' },
  {
    key: 'calibratedAtUtc',
    header: '校准时间',
    accessor: (row) => formatDateTime(row.calibratedAtUtc),
  },
  {
    key: 'calibrationProvider',
    header: '校准机构',
    accessor: (row) => row.calibrationProvider?.trim() || '无',
  },
  {
    key: 'nextCalibrationDueAtUtc',
    header: '下次到期',
    accessor: (row) => formatDate(row.nextCalibrationDueAtUtc),
  },
]

function deviceRowKey(row: QualityMeasuringDeviceItem) {
  return row.measuringDeviceId ?? row.deviceCode ?? '未知'
}
function recordRowKey(row: QualityCalibrationRecordItem) {
  return row.calibrationRecordId ?? row.calibrationNo ?? '未知'
}
function deviceRowClass(row: QualityMeasuringDeviceItem) {
  return selectedDevice.value?.measuringDeviceId === row.measuringDeviceId
    ? 'cursor-pointer bg-muted/60'
    : 'cursor-pointer'
}
function refreshAll() {
  void refreshMeasuringDevices()
  void refreshCalibrationRecords()
}
const auth = useAuthStore()
const canManage = computed(() =>
  auth.principal?.permissionCodes?.includes(P.qualityMeasuringDevicesManage),
)
const writeMode = shallowRef<'device' | 'calibration'>('device')
const writeOpen = shallowRef(false)
const saving = shallowRef(false)
const writeTarget = shallowRef<QualityMeasuringDeviceItem>()
const writeForm = reactive({
  deviceCode: '',
  deviceType: '',
  accuracy: '',
  interval: '',
  calibrationNo: '',
  provider: '',
  calibratedOn: '',
})
function openWrite(mode: 'device' | 'calibration') {
  writeMode.value = mode
  writeTarget.value = selectedDevice.value
  Object.assign(writeForm, {
    deviceCode: '',
    deviceType: '',
    accuracy: '',
    interval: '',
    calibrationNo: '',
    provider: '',
    calibratedOn: '',
  })
  writeOpen.value = true
}
async function submitWrite() {
  if (!canManage.value || saving.value || !hasBusinessContext(deviceFilters)) return
  const { organizationId, environmentId } = deviceFilters
  const date = new Date(`${writeForm.calibratedOn}T00:00:00`)
  if (Number.isNaN(date.getTime())) return
  const calibratedAtUtc = date.toISOString()
  const isDevice = writeMode.value === 'device'
  saving.value = true
  try {
    if (isDevice) {
      const interval = Number(writeForm.interval)
      if (
        !Number.isInteger(interval) ||
        interval <= 0 ||
        !writeForm.deviceType.trim() ||
        !writeForm.accuracy.trim()
      ) {
        notifyWarning('请填写器具类型、准确度和正整数校准周期。')
        return
      }
      const { data } = await createBusinessConsoleQualityMeasuringDevice({
        body: {
          organizationId,
          environmentId,
          calibratedAtUtc,
          deviceCode: writeForm.deviceCode.trim() || undefined,
          deviceType: writeForm.deviceType.trim(),
          accuracy: writeForm.accuracy.trim(),
          calibrationIntervalDays: interval,
        },
        throwOnError: true,
      })
      if (!data.success || !data.data?.measuringDeviceId)
        throw new Error(data.message || '建档失败。')
    } else {
      const measuringDeviceId = writeTarget.value?.measuringDeviceId
      if (!measuringDeviceId || !writeForm.calibrationNo.trim() || !writeForm.provider.trim()) {
        notifyWarning('请选择计量器具并填写校准编号和机构。')
        return
      }
      const { data } = await recordBusinessConsoleQualityMeasuringDeviceCalibration({
        path: { measuringDeviceId },
        body: {
          organizationId,
          environmentId,
          calibratedAtUtc,
          calibrationNo: writeForm.calibrationNo.trim(),
          calibrationProvider: writeForm.provider.trim(),
        },
        throwOnError: true,
      })
      if (!data.success || !data.data?.accepted) throw new Error(data.message || '登记失败。')
    }
    writeOpen.value = false
    notifySuccess(isDevice ? '计量器具已建档。' : '校准记录已登记。')
    refreshAll()
  } catch (error) {
    notifyOperationFailure('保存失败', error, '保存失败，请稍后重试。')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="计量与校准"
      :breadcrumbs="[{ label: '质量管理' }]"
      :count="deviceErrorMessage ? '台账加载失败' : `${measuringDevicesTotal} 件计量器具`"
    >
      <template #actions>
        <NvButton v-if="canManage" size="sm" type="button" @click="openWrite('device')">
          新建计量器具
        </NvButton>
        <NvButton
          v-if="canManage"
          size="sm"
          type="button"
          variant="outline"
          :disabled="!selectedDevice?.measuringDeviceId"
          @click="openWrite('calibration')"
        >
          登记校准记录
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="measuringDevicesPending || calibrationRecordsPending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <NvMetricCard
        variant="icon"
        label="有效期内"
        :value="measuringDeviceCurrentCount"
        unit="件"
        tone="success"
        :icon="ShieldCheckIcon"
      />
      <NvMetricCard
        variant="icon"
        label="临近到期"
        :value="measuringDeviceWarningCount"
        unit="件"
        tone="warning"
        :icon="CalendarClockIcon"
      />
      <NvMetricCard
        variant="icon"
        label="已过期"
        :value="measuringDeviceOverdueCount"
        unit="件"
        tone="danger"
        :icon="TriangleAlertIcon"
      />
      <NvMetricCard
        variant="icon"
        label="停用报废"
        :value="measuringDeviceUnavailableCount"
        unit="件"
        tone="neutral"
        :icon="CircleSlashIcon"
      />
    </div>

    <NvToolbar
      v-model:search="deviceKeyword"
      search-placeholder="搜索器具编码 / 类别"
      search-label="搜索计量器具"
    >
      <template #filters>
        <NvSelect v-model="calibrationStateFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="校准状态">
            <NvSelectValue />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in calibrationStateOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="deviceStatusFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="器具状态">
            <NvSelectValue />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in deviceStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p
      v-if="deviceErrorMessage"
      class="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive"
      role="alert"
    >
      {{ deviceErrorMessage }}
    </p>

    <NvDataTable
      v-else
      manual
      :page="devicePaging.page.value"
      :page-size="devicePaging.pageSize.value"
      :total-items="measuringDevicesTotal"
      :columns="deviceColumns"
      :rows="measuringDevices"
      :row-key="deviceRowKey"
      :row-class="deviceRowClass"
      :loading="measuringDevicesPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围内没有计量器具。计量管理员可新建器具并登记校准周期。"
      @update:page="devicePaging.page.value = $event"
      @update:page-size="(value) => (devicePaging.pageSize.value = String(value))"
      @row-click="selectDevice"
    >
      <template #cell-status="{ row }">
        <NvStatusBadge :value="row.status" :label="measuringDeviceStatusLabel(row.status)" />
      </template>
      <template #cell-calibrationState="{ row }">
        <NvStatusBadge
          :value="row.calibrationState"
          :label="calibrationStatePresentation(row.calibrationState).label"
          :tone="calibrationStatePresentation(row.calibrationState).tone"
        />
      </template>
    </NvDataTable>

    <section class="grid gap-3">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <h2 class="text-base font-semibold text-foreground">校准记录流水</h2>
        <p class="text-sm text-muted-foreground">
          {{
            selectedDeviceLabel
              ? `仅看器具 ${selectedDeviceLabel} 的校准证书`
              : '点击上方器具行可只看该器具的校准证书'
          }}
        </p>
      </div>

      <NvToolbar
        v-model:search="recordKeyword"
        search-placeholder="搜索证书号 / 校准机构"
        search-label="搜索校准记录"
      >
        <template #filters>
          <NvButton
            v-if="selectedDeviceLabel"
            size="sm"
            type="button"
            variant="outline"
            @click="selectedDevice = undefined"
          >
            <XIcon aria-hidden="true" />
            查看全部器具
          </NvButton>
        </template>
      </NvToolbar>

      <p
        v-if="recordErrorMessage"
        class="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive"
        role="alert"
      >
        {{ recordErrorMessage }}
      </p>

      <NvDataTable
        v-else
        manual
        :page="recordPaging.page.value"
        :page-size="recordPaging.pageSize.value"
        :total-items="calibrationRecordsTotal"
        :columns="recordColumns"
        :rows="calibrationRecords"
        :row-key="recordRowKey"
        :loading="calibrationRecordsPending"
        :searchable="false"
        :column-settings="false"
        empty-message="当前范围内没有校准记录。选择已建档器具后，可登记送检回证。"
        @update:page="recordPaging.page.value = $event"
        @update:page-size="(value) => (recordPaging.pageSize.value = String(value))"
      />
    </section>
    <NvDialog v-model:open="writeOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>{{
            writeMode === 'device' ? '新建计量器具' : '登记校准记录'
          }}</NvDialogTitle>
          <NvDialogDescription>
            {{ writeMode === 'device' ? '填写器具规格和校准周期。' : writeTarget?.deviceCode }}
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitWrite">
          <template v-if="writeMode === 'device'">
            <NvField>
              <NvFieldLabel for="cal-deviceCode">器具编码（可由系统生成）</NvFieldLabel>
              <NvInput id="cal-deviceCode" v-model="writeForm.deviceCode" type="text" />
            </NvField>
            <NvField>
              <NvFieldLabel for="cal-deviceType">器具类型</NvFieldLabel>
              <NvInput id="cal-deviceType" v-model="writeForm.deviceType" type="text" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="cal-accuracy">准确度</NvFieldLabel>
              <NvInput id="cal-accuracy" v-model="writeForm.accuracy" type="text" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="cal-interval">校准周期（天）</NvFieldLabel>
              <NvInput
                id="cal-interval"
                v-model="writeForm.interval"
                type="number"
                required
                min="1"
                step="1"
              />
            </NvField>
          </template>
          <template v-else>
            <NvField>
              <NvFieldLabel for="cal-calibrationNo">校准编号</NvFieldLabel>
              <NvInput
                id="cal-calibrationNo"
                v-model="writeForm.calibrationNo"
                type="text"
                required
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="cal-provider">校准机构</NvFieldLabel>
              <NvInput id="cal-provider" v-model="writeForm.provider" type="text" required />
            </NvField>
          </template>
          <NvField>
            <NvFieldLabel for="cal-calibratedOn">校准日期</NvFieldLabel>
            <NvInput id="cal-calibratedOn" v-model="writeForm.calibratedOn" type="date" required />
          </NvField>
          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="writeOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="saving">保存</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
