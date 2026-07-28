<script setup lang="ts">
import type { BusinessConsoleWmsWcsTaskItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import {
  labelFor,
  WCS_ADAPTER_TYPE_LABELS,
  WCS_TASK_STATUS_LABELS,
  wmsStatusTone,
} from '@/data/businessLabels'
import { hasBusinessContext } from '@/composables/businessContextBinding'
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
import { RouterLink } from 'vue-router'

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
  failWcs,
  failWcsPending,
  completeWcs,
  completeWcsPending,
} = useWmsWcsTasks()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.externalTaskId, () => filters.warehouseTaskId],
})

/**
 * 读错误只归列表区域。派发 / 标记失败 / 标记完成的失败一律走 toast，不并进这一条：
 * 两者共用一个变量时，「派发失败」会伪装成「列表加载失败」。
 */
const listErrorMessage = computed(() =>
  wcsTasksError.value
    ? `取不到设备任务列表，设备执行情况无法判断：${formatError(wcsTasksError.value)}`
    : '',
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
    formError.value = '请填写设备类型与外部任务号。'
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
  // 仓库任务只有 GUID（读面缺 taskNo），GUID 不上屏，这里就不摆这一行。
  return [
    { label: '外部任务号', value: task.externalTaskId },
    { label: '设备类型', value: adapterTypeLabel(task.adapterType) },
  ]
})
const hasCarriedAdapter = computed(() => Boolean(pendingTask.value?.adapterType))
const hasCarriedExternalTaskId = computed(() => Boolean(pendingTask.value?.externalTaskId))
/**
 * 数字口径：页头与「设备任务」KPI 一律用**服务端总数**；失败/执行中只能按当前页算，
 * 一律带「本页」前缀。读不到数（上下文未就绪 / 读取中 / 读失败）时显 `—` 并明说取不到，
 * 不断言 0，更不敢说「运行正常」——那是把故障伪装成一切正常。
 */
// 业务范围是否选定走全站唯一判定，不在页面里另写一份——判定分叉了，
// 「还没查」和「真的 0 条」很快又会混回同一个渲染。
const contextReady = computed(() => hasBusinessContext(filters))
const listReady = computed(
  () => contextReady.value && !wcsTasksError.value && !wcsTasksPending.value,
)
const headerCount = computed(() => {
  if (!contextReady.value) return '未选择组织与环境'
  if (wcsTasksError.value) return '任务数取不到'
  if (wcsTasksPending.value) return '加载中'
  return `${wcsTasksTotal.value} 个任务`
})
const failedCount = computed(
  () =>
    wcsTasks.value.filter((t) => !!t.failedAtUtc || (t.status ?? '').toLowerCase() === 'failed')
      .length,
)
const metricCells = computed<NvMetricStripCell[]>(() => {
  if (!listReady.value) {
    return [
      { key: 'total', label: '设备任务', value: '—' },
      { key: 'running', label: '本页执行中', value: '—' },
      { key: 'failed', label: '本页执行失败', value: '—' },
    ]
  }
  return [
    { key: 'total', label: '设备任务', value: wcsTasksTotal.value, unit: '条' },
    {
      key: 'running',
      label: '本页执行中',
      value: wcsTasks.value.length - failedCount.value,
      unit: '条',
    },
    {
      key: 'failed',
      label: '本页执行失败',
      value: failedCount.value,
      unit: '条',
      valueTone: failedCount.value > 0 ? 'danger' : undefined,
    },
  ]
})
const failedCardStatus = computed(() => {
  if (!listReady.value) return { label: '取不到数据', tone: 'neutral' as const }
  return failedCount.value > 0
    ? { label: '需人工跟进', tone: 'danger' as const }
    : { label: '本页无失败', tone: 'success' as const }
})
const failedCardNote = computed(() => {
  if (!listReady.value) return '设备任务读不到，本页有没有失败任务无法判断，请重试。'
  return failedCount.value > 0
    ? '失败任务不会自动重试，需要在设备侧确认后重新下发。'
    : '本页设备任务都在正常执行；其余分页请翻页或按状态筛选查看。'
})

type WcsRow = BusinessConsoleWmsWcsTaskItem
const columns: NvDataTableColumn<WcsRow>[] = [
  {
    key: 'externalTaskId',
    header: '外部任务号',
    cellClass: 'font-medium',
    accessor: (r) => r.externalTaskId ?? '无',
  },
  {
    key: 'adapterType',
    header: '设备类型',
    accessor: (r) => adapterTypeLabel(r.adapterType),
  },
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
/**
 * 适配器类型说人话。后端是自由文本技术标识（`agv` / `stacker-crane`），
 * 词表里有就只显中文设备名（技术标识对现场没有信息量，不并列展示）；
 * 没收录就退回技术标识，不编名字。
 */
function adapterTypeLabel(value?: string | null) {
  if (!value) return '无'
  const name = labelFor(WCS_ADAPTER_TYPE_LABELS, value, '')
  if (!name && import.meta.env.DEV) {
    console.warn(`[WCS] 词表缺失: ${value}，请补 businessLabels.ts 的 WCS_ADAPTER_TYPE_LABELS`)
  }
  return name || value
}
function statusLabel(value?: string | null) {
  return labelFor(WCS_TASK_STATUS_LABELS, value, '未知状态')
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
    <NvPageHeader title="WCS 任务" :breadcrumbs="[{ label: '仓储作业' }]" :count="headerCount">
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
      <NvMetricStrip :cells="metricCells" />
      <NvMetricCard
        variant="alert"
        label="本页执行失败"
        :value="listReady ? failedCount : '—'"
        :unit="listReady ? '条' : undefined"
        :tone="listReady && failedCount > 0 ? 'danger' : 'neutral'"
        :status="failedCardStatus"
        :foot-start="failedCardNote"
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

    <!-- 读失败 / 未选组织环境都由表格自己的三态呈现，绝不退化成「暂无设备任务」。 -->
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
      :error="wcsTasksError"
      :error-message="listErrorMessage"
      :awaiting-scope="!contextReady"
      awaiting-scope-message="请先在顶部选择组织与环境，再查看设备任务。"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无设备任务。"
      @retry="refreshWcsTasks"
    >
      <template #empty>
        <p class="text-sm font-medium">暂无设备任务</p>
        <p class="max-w-md text-sm text-muted-foreground">
          设备任务由上架、拣货作业下发给堆垛机、输送线等设备控制系统后产生；本仓库尚未接入设备控制系统。
        </p>
        <div class="flex gap-2">
          <NvButton size="sm" type="button" variant="outline" as-child>
            <RouterLink to="/wms/putaway">上架任务</RouterLink>
          </NvButton>
          <NvButton size="sm" type="button" variant="outline" as-child>
            <RouterLink to="/wms/picking">拣货任务</RouterLink>
          </NvButton>
        </div>
      </template>
      <!-- 设备类型是技术标识不是设备编码：词表命中就只显中文名（`agv` 这类原样并列没有信息量），
           没收录才退回原标识，仍不编造名字。 -->
      <template #cell-adapterType="{ row }">
        <span v-if="!row.adapterType" class="text-muted-foreground">无</span>
        <span v-else>{{ adapterTypeLabel(row.adapterType) }}</span>
      </template>
      <template #cell-status="{ row }"
        ><NvStatusBadge
          :value="row.status"
          :label="statusLabel(row.status)"
          :tone="wmsStatusTone(row.status)"
      /></template>
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
              <NvFieldLabel for="wcs-adapter">设备类型</NvFieldLabel>
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
