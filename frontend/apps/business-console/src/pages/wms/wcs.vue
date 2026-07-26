<script setup lang="ts">
import type { BusinessConsoleWmsWcsTaskItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import { useWmsWcsTasks } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricCard,
  NvMetricStrip,
  NvPageHeader,
  NvRowActions,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, RefreshCwIcon, SendIcon, XCircleIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'WCS 任务',
    requiredPermissions: ['business.wms.automation.manage'],
  },
})

const {
  filters,
  wcsTasks,
  wcsTasksError,
  wcsTasksPending,
  wcsTasksTotal,
  refreshWcsTasks,
  dispatchWcs,
  dispatchWcsPending,
  dispatchWcsError,
  failWcs,
  failWcsPending,
  failWcsError,
  completeWcs,
  completeWcsPending,
  completeWcsError,
} = useWmsWcsTasks()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.externalTaskId, () => filters.warehouseTaskId],
})

const errorMessage = computed(() =>
  formatError(
    wcsTasksError.value ?? dispatchWcsError.value ?? failWcsError.value ?? completeWcsError.value,
  ),
)

type Action = 'dispatch' | 'fail' | 'complete'
const openAction = shallowRef<Action | ''>('')
const pendingTask = shallowRef<WcsRow>()
const formError = shallowRef('')
const dispatchForm = reactive({ adapterType: '', externalTaskId: '', payloadJson: '{}' })
const failForm = reactive({ failureCode: '', failureMessage: '' })
const completeForm = reactive({ completionPayloadJson: '{}' })

const actionPending = computed(
  () => dispatchWcsPending.value || failWcsPending.value || completeWcsPending.value,
)

function openDialog(action: Action, row: WcsRow) {
  pendingTask.value = row
  formError.value = ''
  if (action === 'dispatch') {
    dispatchForm.adapterType = row.adapterType ?? ''
    dispatchForm.externalTaskId = row.externalTaskId ?? ''
    dispatchForm.payloadJson = '{}'
  } else if (action === 'fail') {
    failForm.failureCode = ''
    failForm.failureMessage = ''
  } else {
    completeForm.completionPayloadJson = '{}'
  }
  openAction.value = action
}

function invalidJson(value: string) {
  try {
    JSON.parse(value)
    return false
  } catch {
    return true
  }
}

async function submitDispatch() {
  const id = pendingTask.value?.warehouseTaskId
  if (!id) return
  if (!dispatchForm.adapterType.trim() || !dispatchForm.externalTaskId.trim()) {
    formError.value = '请填写适配器与外部任务号。'
    return
  }
  if (invalidJson(dispatchForm.payloadJson)) {
    formError.value = '派发载荷必须是合法 JSON。'
    return
  }
  try {
    await dispatchWcs(id, {
      adapterType: dispatchForm.adapterType.trim(),
      externalTaskId: dispatchForm.externalTaskId.trim(),
      payloadJson: dispatchForm.payloadJson,
    })
    openAction.value = ''
    notifySuccess('WCS 任务已派发')
  } catch (error) {
    notifyError(error, '派发 WCS 任务失败，请稍后重试。')
  }
}

async function submitFail() {
  const id = pendingTask.value?.externalTaskId
  if (!id) return
  if (!failForm.failureCode.trim() || !failForm.failureMessage.trim()) {
    formError.value = '请填写失败代码与说明。'
    return
  }
  try {
    await failWcs(id, {
      failureCode: failForm.failureCode.trim(),
      failureMessage: failForm.failureMessage.trim(),
    })
    openAction.value = ''
    notifySuccess('已标记为失败')
  } catch (error) {
    notifyError(error, '标记失败未成功，请稍后重试。')
  }
}

async function submitComplete() {
  const id = pendingTask.value?.externalTaskId
  if (!id) return
  if (invalidJson(completeForm.completionPayloadJson)) {
    formError.value = '完成回执必须是合法 JSON。'
    return
  }
  try {
    await completeWcs(id, { completionPayloadJson: completeForm.completionPayloadJson })
    openAction.value = ''
    notifySuccess('WCS 任务已完成')
  } catch (error) {
    notifyError(error, '标记完成失败，请稍后重试。')
  }
}

