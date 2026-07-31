<script setup lang="ts">
import type { BusinessConsoleTelemetryDeviceControlBindingItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import { useBusinessDeviceControlBindings } from '@/composables/useBusinessDeviceControlBinding'
import {
  useConnectorInstanceCatalog,
  useEquipmentDeviceCatalog,
} from '@/composables/useEquipmentPickerCatalog'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  NvAlertDialog,
  NvAlertDialogAction,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, ref } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备控制通道绑定',
    requiredPermissions: ['business.iiot.device-control.read'],
  },
})

const auth = useAuthStore()
const canManage = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.iiotDeviceControlManage),
)

const {
  bindings,
  bindingsError,
  bindingsPending,
  bindingsTotal,
  filters,
  refreshBindings,
  saveBinding,
  saveBindingPending,
  disableBinding,
  disableBindingPending,
} = useBusinessDeviceControlBindings()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.deviceAssetId] })
const { deviceOptions, devicesPending } = useEquipmentDeviceCatalog()
const { connectorInstanceOptions, connectorsPending } = useConnectorInstanceCatalog()

const errorMessage = computed(() => formatError(bindingsError.value))

const dialogOpen = ref(false)
const editing = ref(false)
const form = reactive({ deviceAssetId: '', connectorHostId: '', instanceKey: '' })
const showErrors = ref(false)

const disableOpen = ref(false)
const disableTarget = ref<BusinessConsoleTelemetryDeviceControlBindingItem | null>(null)
const disableReason = ref('')

// 绑定读面只回设备编号（DEV-CNC-01），设备名在主数据里，按编号 join 出中文名。
const { resolveDevice } = useMasterDataDisplayNames({ devices: true })

