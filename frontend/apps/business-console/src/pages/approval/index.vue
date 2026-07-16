<script setup lang="ts">
import type {
  BusinessConsoleApprovalChainItem,
  BusinessConsoleApprovalDecisionListItem,
  BusinessConsoleApprovalDelegationItem,
  BusinessConsoleApprovalTaskItem,
  BusinessConsoleApprovalTemplateItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useBusinessApproval } from '@/composables/useBusinessApproval'
import { usePagedList } from '@/composables/usePagedList'
import { useAuthStore } from '@/stores/auth'
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
  NvPageHeader,
  NvRowActions,
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
} from '@nerv-iip/ui'
import {
  CheckCircle2Icon,
  EyeIcon,
  FilePlus2Icon,
  RefreshCwIcon,
  RotateCcwIcon,
  SendIcon,
  UserRoundPlusIcon,
  XCircleIcon,
} from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { storeToRefs } from 'pinia'
import { useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '审批中心',
    requiredPermissions: ['business.approvals.read', 'business.approvals.manage'],
  },
})

const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const permissionCodes = computed(() => principal.value?.permissionCodes ?? [])
const actorRef = computed(() => principal.value?.principalId ?? principal.value?.loginName ?? '')
const actor = computed(() => ({ actorType: 'user', actorRef: actorRef.value }))
const canReadApprovals = computed(
  () =>
    permissionCodes.value.includes(P.approvalsRead) ||
    permissionCodes.value.includes(P.approvalsManage),
)
const canManageApprovals = computed(() => permissionCodes.value.includes(P.approvalsManage))

const approval = useBusinessApproval(actor)
const route = useRoute() as { query?: Record<string, unknown> } | undefined
const taskPager = usePagedList(approval.taskFilters)
const chainPager = usePagedList(approval.chainFilters)
const decisionPager = usePagedList(approval.decisionFilters)
const delegationPager = usePagedList(approval.delegationFilters)
const templatePager = usePagedList(approval.templateFilters)

const taskDecisionOpen = shallowRef(false)
const decisionForm = reactive({
  chainId: '',
  stepNo: 0,
  decision: 'Approve',
  comment: '',
})

const delegationOpen = shallowRef(false)
const delegationForm = reactive({
  delegatorActorRef: '',
  delegateActorRef: '',
  documentType: '',
  effectiveFromUtc: '',
  effectiveToUtc: '',
  reason: '',
})
const delegationError = shallowRef('')

const templateOpen = shallowRef(false)
const templateForm = reactive({
  templateCode: '',
  documentType: '',
  version: '1',
  isActive: 'true',
  stepNo: '10',
  stepName: '',
  approverType: 'role',
  approverRef: '',
  dueInHours: '',
})
const templateError = shallowRef('')

