<script setup lang="ts">
/**
 * 停机原因目录维护（#3855）。目录归 Maintenance 所有，新环境由产品基线预置一套标准原因；
 * 这里补齐新增、编辑、删除入口。新增与编辑走侧滑抽屉，不离开本页。
 * 被维修工单引用过的原因不能删除（服务端拒绝），可改名称或分类。
 */
import type { NvDataTableColumn } from '@nerv-iip/ui'
import DowntimeReasonFormSheet from '@/components/maintenance/DowntimeReasonFormSheet.vue'
import {
  useMaintenanceDowntimeReasonDirectory,
  type MaintenanceDowntimeReasonRow,
} from '@/composables/useMaintenanceDowntimeReasonDirectory'
import { useMaintenanceDowntimeReasonMutations } from '@/composables/useMaintenanceDowntimeReasonMutations'
import {
  downtimeLossCategoryLabel,
  downtimeReasonCategoryLabel,
} from '@/data/downtimeReasonReference'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvDataTable,
  NvPageHeader,
  NvToolbar,
  NvSpinner,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, shallowRef } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '停机原因',
    requiredPermissions: ['business.maintenance.downtime-reasons.read'],
  },
})

const businessContext = useBusinessContextStore()
const directory = useMaintenanceDowntimeReasonDirectory(businessContext)
const { keyword, reasons, state, message, total } = directory
const { deleteReason, deleting } = useMaintenanceDowntimeReasonMutations()

const auth = useAuthStore()
const canManage = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.maintenanceWorkOrdersManage),
)

const columns = computed<NvDataTableColumn<MaintenanceDowntimeReasonRow>[]>(() => [
  { key: 'reasonCode', header: '原因编码', width: 'w-40' },
  { key: 'description', header: '原因名称', cellClass: 'font-medium' },
  {
    key: 'reasonCategory',
    header: '停机分类',
    accessor: (row) => downtimeReasonCategoryLabel(row.reasonCategory),
  },
  {
    key: 'lossCategory',
    header: '损失类别',
    accessor: (row) => downtimeLossCategoryLabel(row.lossCategory),
  },
  ...(canManage.value
    ? [{ key: 'actions', header: '操作', align: 'end' as const, width: 'w-32' }]
    : []),
])

// 目录一页取 100 条（受控词表），关键字走服务端检索；空结果和失败分开提示。
const emptyMessage = computed(() =>
  state.value === 'empty' || state.value === 'ok' ? '还没有停机原因。' : message.value,
)

// ── 新建 / 编辑 ─────────────────────────────────────────────────
// 每次打开递增，作抽屉的 key：每次都是全新实例，按当次的原因（或空白）初始化表单。
const formSession = shallowRef(0)
const formOpen = shallowRef(false)
const editing = shallowRef<MaintenanceDowntimeReasonRow>()
const existingCodes = computed(() => reasons.value.map((row) => row.reasonCode))

function openCreate() {
  editing.value = undefined
  formSession.value += 1
  formOpen.value = true
}
function openEdit(row: MaintenanceDowntimeReasonRow) {
  editing.value = row
  formSession.value += 1
  formOpen.value = true
}

// ── 删除 ────────────────────────────────────────────────────────
const deleteTarget = shallowRef<MaintenanceDowntimeReasonRow>()
const deleteOpen = shallowRef(false)
function askDelete(row: MaintenanceDowntimeReasonRow) {
  deleteTarget.value = row
  deleteOpen.value = true
}
async function confirmDelete() {
  const target = deleteTarget.value
  if (!target) return
  try {
    await deleteReason(target.reasonCode, {
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
    })
    deleteOpen.value = false
    notifySuccess(`已删除停机原因「${target.description}」。`)
  } catch (error) {
    notifyOperationFailure(
      '删除停机原因失败',
      error,
      '删除停机原因失败：已被维修工单引用的原因不能删除，可改为编辑名称或分类。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="停机原因"
      :breadcrumbs="[{ label: '设备监控' }, { label: '维护保养' }]"
      :count="`${total} 条停机原因`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="state === 'loading'"
          @click="directory.refresh()"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton v-if="canManage" size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建停机原因
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="按原因编码、名称或分类检索" />

    <p
      v-if="state === 'failed' || state === 'forbidden'"
      class="text-sm text-destructive"
      role="alert"
    >
      {{ message }}
    </p>

    <NvDataTable
      :columns="columns"
      :rows="reasons"
      row-key="reasonCode"
      :loading="state === 'loading'"
      :searchable="false"
      :column-settings="false"
      :empty-message="emptyMessage"
    >
      <template #empty>
        <template v-if="keyword.trim()">
          <p class="text-sm font-medium">没有符合条件的停机原因</p>
          <NvButton size="sm" type="button" variant="outline" @click="keyword = ''">
            清空检索
          </NvButton>
        </template>
        <template v-else-if="state === 'empty'">
          <p class="text-sm font-medium">还没有停机原因</p>
          <p class="max-w-md text-sm text-muted-foreground">
            报修登记设备占用、完工登记停机都要从这里选原因，请先新建。
          </p>
          <NvButton v-if="canManage" size="sm" type="button" @click="openCreate">
            <PlusIcon aria-hidden="true" />
            新建停机原因
          </NvButton>
        </template>
        <p v-else class="text-sm text-muted-foreground">{{ message }}</p>
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-1">
          <NvButton type="button" variant="ghost" size="sm" @click="openEdit(row)">编辑</NvButton>
          <NvButton type="button" variant="ghost" size="sm" @click="askDelete(row)">删除</NvButton>
        </div>
      </template>
    </NvDataTable>

    <DowntimeReasonFormSheet
      v-if="formSession"
      :key="formSession"
      v-model:open="formOpen"
      :reason="editing"
      :existing-codes="existingCodes"
    />

    <NvAlertDialog v-model:open="deleteOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>删除停机原因「{{ deleteTarget?.description }}」？</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            删除后新建工单和完工登记将不能再选择它。已被维修工单引用的原因不能删除。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel>取消</NvAlertDialogCancel>
          <!-- 确认按钮不用 NvAlertDialogAction：它点击即无条件关框，失败时就看不到原因了。 -->
          <NvButton type="button" variant="destructive" :disabled="deleting" @click="confirmDelete">
            <NvSpinner v-if="deleting" aria-hidden="true" />
            删除
          </NvButton>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
  </BusinessLayout>
</template>