// 任务身份由所选行带出，只读展示；行上没有的值才留输入框（重新派发时可能需要改写适配器）。
const taskContextItems = computed(() => {
  const task = pendingTask.value
  if (!task) return []
  return [
    { label: '外部任务号', value: task.externalTaskId },
    { label: '适配器', value: task.adapterType },
    { label: '仓库任务', value: task.warehouseTaskId },
  ]
})
const hasCarriedAdapter = computed(() => Boolean(pendingTask.value?.adapterType))
const hasCarriedExternalTaskId = computed(() => Boolean(pendingTask.value?.externalTaskId))
const failedCount = computed(
  () =>
    wcsTasks.value.filter((t) => !!t.failedAtUtc || (t.status ?? '').toLowerCase() === 'failed')
      .length,
)

type WcsRow = BusinessConsoleWmsWcsTaskItem
const columns: NvDataTableColumn<WcsRow>[] = [
  {
    key: 'externalTaskId',
    header: '外部任务号',
    cellClass: 'font-medium',
    accessor: (r) => r.externalTaskId ?? '无',
  },
  { key: 'adapterType', header: '适配器', accessor: (r) => r.adapterType ?? '无' },
  {
    key: 'warehouseTaskId',
    header: '仓库任务',
    cellClass: 'text-muted-foreground',
    accessor: (r) => r.warehouseTaskId ?? '无',
  },
  { key: 'inventoryContext', header: '库存上下文', width: 'w-72' },
  { key: 'status', header: '状态', width: 'w-28' },
  {
    key: 'attemptCount',
    header: '尝试次数',
    align: 'end',
    width: 'w-24',
    accessor: (r) => r.attemptCount ?? 0,
  },
  { key: 'failure', header: '失败原因' },
  {
    key: 'dispatchedAtUtc',
    header: '派发时间',
    accessor: (r) => formatDateTime(r.dispatchedAtUtc),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function rowKey(row: WcsRow) {
  return row.wcsTaskId ?? row.externalTaskId ?? 'WCS 任务'
}
function formatDateTime(value?: string | null) {
  if (!value) return '—'
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
      title="WCS 任务"
      :breadcrumbs="[{ label: '仓储作业' }]"
      :count="`${wcsTasksTotal} 个任务`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="wcsTasksPending"
          @click="refreshWcsTasks"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
      <NvMetricStrip
        :cells="[
          { key: 'total', label: '设备任务', value: wcsTasksTotal, unit: '条' },
          {
            key: 'running',
            label: '执行中',
            value: wcsTasks.length - failedCount,
            unit: '条',
          },
          {
            key: 'failed',
            label: '执行失败',
            value: failedCount,
            unit: '条',
            valueTone: failedCount > 0 ? 'danger' : undefined,
          },
        ]"
      />
      <NvMetricCard
        variant="alert"
        label="执行失败"
        :value="failedCount"
        unit="条"
        :tone="failedCount > 0 ? 'danger' : 'neutral'"
        :status="
          failedCount > 0
            ? { label: '需人工跟进', tone: 'danger' }
            : { label: '运行正常', tone: 'success' }
        "
        :foot-start="
          failedCount > 0
            ? '失败任务不会自动重试，需要在设备侧确认后重新下发。'
            : '本页设备任务都在正常执行。'
        "
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.externalTaskId"
          class="h-9 w-40"
          placeholder="外部任务号"
          aria-label="外部任务号"
        />
        <NvInput
          v-model="filters.warehouseTaskId"
          class="h-9 w-40"
          placeholder="仓库任务"
          aria-label="仓库任务"
        />
        <NvInput
          v-model="filters.status"
          class="h-9 w-28"
          placeholder="状态（可选）"
          aria-label="任务状态"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="wcsTasksTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="wcsTasks"
      :row-key="rowKey"
      :loading="wcsTasksPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无 WCS 任务。派发到设备控制系统的任务会出现在这里。"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-inventoryContext="{ row }">
        <WmsInventoryContextPanel
          compact
          gap-message="本页暂不显示物料、库位与库存数量，请到对应的上架或拣货任务查看库存上下文。"
        />
      </template>
      <template #cell-failure="{ row }">
        <div v-if="row.failureCode || row.failureMessage" class="flex flex-col gap-0.5">
          <span class="text-sm text-destructive">{{ row.failureCode ?? '失败' }}</span>
          <span v-if="row.failureMessage" class="text-xs text-muted-foreground">{{
            row.failureMessage
          }}</span>
        </div>
        <span v-else class="text-muted-foreground">无</span>
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`WCS 任务操作 ${row.externalTaskId ?? ''}`">
          <NvDropdownMenuItem :disabled="!row.warehouseTaskId" @click="openDialog('dispatch', row)">
            <SendIcon aria-hidden="true" />
            重新派发
          </NvDropdownMenuItem>
          <NvDropdownMenuItem :disabled="!row.externalTaskId" @click="openDialog('fail', row)">
            <XCircleIcon aria-hidden="true" />
            标记失败
          </NvDropdownMenuItem>
          <NvDropdownMenuItem :disabled="!row.externalTaskId" @click="openDialog('complete', row)">
            <CheckCircle2Icon aria-hidden="true" />
            标记完成
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog
      :open="openAction === 'dispatch'"
      @update:open="
        (v) => {
          if (!v) openAction = ''
        }
      "
    >
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>重新派发 WCS 任务</NvDialogTitle>
          <!-- 任务身份已在下方只读区呈现；此处仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            外部任务 {{ pendingTask?.externalTaskId ?? '' }} 的重新派发。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitDispatch">
          <CarriedContextSummary label="派发对象" :items="taskContextItems" />
          <NvFieldGroup>
            <NvField v-if="!hasCarriedAdapter">
              <NvFieldLabel for="wcs-adapter">适配器</NvFieldLabel>
              <NvInput id="wcs-adapter" v-model="dispatchForm.adapterType" autocomplete="off" />
            </NvField>
            <NvField v-if="!hasCarriedExternalTaskId">
              <NvFieldLabel for="wcs-external">外部任务号</NvFieldLabel>
              <NvInput id="wcs-external" v-model="dispatchForm.externalTaskId" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wcs-payload">派发载荷（JSON）</NvFieldLabel>
              <NvInput
                id="wcs-payload"
                v-model="dispatchForm.payloadJson"
                class="font-mono"
                autocomplete="off"
              />
            </NvField>
            <NvFieldError v-if="formError" :errors="[formError]" />
          </NvFieldGroup>
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="actionPending">重新派发</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog
      :open="openAction === 'fail'"
      @update:open="
        (v) => {
          if (!v) openAction = ''
        }
      "
    >
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>标记 WCS 任务失败</NvDialogTitle>
          <!-- 任务身份已在下方只读区呈现；此处仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            外部任务 {{ pendingTask?.externalTaskId ?? '' }} 的失败登记。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitFail">
          <CarriedContextSummary label="失败任务" :items="taskContextItems" />
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="wcs-failure-code">失败代码</NvFieldLabel>
              <NvInput id="wcs-failure-code" v-model="failForm.failureCode" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wcs-failure-message">失败说明</NvFieldLabel>
              <NvInput
                id="wcs-failure-message"
                v-model="failForm.failureMessage"
                autocomplete="off"
              />
            </NvField>
            <NvFieldError v-if="formError" :errors="[formError]" />
          </NvFieldGroup>
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" variant="destructive" :disabled="actionPending"
              >标记失败</NvButton
            >
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog
      :open="openAction === 'complete'"
      @update:open="
        (v) => {
          if (!v) openAction = ''
        }
      "
    >
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>标记 WCS 任务完成</NvDialogTitle>
          <!-- 任务身份已在下方只读区呈现；此处仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            外部任务 {{ pendingTask?.externalTaskId ?? '' }} 的完成回执。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitComplete">
          <CarriedContextSummary label="完成任务" :items="taskContextItems" />
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="wcs-completion">完成回执（JSON）</NvFieldLabel>
              <NvInput
                id="wcs-completion"
                v-model="completeForm.completionPayloadJson"
                class="font-mono"
                autocomplete="off"
              />
            </NvField>
            <NvFieldError v-if="formError" :errors="[formError]" />
          </NvFieldGroup>
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="actionPending">标记完成</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