const taskColumns: NvDataTableColumn<BusinessConsoleApprovalTaskItem>[] = [
  { key: 'documentId', header: '单据', cellClass: 'font-medium', accessor: documentLabel },
  {
    key: 'stepName',
    header: '当前步骤',
    accessor: (row) => row.stepName ?? `第 ${row.stepNo ?? '—'} 步`,
  },
  { key: 'documentType', header: '单据类型', accessor: (row) => row.documentType ?? '—' },
  { key: 'dueAtUtc', header: '到期时间', accessor: (row) => formatDateTime(row.dueAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

const chainColumns: NvDataTableColumn<BusinessConsoleApprovalChainItem>[] = [
  { key: 'documentId', header: '单据', cellClass: 'font-medium', accessor: documentLabel },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'templateCode', header: '模板', accessor: (row) => row.templateCode ?? '—' },
  { key: 'startedBy', header: '发起人', accessor: (row) => row.startedBy ?? '—' },
  { key: 'startedAtUtc', header: '发起时间', accessor: (row) => formatDateTime(row.startedAtUtc) },
  { key: 'actions', header: '步骤', align: 'end', width: 'w-12' },
]

const decisionColumns: NvDataTableColumn<BusinessConsoleApprovalDecisionListItem>[] = [
  { key: 'documentId', header: '单据', cellClass: 'font-medium', accessor: documentLabel },
  { key: 'decision', header: '决策', width: 'w-24' },
  { key: 'actorRef', header: '处理人', accessor: (row) => row.actorRef ?? '—' },
  { key: 'comment', header: '意见', accessor: (row) => row.comment ?? '—' },
  { key: 'decidedAtUtc', header: '处理时间', accessor: (row) => formatDateTime(row.decidedAtUtc) },
]

const delegationColumns: NvDataTableColumn<BusinessConsoleApprovalDelegationItem>[] = [
  {
    key: 'delegatorActorRef',
    header: '委托人',
    cellClass: 'font-medium',
    accessor: (row) => row.delegatorActorRef ?? '—',
  },
  { key: 'delegateActorRef', header: '代理人', accessor: (row) => row.delegateActorRef ?? '—' },
  {
    key: 'documentType',
    header: '单据范围',
    accessor: (row) => row.documentType ?? '全部业务单据',
  },
  { key: 'status', header: '状态', width: 'w-24' },
  {
    key: 'effectiveToUtc',
    header: '截止时间',
    accessor: (row) => formatDateTime(row.effectiveToUtc),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

const templateColumns: NvDataTableColumn<BusinessConsoleApprovalTemplateItem>[] = [
  {
    key: 'templateCode',
    header: '模板',
    cellClass: 'font-medium',
    accessor: (row) => row.templateCode ?? '—',
  },
  { key: 'documentType', header: '单据类型', accessor: (row) => row.documentType ?? '—' },
  { key: 'version', header: '版本', width: 'w-20', accessor: (row) => String(row.version ?? '—') },
  { key: 'isActive', header: '状态', width: 'w-24' },
  { key: 'steps', header: '步骤', accessor: (row) => `${row.steps?.length ?? 0} 步` },
]

const activeDelegations = computed(
  () =>
    approval.delegations.value.filter((item) => (item.status ?? '').toLowerCase() === 'active')
      .length,
)
const pendingTasks = computed(() => approval.tasks.value.length)
const runningChains = computed(
  () =>
    approval.chains.value.filter((item) =>
      ['running', 'pending', 'open'].includes((item.status ?? '').toLowerCase()),
    ).length,
)

applyRouteApprovalFilters()

function documentLabel(row: { documentType?: string | null; documentId?: string | null }) {
  const id = row.documentId ?? ''
  return id ? `${row.documentType ?? '业务单据'} · ${id}` : (row.documentType ?? '业务单据')
}

function rowKey(row: Record<string, unknown>) {
  return String(
    row.chainId ??
      row.delegationId ??
      row.templateId ??
      row.decisionId ??
      row.documentId ??
      JSON.stringify(row),
  )
}

function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}

function applyRouteApprovalFilters() {
  const sourceService = firstQuery(route?.query?.sourceService)
  const documentType = firstQuery(route?.query?.documentType)
  const documentId = firstQuery(route?.query?.documentId)

  if (sourceService) approval.chainFilters.sourceService = sourceService
  if (documentType) {
    approval.chainFilters.documentType = documentType
    approval.decisionFilters.documentType = documentType
    approval.templateFilters.documentType = documentType
  }
  if (documentId) {
    approval.chainFilters.documentId = documentId
    approval.decisionFilters.documentId = documentId
  }
}

function formatStatus(value?: boolean | string | null) {
  if (typeof value === 'boolean') return value ? '启用' : '停用'
  return value ?? '—'
}

function openTaskDecision(row: BusinessConsoleApprovalTaskItem, decision: string) {
  if (!canManageApprovals.value || !row.chainId || row.stepNo === undefined) return
  decisionForm.chainId = row.chainId
  decisionForm.stepNo = row.stepNo
  decisionForm.decision = decision
  decisionForm.comment = ''
  taskDecisionOpen.value = true
}

async function quickResolveTask(row: BusinessConsoleApprovalTaskItem, decision: string) {
  if (!canManageApprovals.value || !row.chainId || row.stepNo === undefined) return
  try {
    await approval.resolveTask({
      chainId: row.chainId,
      stepNo: row.stepNo,
      decision,
      comment: '',
    })
    notifySuccess(`${documentLabel(row)} 已${decisionLabel(decision)}`)
  } catch (error) {
    notifyError(error, '审批处理失败，请稍后重试。')
  }
}

async function submitTaskDecision() {
  if (!decisionForm.chainId || decisionForm.stepNo <= 0) return
  try {
    await approval.resolveTask({ ...decisionForm })
    taskDecisionOpen.value = false
    notifySuccess(`审批任务已${decisionLabel(decisionForm.decision)}`)
  } catch (error) {
    notifyError(error, '审批处理失败，请稍后重试。')
  }
}

function decisionLabel(decision: string) {
  if (decision === 'Approve') return '通过'
  if (decision === 'Reject') return '驳回'
  if (decision === 'Resolve') return '处理'
  return '处理'
}

function viewChain(row: BusinessConsoleApprovalChainItem | BusinessConsoleApprovalTaskItem) {
  if (!row.chainId) return
  approval.chainDetailSelection.chainId = row.chainId
}

function openDelegation() {
  delegationForm.delegatorActorRef = actorRef.value
  delegationForm.delegateActorRef = ''
  delegationForm.documentType = ''
  delegationForm.effectiveFromUtc = ''
  delegationForm.effectiveToUtc = ''
  delegationForm.reason = ''
  delegationError.value = ''
  delegationOpen.value = true
}

async function submitDelegation() {
  if (!delegationForm.delegatorActorRef.trim() || !delegationForm.delegateActorRef.trim()) {
    delegationError.value = '请填写委托人与代理人。'
    return
  }

  try {
    delegationError.value = ''
    await approval.createDelegation({
      delegatorActorType: 'user',
      delegatorActorRef: delegationForm.delegatorActorRef.trim(),
      delegateActorType: 'user',
      delegateActorRef: delegationForm.delegateActorRef.trim(),
      documentType: delegationForm.documentType,
      effectiveFromUtc: delegationForm.effectiveFromUtc
        ? toIsoFromLocalInput(delegationForm.effectiveFromUtc)
        : undefined,
      effectiveToUtc: delegationForm.effectiveToUtc
        ? toIsoFromLocalInput(delegationForm.effectiveToUtc)
        : undefined,
      reason: delegationForm.reason,
    })
    delegationOpen.value = false
    notifySuccess('审批委托已生效')
  } catch (error) {
    notifyError(error, '委托保存失败，请稍后重试。')
  }
}

async function revokeDelegation(row: BusinessConsoleApprovalDelegationItem) {
  if (!row.delegationId || !canManageApprovals.value) return
  try {
    await approval.revokeDelegation(row.delegationId)
    notifySuccess('审批委托已撤销')
  } catch (error) {
    notifyError(error, '委托撤销失败，请稍后重试。')
  }
}

function openTemplate() {
  templateForm.templateCode = ''
  templateForm.documentType = ''
  templateForm.version = '1'
  templateForm.isActive = 'true'
  templateForm.stepNo = '10'
  templateForm.stepName = ''
  templateForm.approverType = 'role'
  templateForm.approverRef = ''
  templateForm.dueInHours = ''
  templateError.value = ''
  templateOpen.value = true
}

async function submitTemplate() {
  const version = Number(templateForm.version)
  const stepNo = Number(templateForm.stepNo)
  const dueInHours = templateForm.dueInHours.trim() ? Number(templateForm.dueInHours) : undefined
  if (
    !templateForm.templateCode.trim() ||
    !templateForm.documentType.trim() ||
    !templateForm.stepName.trim() ||
    !templateForm.approverRef.trim()
  ) {
    templateError.value = '请填写模板、单据类型、步骤和审批人。'
    return
  }
  if (!(version > 0) || !(stepNo > 0)) {
    templateError.value = '版本与步骤序号需为正数。'
    return
  }

  try {
    templateError.value = ''
    await approval.saveTemplate({
      templateCode: templateForm.templateCode.trim(),
      documentType: templateForm.documentType.trim(),
      version,
      isActive: templateForm.isActive === 'true',
      steps: [
        {
          stepNo,
          stepName: templateForm.stepName.trim(),
          approverType: templateForm.approverType,
          approverRef: templateForm.approverRef.trim(),
          dueInHours,
        },
      ],
    })
    templateOpen.value = false
    notifySuccess('审批模板已保存')
  } catch (error) {
    notifyError(error, '模板保存失败，请稍后重试。')
  }
}

function toIsoFromLocalInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toISOString()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="审批中心"
      :breadcrumbs="[{ label: '审批中心' }]"
      :count="`${approval.tasksTotal.value} 个待处理任务`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" @click="approval.refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div
      v-if="!canReadApprovals"
      class="rounded-md border bg-muted/40 p-6 text-sm text-muted-foreground"
      role="status"
    >
      当前账号没有审批中心访问权限。
    </div>

    <template v-else>
      <NvSectionCards :columns="3">
        <NvSectionCard
          description="待处理任务"
          :value="pendingTasks"
          hint="当前账号可处理的审批步骤"
        />
        <NvSectionCard description="进行中流程" :value="runningChains" hint="本页可见流程实例" />
        <NvSectionCard
          description="有效委托"
          :value="activeDelegations"
          hint="当前范围仍在生效的委托"
        />
      </NvSectionCards>

      <div
        v-if="!canManageApprovals"
        class="rounded-md border border-dashed bg-muted/30 p-3 text-sm text-muted-foreground"
        role="status"
      >
        没有审批处理权限；仅展示模板、流程、决策和委托记录。
      </div>

      <NvTabs default-value="tasks">
        <NvTabsList>
          <NvTabsTrigger value="tasks">我的任务 ({{ approval.tasksTotal.value }})</NvTabsTrigger>
          <NvTabsTrigger value="chains">流程实例 ({{ approval.chainsTotal.value }})</NvTabsTrigger>
          <NvTabsTrigger value="decisions"
            >决策记录 ({{ approval.decisionsTotal.value }})</NvTabsTrigger
          >
          <NvTabsTrigger value="delegations"
            >委托设置 ({{ approval.delegationsTotal.value }})</NvTabsTrigger
          >
          <NvTabsTrigger value="templates"
            >模板配置 ({{ approval.templatesTotal.value }})</NvTabsTrigger
          >
        </NvTabsList>

        <NvTabsContent value="tasks" class="grid gap-3">
          <NvDataTable
            manual
            :page="taskPager.page.value"
            :page-size="taskPager.pageSize.value"
            :total-items="approval.tasksTotal.value"
            :columns="taskColumns"
            :rows="approval.tasks.value"
            :row-key="rowKey"
            :loading="approval.tasksPending.value"
            :searchable="false"
            :column-settings="false"
            empty-message="当前没有待处理审批任务。"
            @update:page="taskPager.page.value = $event"
            @update:page-size="(v) => (taskPager.pageSize.value = String(v))"
          >
            <template #cell-actions="{ row }">
              <NvRowActions :label="`审批任务 ${documentLabel(row)}`">
                <NvDropdownMenuItem @click="viewChain(row)">
                  <EyeIcon aria-hidden="true" />
                  查看步骤
                </NvDropdownMenuItem>
                <NvDropdownMenuItem
                  v-if="canManageApprovals"
                  @click="quickResolveTask(row, 'Approve')"
                >
                  <CheckCircle2Icon aria-hidden="true" />
                  通过
                </NvDropdownMenuItem>
                <NvDropdownMenuItem
                  v-if="canManageApprovals"
                  @click="openTaskDecision(row, 'Reject')"
                >
                  <XCircleIcon aria-hidden="true" />
                  驳回
                </NvDropdownMenuItem>
                <NvDropdownMenuItem
                  v-if="canManageApprovals"
                  @click="openTaskDecision(row, 'Resolve')"
                >
                  <SendIcon aria-hidden="true" />
                  处理
                </NvDropdownMenuItem>
              </NvRowActions>
            </template>
          </NvDataTable>
        </NvTabsContent>

        <NvTabsContent value="chains" class="grid gap-3">
          <NvDataTable
            manual
            :page="chainPager.page.value"
            :page-size="chainPager.pageSize.value"
            :total-items="approval.chainsTotal.value"
            :columns="chainColumns"
            :rows="approval.chains.value"
            :row-key="rowKey"
            :loading="approval.chainsPending.value"
            :searchable="false"
            :column-settings="false"
            empty-message="当前没有审批流程。"
            @update:page="chainPager.page.value = $event"
            @update:page-size="(v) => (chainPager.pageSize.value = String(v))"
          >
            <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
            <template #cell-actions="{ row }">
              <NvButton size="sm" type="button" variant="ghost" @click="viewChain(row)">
                <EyeIcon aria-hidden="true" />
                步骤
              </NvButton>
            </template>
          </NvDataTable>

          <section v-if="approval.chainDetail.value" class="rounded-md border bg-card p-4">
            <div class="mb-3 flex items-center justify-between gap-3">
              <h2 class="text-base font-semibold">
                流程步骤 · {{ approval.chainDetail.value.documentId }}
              </h2>
              <NvStatusBadge :value="approval.chainDetail.value.status" />
            </div>
            <ol class="grid gap-2">
              <li
                v-for="step in approval.chainDetail.value.steps ?? []"
                :key="step.stepNo"
                class="rounded-md border bg-background p-3"
              >
                <div class="flex items-center justify-between gap-3">
                  <span class="font-medium">{{ step.stepName ?? `第 ${step.stepNo} 步` }}</span>
                  <NvStatusBadge :value="step.status" />
                </div>
                <p class="mt-1 text-sm text-muted-foreground">
                  {{ step.approverType }} · {{ step.approverRef }} · 到期
                  {{ formatDateTime(step.dueAtUtc) }}
                </p>
              </li>
            </ol>
          </section>
        </NvTabsContent>

        <NvTabsContent value="decisions" class="grid gap-3">
          <NvDataTable
            manual
            :page="decisionPager.page.value"
            :page-size="decisionPager.pageSize.value"
            :total-items="approval.decisionsTotal.value"
            :columns="decisionColumns"
            :rows="approval.decisions.value"
            :row-key="rowKey"
            :loading="approval.decisionsPending.value"
            :searchable="false"
            :column-settings="false"
            empty-message="当前没有审批决策记录。"
            @update:page="decisionPager.page.value = $event"
            @update:page-size="(v) => (decisionPager.pageSize.value = String(v))"
          >
            <template #cell-decision="{ row }"><NvStatusBadge :value="row.decision" /></template>
          </NvDataTable>
        </NvTabsContent>

        <NvTabsContent value="delegations" class="grid gap-3">
          <div class="flex justify-end">
            <NvButton v-if="canManageApprovals" size="sm" type="button" @click="openDelegation">
              <UserRoundPlusIcon aria-hidden="true" />
              新建委托
            </NvButton>
          </div>
          <NvDataTable
            manual
            :page="delegationPager.page.value"
            :page-size="delegationPager.pageSize.value"
            :total-items="approval.delegationsTotal.value"
            :columns="delegationColumns"
            :rows="approval.delegations.value"
            :row-key="rowKey"
            :loading="approval.delegationsPending.value"
            :searchable="false"
            :column-settings="false"
            empty-message="当前没有审批委托。"
            @update:page="delegationPager.page.value = $event"
            @update:page-size="(v) => (delegationPager.pageSize.value = String(v))"
          >
            <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
            <template #cell-actions="{ row }">
              <NvButton
                v-if="canManageApprovals && (row.status ?? '').toLowerCase() === 'active'"
                size="sm"
                type="button"
                variant="ghost"
                @click="revokeDelegation(row)"
              >
                <RotateCcwIcon aria-hidden="true" />
                撤销
              </NvButton>
            </template>
          </NvDataTable>
        </NvTabsContent>

        <NvTabsContent value="templates" class="grid gap-3">
          <div class="flex justify-end">
            <NvButton v-if="canManageApprovals" size="sm" type="button" @click="openTemplate">
              <FilePlus2Icon aria-hidden="true" />
              维护模板
            </NvButton>
          </div>
          <NvDataTable
            manual
            :page="templatePager.page.value"
            :page-size="templatePager.pageSize.value"
            :total-items="approval.templatesTotal.value"
            :columns="templateColumns"
            :rows="approval.templates.value"
            :row-key="rowKey"
            :loading="approval.templatesPending.value"
            :searchable="false"
            :column-settings="false"
            empty-message="当前没有审批模板。"
            @update:page="templatePager.page.value = $event"
            @update:page-size="(v) => (templatePager.pageSize.value = String(v))"
          >
            <template #cell-isActive="{ row }"
              ><NvStatusBadge :value="formatStatus(row.isActive)"
            /></template>
          </NvDataTable>
        </NvTabsContent>
      </NvTabs>
    </template>

    <NvDialog v-model:open="taskDecisionOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>处理审批任务</NvDialogTitle>
          <NvDialogDescription
            >{{
              decisionLabel(decisionForm.decision)
            }}当前审批步骤，并记录处理意见。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitTaskDecision">
          <NvField>
            <NvFieldLabel for="approval-comment">处理意见</NvFieldLabel>
            <NvInput id="approval-comment" v-model="decisionForm.comment" autocomplete="off" />
          </NvField>
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="approval.resolveTaskPending.value">
              <Spinner v-if="approval.resolveTaskPending.value" aria-hidden="true" />
              提交处理
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="delegationOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建审批委托</NvDialogTitle>
          <NvDialogDescription>把指定人员的审批任务临时委托给代理人。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitDelegation">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="approval-delegator">委托人</NvFieldLabel>
              <NvInput
                id="approval-delegator"
                v-model="delegationForm.delegatorActorRef"
                autocomplete="off"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-delegate">代理人</NvFieldLabel>
              <NvInput
                id="approval-delegate"
                v-model="delegationForm.delegateActorRef"
                autocomplete="off"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-delegation-doc">单据范围</NvFieldLabel>
              <NvInput
                id="approval-delegation-doc"
                v-model="delegationForm.documentType"
                autocomplete="off"
                placeholder="可选"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-delegation-from">开始时间</NvFieldLabel>
              <NvInput
                id="approval-delegation-from"
                v-model="delegationForm.effectiveFromUtc"
                type="datetime-local"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-delegation-to">截止时间</NvFieldLabel>
              <NvInput
                id="approval-delegation-to"
                v-model="delegationForm.effectiveToUtc"
                type="datetime-local"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-delegation-reason">原因</NvFieldLabel>
              <NvInput
                id="approval-delegation-reason"
                v-model="delegationForm.reason"
                autocomplete="off"
              />
            </NvField>
          </NvFieldGroup>
          <NvFieldError v-if="delegationError" :errors="[delegationError]" />
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="approval.createDelegationPending.value">
              <Spinner v-if="approval.createDelegationPending.value" aria-hidden="true" />
              保存委托
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="templateOpen">
      <NvDialogContent class="sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>维护审批模板</NvDialogTitle>
          <NvDialogDescription
            >维护业务单据审批模板和首个步骤；复杂步骤继续由后端模板能力承载。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitTemplate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="approval-template-code">模板</NvFieldLabel>
              <NvInput
                id="approval-template-code"
                v-model="templateForm.templateCode"
                autocomplete="off"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-template-doc">单据类型</NvFieldLabel>
              <NvInput
                id="approval-template-doc"
                v-model="templateForm.documentType"
                autocomplete="off"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-template-version">版本</NvFieldLabel>
              <NvInput
                id="approval-template-version"
                v-model="templateForm.version"
                type="number"
                min="1"
                step="1"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-template-active">状态</NvFieldLabel>
              <NvSelect v-model="templateForm.isActive">
                <NvSelectTrigger id="approval-template-active"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="true">启用</NvSelectItem>
                  <NvSelectItem value="false">停用</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-template-step-no">步骤序号</NvFieldLabel>
              <NvInput
                id="approval-template-step-no"
                v-model="templateForm.stepNo"
                type="number"
                min="1"
                step="1"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-template-step-name">步骤名称</NvFieldLabel>
              <NvInput
                id="approval-template-step-name"
                v-model="templateForm.stepName"
                autocomplete="off"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-template-actor-type">审批人类型</NvFieldLabel>
              <NvSelect v-model="templateForm.approverType">
                <NvSelectTrigger id="approval-template-actor-type"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="role">角色</NvSelectItem>
                  <NvSelectItem value="user">人员</NvSelectItem>
                  <NvSelectItem value="department">部门</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-template-actor-ref">审批人</NvFieldLabel>
              <NvInput
                id="approval-template-actor-ref"
                v-model="templateForm.approverRef"
                autocomplete="off"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="approval-template-due">处理时限（小时）</NvFieldLabel>
              <NvInput
                id="approval-template-due"
                v-model="templateForm.dueInHours"
                type="number"
                min="1"
                step="1"
                placeholder="可选"
              />
            </NvField>
          </NvFieldGroup>
          <NvFieldError v-if="templateError" :errors="[templateError]" />
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="approval.saveTemplatePending.value">
              <Spinner v-if="approval.saveTemplatePending.value" aria-hidden="true" />
              保存模板
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
