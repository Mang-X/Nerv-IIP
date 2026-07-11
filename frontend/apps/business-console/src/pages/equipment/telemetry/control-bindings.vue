<script setup lang="ts">
import type { BusinessConsoleTelemetryDeviceControlBindingItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useBusinessDeviceControlBindings } from '@/composables/useBusinessDeviceControlBinding'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifySuccess } from '@/utils/notify'
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
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
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

const errorMessage = computed(() => formatError(bindingsError.value))

const dialogOpen = ref(false)
const editing = ref(false)
const form = reactive({ deviceAssetId: '', connectorHostId: '', instanceKey: '' })
const showErrors = ref(false)

const disableOpen = ref(false)
const disableTarget = ref<BusinessConsoleTelemetryDeviceControlBindingItem | null>(null)
const disableReason = ref('')

const columns: NvDataTableColumn<BusinessConsoleTelemetryDeviceControlBindingItem>[] = [
  {
    key: 'deviceAssetId',
    header: '设备',
    cellClass: 'font-medium',
    accessor: (r) => r.deviceAssetId ?? '无',
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
    notifyError(error, '保存控制通道绑定失败，请稍后重试。')
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
    notifyError(error, '停用控制通道绑定失败，请稍后重试。')
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
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
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
              <NvDialogDescription>
                绑定设备到连接器主机与实例，作为设备控制命令下发的路由目标。操作员下发时无需再手输。
              </NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-3" @submit.prevent="submit">
              <NvFieldGroup class="grid gap-3">
                <NvField>
                  <NvFieldLabel for="binding-device"
                    >设备编号 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="binding-device"
                    v-model="form.deviceAssetId"
                    :readonly="editing"
                    placeholder="如 DEV-CNC-01"
                    :aria-invalid="!!deviceError"
                  />
                  <p v-if="deviceError" class="text-xs text-destructive" role="alert">
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
                  <NvInput
                    id="binding-instance"
                    v-model="form.instanceKey"
                    placeholder="连接器实例键，如 opcua-cell-01"
                    :aria-invalid="!!instanceError"
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
                <NvButton type="submit" :disabled="saveBindingPending">保存</NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.deviceAssetId"
          class="h-9 w-72"
          placeholder="按设备编号筛选"
          aria-label="设备编号"
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
            停用后该设备将无法下发控制命令，直至重新配置绑定。已下发的命令不受影响。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
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
