<script setup lang="ts">
import type { BusinessConsoleWmsWcsTaskItem } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import { useWmsWcsTasks } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePro,
  DialogPro,
  DialogProClose,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DropdownMenuProItem,
  FieldPro,
  FieldProError,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  StatusBadgePro,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, RefreshCwIcon, SendIcon, XCircleIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: 'WCS 任务', requiredPermissions: ['business.wms.automation.manage'] } })

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
  formatError(wcsTasksError.value ?? dispatchWcsError.value ?? failWcsError.value ?? completeWcsError.value),
)

type Action = 'dispatch' | 'fail' | 'complete'
const openAction = shallowRef<Action | ''>('')
const pendingTask = shallowRef<WcsRow>()
const formError = shallowRef('')
const dispatchForm = reactive({ adapterType: '', externalTaskId: '', payloadJson: '{}' })
const failForm = reactive({ failureCode: '', failureMessage: '' })
const completeForm = reactive({ completionPayloadJson: '{}' })

const actionPending = computed(() => dispatchWcsPending.value || failWcsPending.value || completeWcsPending.value)

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
    toast.success('WCS 任务已派发')
  } catch {
    // 失败信息由页面错误区呈现。
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
    await failWcs(id, { failureCode: failForm.failureCode.trim(), failureMessage: failForm.failureMessage.trim() })
    openAction.value = ''
    toast.success('已标记为失败')
  } catch {
    // 失败信息由页面错误区呈现。
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
    toast.success('WCS 任务已完成')
  } catch {
    // 失败信息由页面错误区呈现。
  }
}
const failedCount = computed(() => wcsTasks.value.filter((t) => !!t.failedAtUtc || (t.status ?? '').toLowerCase() === 'failed').length)

type WcsRow = BusinessConsoleWmsWcsTaskItem
const columns: DataTableProColumn<WcsRow>[] = [
  { key: 'externalTaskId', header: '外部任务号', cellClass: 'font-medium', accessor: (r) => r.externalTaskId ?? '无' },
  { key: 'adapterType', header: '适配器', accessor: (r) => r.adapterType ?? '无' },
  { key: 'warehouseTaskId', header: '仓库任务', cellClass: 'text-muted-foreground', accessor: (r) => r.warehouseTaskId ?? '无' },
  { key: 'inventoryContext', header: '库存上下文', width: 'w-72' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'attemptCount', header: '尝试次数', align: 'end', width: 'w-24', accessor: (r) => r.attemptCount ?? 0 },
  { key: 'failure', header: '失败原因' },
  { key: 'dispatchedAtUtc', header: '派发时间', accessor: (r) => formatDateTime(r.dispatchedAtUtc) },
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
    <PageHeader title="WCS 任务" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${wcsTasksTotal} 个任务`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="wcsTasksPending" @click="refreshWcsTasks">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="WCS 任务" :value="wcsTasksTotal" hint="后端返回总数" />
      <SectionCard description="本页失败任务" :value="failedCount" hint="需人工跟进重试" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <InputPro v-model="filters.externalTaskId" class="h-9 w-40" placeholder="外部任务号" aria-label="外部任务号" />
        <InputPro v-model="filters.warehouseTaskId" class="h-9 w-40" placeholder="仓库任务" aria-label="仓库任务" />
        <InputPro v-model="filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="任务状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
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
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status" /></template>
      <template #cell-inventoryContext="{ row }">
        <WmsInventoryContextPanel
          compact
          source-workflow="wms.wcs"
          source-label="WCS 来源"
          :source-document-id="row.externalTaskId ?? row.warehouseTaskId"
          gap-message="后端缺口：WCS 任务列表只返回设备任务与仓库任务标识，未返回 SKU、库位、批次/序列号或库存数量；需要先进入关联的上架/拣货任务查看 Inventory 上下文。"
        />
      </template>
      <template #cell-failure="{ row }">
        <div v-if="row.failureCode || row.failureMessage" class="flex flex-col gap-0.5">
          <span class="text-sm text-destructive">{{ row.failureCode ?? '失败' }}</span>
          <span v-if="row.failureMessage" class="text-xs text-muted-foreground">{{ row.failureMessage }}</span>
        </div>
        <span v-else class="text-muted-foreground">无</span>
      </template>
      <template #cell-actions="{ row }">
        <RowActions :label="`WCS 任务操作 ${row.externalTaskId ?? ''}`">
          <DropdownMenuProItem :disabled="!row.warehouseTaskId" @click="openDialog('dispatch', row)">
            <SendIcon aria-hidden="true" />
            重新派发
          </DropdownMenuProItem>
          <DropdownMenuProItem :disabled="!row.externalTaskId" @click="openDialog('fail', row)">
            <XCircleIcon aria-hidden="true" />
            标记失败
          </DropdownMenuProItem>
          <DropdownMenuProItem :disabled="!row.externalTaskId" @click="openDialog('complete', row)">
            <CheckCircle2Icon aria-hidden="true" />
            标记完成
          </DropdownMenuProItem>
        </RowActions>
      </template>
    </DataTablePro>


    <DialogPro :open="openAction === 'dispatch'" @update:open="(v) => { if (!v) openAction = '' }">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>重新派发 WCS 任务</DialogProTitle>
          <DialogProDescription>将仓库任务派发到设备控制系统（WCS）适配器。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitDispatch">
          <FieldProGroup>
            <FieldPro>
              <FieldProLabel for="wcs-adapter">适配器</FieldProLabel>
              <InputPro id="wcs-adapter" v-model="dispatchForm.adapterType" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="wcs-external">外部任务号</FieldProLabel>
              <InputPro id="wcs-external" v-model="dispatchForm.externalTaskId" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="wcs-payload">派发载荷（JSON）</FieldProLabel>
              <InputPro id="wcs-payload" v-model="dispatchForm.payloadJson" class="font-mono" autocomplete="off" />
            </FieldPro>
            <FieldProError v-if="formError" :errors="[formError]" />
          </FieldProGroup>
          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="actionPending">派发</ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>

    <DialogPro :open="openAction === 'fail'" @update:open="(v) => { if (!v) openAction = '' }">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>标记 WCS 任务失败</DialogProTitle>
          <DialogProDescription>记录 {{ pendingTask?.externalTaskId ?? '' }} 的失败代码与说明，便于后续重试。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitFail">
          <FieldProGroup>
            <FieldPro>
              <FieldProLabel for="wcs-failure-code">失败代码</FieldProLabel>
              <InputPro id="wcs-failure-code" v-model="failForm.failureCode" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="wcs-failure-message">失败说明</FieldProLabel>
              <InputPro id="wcs-failure-message" v-model="failForm.failureMessage" autocomplete="off" />
            </FieldPro>
            <FieldProError v-if="formError" :errors="[formError]" />
          </FieldProGroup>
          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" variant="destructive" :disabled="actionPending">标记失败</ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>

    <DialogPro :open="openAction === 'complete'" @update:open="(v) => { if (!v) openAction = '' }">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>标记 WCS 任务完成</DialogProTitle>
          <DialogProDescription>提交 {{ pendingTask?.externalTaskId ?? '' }} 的完成回执。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitComplete">
          <FieldProGroup>
            <FieldPro>
              <FieldProLabel for="wcs-completion">完成回执（JSON）</FieldProLabel>
              <InputPro id="wcs-completion" v-model="completeForm.completionPayloadJson" class="font-mono" autocomplete="off" />
            </FieldPro>
            <FieldProError v-if="formError" :errors="[formError]" />
          </FieldProGroup>
          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="actionPending">完成</ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