const columns: NvDataTableColumn<BusinessConsoleTelemetryDeviceControlBindingItem>[] = [
  {
    key: 'deviceAssetId',
    header: '设备',
    cellClass: 'font-medium',
    accessor: (r) =>
      resolveDevice(r.deviceAssetId)
        ? `${resolveDevice(r.deviceAssetId)} ${r.deviceAssetId}`
        : (r.deviceAssetId ?? '无'),
  },
  { key: 'connectorHostId', header: '连接主机', accessor: (r) => r.connectorHostId ?? '无' },
  { key: 'instanceKey', header: '实例标识', accessor: (r) => r.instanceKey ?? '无' },
  { key: 'isActive', header: '状态', width: 'w-24' },
  {
    key: 'updatedAtUtc',
    header: '更新时间',
    width: 'w-44',
    accessor: (r) => formatDateTime(r.updatedAtUtc),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]

/**
 * 实例目录只包含上报过采集健康的连接器实例；编辑一条老绑定时它的实例可能已不在名单里，
 * 仍把当前值显示出来，避免打开弹窗就把已存的实例标识"看没了"。
 */
const instancePickerOptions = computed(() => {
  const instanceKey = form.instanceKey.trim()
  if (!instanceKey || connectorInstanceOptions.value.some((o) => o.value === instanceKey)) {
    return connectorInstanceOptions.value
  }
  return [
    ...connectorInstanceOptions.value,
    { value: instanceKey, label: instanceKey, hint: '当前绑定（连接器未上报采集健康）' },
  ]
})

const deviceError = computed(() =>
  showErrors.value && !form.deviceAssetId.trim() ? '请填写设备编号' : '',
)
const hostError = computed(() =>
  showErrors.value && !form.connectorHostId.trim() ? '请填写连接主机' : '',
)
const instanceError = computed(() =>
  showErrors.value && !form.instanceKey.trim() ? '请填写实例标识' : '',
)
const formValid = computed(
  () => form.deviceAssetId.trim() && form.connectorHostId.trim() && form.instanceKey.trim(),
)
// 编辑态：设备编号是绑定身份，由所选行带出，只读呈现。
const bindingContextItems = computed(() => [{ label: '设备编号', value: form.deviceAssetId }])
// 停用确认：目标绑定的事实由所选行带出。
const disableContextItems = computed(() => [
  { label: '设备编号', value: disableTarget.value?.deviceAssetId },
  { label: '连接主机', value: disableTarget.value?.connectorHostId },
  { label: '实例标识', value: disableTarget.value?.instanceKey },
])

function openCreate() {
  editing.value = false
  form.deviceAssetId = ''
  form.connectorHostId = ''
  form.instanceKey = ''
  showErrors.value = false
  dialogOpen.value = true
}
function openEdit(row: BusinessConsoleTelemetryDeviceControlBindingItem) {
  editing.value = true
  form.deviceAssetId = row.deviceAssetId ?? ''
  form.connectorHostId = row.connectorHostId ?? ''
  form.instanceKey = row.instanceKey ?? ''
  showErrors.value = false
  dialogOpen.value = true
}

async function submit() {
  showErrors.value = true
  if (!formValid.value) return
  try {
    await saveBinding({
      deviceAssetId: form.deviceAssetId.trim(),
      connectorHostId: form.connectorHostId.trim(),
      instanceKey: form.instanceKey.trim(),
    })
    notifySuccess(
      editing.value
        ? `控制通道绑定已更新：${form.deviceAssetId}`
        : `控制通道绑定已创建：${form.deviceAssetId}`,
    )
    dialogOpen.value = false
  } catch (error) {
    notifyOperationFailure('保存控制通道绑定失败', error, '保存控制通道绑定失败，请稍后重试。')
  }
}

function openDisable(row: BusinessConsoleTelemetryDeviceControlBindingItem) {
  disableTarget.value = row
  disableReason.value = ''
  disableOpen.value = true
}
async function confirmDisable() {
  const target = disableTarget.value
  if (!target?.deviceAssetId || !disableReason.value.trim()) return
  try {
    await disableBinding(target.deviceAssetId, disableReason.value.trim())
    notifySuccess(`控制通道绑定已停用：${target.deviceAssetId}`)
    disableOpen.value = false
  } catch (error) {
    notifyOperationFailure('停用控制通道绑定失败', error, '停用控制通道绑定失败，请稍后重试。')
  }
}

function rowKey(row: BusinessConsoleTelemetryDeviceControlBindingItem) {
  return row.deviceControlChannelBindingId ?? row.deviceAssetId ?? ''
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="设备控制通道绑定"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="`${bindingsTotal} 条绑定`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="bindingsPending"
          @click="refreshBindings"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-if="canManage" v-model:open="dialogOpen">
          <NvButton size="sm" type="button" @click="openCreate">
            <PlusIcon aria-hidden="true" />
            新建绑定
          </NvButton>
          <NvDialogContent class="sm:max-w-md">
            <NvDialogHeader>
              <NvDialogTitle>{{ editing ? '编辑控制通道绑定' : '新建控制通道绑定' }}</NvDialogTitle>
              <NvDialogDescription class="sr-only">
                设备控制命令的下发通道路由目标。
              </NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-3" @submit.prevent="submit">
              <!-- 编辑态：设备编号是绑定身份，只读呈现，不做成 readonly 输入位。 -->
              <CarriedContextSummary v-if="editing" label="绑定设备" :items="bindingContextItems" />

              <NvFieldGroup class="grid gap-3">
                <NvField v-if="!editing">
                  <NvFieldLabel for="binding-device"
                    >设备编号 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvEntityPicker
                    id="binding-device"
                    v-model="form.deviceAssetId"
                    :options="deviceOptions"
                    title="选择设备"
                    placeholder="选择设备"
                    source-text="数据来自基础数据设备资产"
                    empty-text="暂无设备资产，请先在基础数据登记设备"
                    :loading="devicesPending"
                    aria-label="设备编号"
                  />
                  <p
                    v-if="deviceError"
                    id="binding-device-error"
                    class="text-xs text-destructive"
                    role="alert"
                  >
                    {{ deviceError }}
                  </p>
                </NvField>
                <NvField>
                  <NvFieldLabel for="binding-host"
                    >连接主机 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="binding-host"
                    v-model="form.connectorHostId"
                    placeholder="连接器主机 ID"
                    :aria-invalid="!!hostError"
                  />
                  <p v-if="hostError" class="text-xs text-destructive" role="alert">
                    {{ hostError }}
                  </p>
                </NvField>
                <NvField>
                  <NvFieldLabel for="binding-instance"
                    >实例标识 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvEntityPicker
                    id="binding-instance"
                    v-model="form.instanceKey"
                    :options="instancePickerOptions"
                    title="选择连接器实例"
                    placeholder="选择连接器实例"
                    source-text="数据来自连接器采集健康上报"
                    empty-text="还没有连接器上报采集健康，请先让连接器接入并上报"
                    :loading="connectorsPending"
                    aria-label="实例标识"
                  />
                  <p v-if="instanceError" class="text-xs text-destructive" role="alert">
                    {{ instanceError }}
                  </p>
                </NvField>
              </NvFieldGroup>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="dialogOpen = false"
                  >取消</NvButton
                >
                <NvButton type="submit" :disabled="saveBindingPending">{{
                  editing ? '保存绑定' : '创建绑定'
                }}</NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvEntityPicker
          v-model="filters.deviceAssetId"
          class="w-72"
          :options="deviceOptions"
          title="选择设备"
          placeholder="全部设备"
          source-text="数据来自基础数据设备资产"
          empty-text="暂无设备资产，请先在基础数据登记设备"
          :loading="devicesPending"
          clearable
          aria-label="设备"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="bindingsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="bindings"
      :row-key="rowKey"
      :loading="bindingsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="还没有设备控制通道绑定，点击「新建绑定」为设备配置下发通道。"
    >
      <template #cell-deviceAssetId="{ row }">
        <span class="grid leading-tight">
          <span>{{ resolveDevice(row.deviceAssetId) ?? row.deviceAssetId ?? '无' }}</span>
          <span v-if="resolveDevice(row.deviceAssetId)" class="text-xs text-muted-foreground">{{
            row.deviceAssetId
          }}</span>
        </span>
      </template>
      <template #cell-isActive="{ row }">
        <NvStatusBadge :value="row.isActive === false ? 'disabled' : 'active'" />
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`控制通道绑定操作 ${row.deviceAssetId ?? ''}`">
          <NvDropdownMenuItem :disabled="!canManage" @click="openEdit(row)"
            >编辑</NvDropdownMenuItem
          >
          <NvDropdownMenuItem
            v-if="row.isActive !== false"
            :disabled="!canManage"
            @click="openDisable(row)"
          >
            停用
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvAlertDialog v-model:open="disableOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>确认停用该控制通道绑定？</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            停用后该设备将无法下发控制命令，直至重新配置绑定。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <CarriedContextSummary label="停用对象" :items="disableContextItems" />
        <NvField>
          <NvFieldLabel for="binding-disable-reason"
            >停用原因 <span class="text-destructive">*</span></NvFieldLabel
          >
          <textarea
            id="binding-disable-reason"
            v-model="disableReason"
            rows="2"
            class="min-h-16 w-full rounded-md border bg-transparent px-3 py-2 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            placeholder="说明停用原因，进审计"
          ></textarea>
        </NvField>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel>取消</NvAlertDialogCancel>
          <NvAlertDialogAction
            variant="destructive"
            :disabled="!disableReason.trim() || disableBindingPending"
            @click="confirmDisable"
          >
            确认停用
          </NvAlertDialogAction>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
  </BusinessLayout>
</template>
