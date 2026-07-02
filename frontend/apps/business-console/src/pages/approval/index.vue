<script setup lang="ts">
import type {
  BusinessConsoleApprovalChainItem,
  BusinessConsoleApprovalDecisionListItem,
  BusinessConsoleApprovalDelegationItem,
  BusinessConsoleApprovalTaskItem,
  BusinessConsoleApprovalTemplateItem,
} from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useBusinessApproval } from '@/composables/useBusinessApproval'
import { usePagedList } from '@/composables/usePagedList'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifySuccess } from '@/utils/notify'
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
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  TabsPro,
  TabsProContent,
  TabsProList,
  TabsProTrigger,
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
} from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { storeToRefs } from 'pinia'
import { useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '审批中心',
    requiredPermissions: [P.approvalsRead, P.approvalsManage],
  },
})

const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const permissionCodes = computed(() => principal.value?.permissionCodes ?? [])
const actorRef = computed(() => principal.value?.principalId ?? principal.value?.loginName ?? '')
const actor = computed(() => ({ actorType: 'user', actorRef: actorRef.value }))
const canReadApprovals = computed(() => permissionCodes.value.includes(P.approvalsRead) || permissionCodes.value.includes(P.approvalsManage))
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

const taskColumns: DataTableProColumn<BusinessConsoleApprovalTaskItem>[] = [
  { key: 'documentId', header: '单据', cellClass: 'font-medium', accessor: documentLabel },
  { key: 'stepName', header: '当前步骤', accessor: (row) => row.stepName ?? `第 ${row.stepNo ?? '—'} 步` },
  { key: 'documentType', header: '单据类型', accessor: (row) => row.documentType ?? '—' },
  { key: 'dueAtUtc', header: '到期时间', accessor: (row) => formatDateTime(row.dueAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

const chainColumns: DataTableProColumn<BusinessConsoleApprovalChainItem>[] = [
  { key: 'documentId', header: '单据', cellClass: 'font-medium', accessor: documentLabel },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'templateCode', header: '模板', accessor: (row) => row.templateCode ?? '—' },
  { key: 'startedBy', header: '发起人', accessor: (row) => row.startedBy ?? '—' },
  { key: 'startedAtUtc', header: '发起时间', accessor: (row) => formatDateTime(row.startedAtUtc) },
  { key: 'actions', header: '步骤', align: 'end', width: 'w-12' },
]

const decisionColumns: DataTableProColumn<BusinessConsoleApprovalDecisionListItem>[] = [
  { key: 'documentId', header: '单据', cellClass: 'font-medium', accessor: documentLabel },
  { key: 'decision', header: '决策', width: 'w-24' },
  { key: 'actorRef', header: '处理人', accessor: (row) => row.actorRef ?? '—' },
  { key: 'comment', header: '意见', accessor: (row) => row.comment ?? '—' },
  { key: 'decidedAtUtc', header: '处理时间', accessor: (row) => formatDateTime(row.decidedAtUtc) },
]

const delegationColumns: DataTableProColumn<BusinessConsoleApprovalDelegationItem>[] = [
  { key: 'delegatorActorRef', header: '委托人', cellClass: 'font-medium', accessor: (row) => row.delegatorActorRef ?? '—' },
  { key: 'delegateActorRef', header: '代理人', accessor: (row) => row.delegateActorRef ?? '—' },
  { key: 'documentType', header: '单据范围', accessor: (row) => row.documentType ?? '全部业务单据' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveToUtc', header: '截止时间', accessor: (row) => formatDateTime(row.effectiveToUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

const templateColumns: DataTableProColumn<BusinessConsoleApprovalTemplateItem>[] = [
  { key: 'templateCode', header: '模板', cellClass: 'font-medium', accessor: (row) => row.templateCode ?? '—' },
  { key: 'documentType', header: '单据类型', accessor: (row) => row.documentType ?? '—' },
  { key: 'version', header: '版本', width: 'w-20', accessor: (row) => String(row.version ?? '—') },
  { key: 'isActive', header: '状态', width: 'w-24' },
  { key: 'steps', header: '步骤', accessor: (row) => `${row.steps?.length ?? 0} 步` },
]

const activeDelegations = computed(() =>
  approval.delegations.value.filter((item) => (item.status ?? '').toLowerCase() === 'active').length,
)
const pendingTasks = computed(() => approval.tasks.value.length)
const runningChains = computed(() =>
  approval.chains.value.filter((item) => ['running', 'pending', 'open'].includes((item.status ?? '').toLowerCase())).length,
)

applyRouteApprovalFilters()

function documentLabel(row: { documentType?: string | null, documentId?: string | null }) {
  const id = row.documentId ?? ''
  return id ? `${row.documentType ?? '业务单据'} · ${id}` : row.documentType ?? '业务单据'
}

function rowKey(row: Record<string, unknown>) {
  return String(row.chainId ?? row.delegationId ?? row.templateId ?? row.decisionId ?? row.documentId ?? JSON.stringify(row))
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
      effectiveFromUtc: delegationForm.effectiveFromUtc ? toIsoFromLocalInput(delegationForm.effectiveFromUtc) : undefined,
      effectiveToUtc: delegationForm.effectiveToUtc ? toIsoFromLocalInput(delegationForm.effectiveToUtc) : undefined,
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
  if (!templateForm.templateCode.trim() || !templateForm.documentType.trim() || !templateForm.stepName.trim() || !templateForm.approverRef.trim()) {
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
      steps: [{
        stepNo,
        stepName: templateForm.stepName.trim(),
        approverType: templateForm.approverType,
        approverRef: templateForm.approverRef.trim(),
        dueInHours,
      }],
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
    <PageHeader title="审批中心" :breadcrumbs="[{ label: '审批中心' }]" :count="`${approval.tasksTotal.value} 个待处理任务`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" @click="approval.refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <div v-if="!canReadApprovals" class="rounded-md border bg-muted/40 p-6 text-sm text-muted-foreground" role="status">
      当前账号没有审批中心访问权限。
    </div>

    <template v-else>
      <SectionCards :columns="3">
        <SectionCard description="待处理任务" :value="pendingTasks" hint="当前账号可处理的审批步骤" />
        <SectionCard description="进行中流程" :value="runningChains" hint="本页可见流程实例" />
        <SectionCard description="有效委托" :value="activeDelegations" hint="当前范围仍在生效的委托" />
      </SectionCards>

      <div v-if="!canManageApprovals" class="rounded-md border border-dashed bg-muted/30 p-3 text-sm text-muted-foreground" role="status">
        没有审批处理权限；仅展示模板、流程、决策和委托记录。
      </div>

      <TabsPro default-value="tasks">
        <TabsProList>
          <TabsProTrigger value="tasks">我的任务 ({{ approval.tasksTotal.value }})</TabsProTrigger>
          <TabsProTrigger value="chains">流程实例 ({{ approval.chainsTotal.value }})</TabsProTrigger>
          <TabsProTrigger value="decisions">决策记录 ({{ approval.decisionsTotal.value }})</TabsProTrigger>
          <TabsProTrigger value="delegations">委托设置 ({{ approval.delegationsTotal.value }})</TabsProTrigger>
          <TabsProTrigger value="templates">模板配置 ({{ approval.templatesTotal.value }})</TabsProTrigger>
        </TabsProList>

        <TabsProContent value="tasks" class="grid gap-3">
          <DataTablePro
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
              <RowActions :label="`审批任务 ${documentLabel(row)}`">
                <DropdownMenuProItem @click="viewChain(row)">
                  <EyeIcon aria-hidden="true" />
                  查看步骤
                </DropdownMenuProItem>
                <DropdownMenuProItem v-if="canManageApprovals" @click="quickResolveTask(row, 'Approve')">
                  <CheckCircle2Icon aria-hidden="true" />
                  通过
                </DropdownMenuProItem>
                <DropdownMenuProItem v-if="canManageApprovals" @click="openTaskDecision(row, 'Reject')">
                  <XCircleIcon aria-hidden="true" />
                  驳回
                </DropdownMenuProItem>
                <DropdownMenuProItem v-if="canManageApprovals" @click="openTaskDecision(row, 'Resolve')">
                  <SendIcon aria-hidden="true" />
                  处理
                </DropdownMenuProItem>
              </RowActions>
            </template>
          </DataTablePro>
        </TabsProContent>

        <TabsProContent value="chains" class="grid gap-3">
          <DataTablePro
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
            <template #cell-status="{ row }"><StatusBadgePro :value="row.status" /></template>
            <template #cell-actions="{ row }">
              <ButtonPro size="sm" type="button" variant="ghost" @click="viewChain(row)">
                <EyeIcon aria-hidden="true" />
                步骤
              </ButtonPro>
            </template>
          </DataTablePro>

          <section v-if="approval.chainDetail.value" class="rounded-md border bg-card p-4">
            <div class="mb-3 flex items-center justify-between gap-3">
              <h2 class="text-base font-semibold">流程步骤 · {{ approval.chainDetail.value.documentId }}</h2>
              <StatusBadgePro :value="approval.chainDetail.value.status" />
            </div>
            <ol class="grid gap-2">
              <li v-for="step in approval.chainDetail.value.steps ?? []" :key="step.stepNo" class="rounded-md border bg-background p-3">
                <div class="flex items-center justify-between gap-3">
                  <span class="font-medium">{{ step.stepName ?? `第 ${step.stepNo} 步` }}</span>
                  <StatusBadgePro :value="step.status" />
                </div>
                <p class="mt-1 text-sm text-muted-foreground">
                  {{ step.approverType }} · {{ step.approverRef }} · 到期 {{ formatDateTime(step.dueAtUtc) }}
                </p>
              </li>
            </ol>
          </section>
        </TabsProContent>

        <TabsProContent value="decisions" class="grid gap-3">
          <DataTablePro
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
            <template #cell-decision="{ row }"><StatusBadgePro :value="row.decision" /></template>
          </DataTablePro>
        </TabsProContent>

        <TabsProContent value="delegations" class="grid gap-3">
          <div class="flex justify-end">
            <ButtonPro v-if="canManageApprovals" size="sm" type="button" @click="openDelegation">
              <UserRoundPlusIcon aria-hidden="true" />
              新建委托
            </ButtonPro>
          </div>
          <DataTablePro
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
            <template #cell-status="{ row }"><StatusBadgePro :value="row.status" /></template>
            <template #cell-actions="{ row }">
              <ButtonPro
                v-if="canManageApprovals && (row.status ?? '').toLowerCase() === 'active'"
                size="sm"
                type="button"
                variant="ghost"
                @click="revokeDelegation(row)"
              >
                <RotateCcwIcon aria-hidden="true" />
                撤销
              </ButtonPro>
            </template>
          </DataTablePro>
        </TabsProContent>

        <TabsProContent value="templates" class="grid gap-3">
          <div class="flex justify-end">
            <ButtonPro v-if="canManageApprovals" size="sm" type="button" @click="openTemplate">
              <FilePlus2Icon aria-hidden="true" />
              维护模板
            </ButtonPro>
          </div>
          <DataTablePro
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
            <template #cell-isActive="{ row }"><StatusBadgePro :value="formatStatus(row.isActive)" /></template>
          </DataTablePro>
        </TabsProContent>
      </TabsPro>
    </template>

    <DialogPro v-model:open="taskDecisionOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>处理审批任务</DialogProTitle>
          <DialogProDescription>{{ decisionLabel(decisionForm.decision) }}当前审批步骤，并记录处理意见。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitTaskDecision">
          <FieldPro>
            <FieldProLabel for="approval-comment">处理意见</FieldProLabel>
            <InputPro id="approval-comment" v-model="decisionForm.comment" autocomplete="off" />
          </FieldPro>
          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="approval.resolveTaskPending.value">
              <Spinner v-if="approval.resolveTaskPending.value" aria-hidden="true" />
              提交处理
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>

    <DialogPro v-model:open="delegationOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>新建审批委托</DialogProTitle>
          <DialogProDescription>把指定人员的审批任务临时委托给代理人。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitDelegation">
          <FieldProGroup class="grid gap-3 sm:grid-cols-2">
            <FieldPro>
              <FieldProLabel for="approval-delegator">委托人</FieldProLabel>
              <InputPro id="approval-delegator" v-model="delegationForm.delegatorActorRef" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-delegate">代理人</FieldProLabel>
              <InputPro id="approval-delegate" v-model="delegationForm.delegateActorRef" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-delegation-doc">单据范围</FieldProLabel>
              <InputPro id="approval-delegation-doc" v-model="delegationForm.documentType" autocomplete="off" placeholder="可选" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-delegation-from">开始时间</FieldProLabel>
              <InputPro id="approval-delegation-from" v-model="delegationForm.effectiveFromUtc" type="datetime-local" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-delegation-to">截止时间</FieldProLabel>
              <InputPro id="approval-delegation-to" v-model="delegationForm.effectiveToUtc" type="datetime-local" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-delegation-reason">原因</FieldProLabel>
              <InputPro id="approval-delegation-reason" v-model="delegationForm.reason" autocomplete="off" />
            </FieldPro>
          </FieldProGroup>
          <FieldProError v-if="delegationError" :errors="[delegationError]" />
          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="approval.createDelegationPending.value">
              <Spinner v-if="approval.createDelegationPending.value" aria-hidden="true" />
              保存委托
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>

    <DialogPro v-model:open="templateOpen">
      <DialogProContent class="sm:max-w-2xl">
        <DialogProHeader>
          <DialogProTitle>维护审批模板</DialogProTitle>
          <DialogProDescription>维护业务单据审批模板和首个步骤；复杂步骤继续由后端模板能力承载。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitTemplate">
          <FieldProGroup class="grid gap-3 sm:grid-cols-2">
            <FieldPro>
              <FieldProLabel for="approval-template-code">模板</FieldProLabel>
              <InputPro id="approval-template-code" v-model="templateForm.templateCode" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-template-doc">单据类型</FieldProLabel>
              <InputPro id="approval-template-doc" v-model="templateForm.documentType" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-template-version">版本</FieldProLabel>
              <InputPro id="approval-template-version" v-model="templateForm.version" type="number" min="1" step="1" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-template-active">状态</FieldProLabel>
              <SelectPro v-model="templateForm.isActive">
                <SelectProTrigger id="approval-template-active"><SelectProValue /></SelectProTrigger>
                <SelectProContent>
                  <SelectProItem value="true">启用</SelectProItem>
                  <SelectProItem value="false">停用</SelectProItem>
                </SelectProContent>
              </SelectPro>
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-template-step-no">步骤序号</FieldProLabel>
              <InputPro id="approval-template-step-no" v-model="templateForm.stepNo" type="number" min="1" step="1" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-template-step-name">步骤名称</FieldProLabel>
              <InputPro id="approval-template-step-name" v-model="templateForm.stepName" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-template-actor-type">审批人类型</FieldProLabel>
              <SelectPro v-model="templateForm.approverType">
                <SelectProTrigger id="approval-template-actor-type"><SelectProValue /></SelectProTrigger>
                <SelectProContent>
                  <SelectProItem value="role">角色</SelectProItem>
                  <SelectProItem value="user">人员</SelectProItem>
                  <SelectProItem value="department">部门</SelectProItem>
                </SelectProContent>
              </SelectPro>
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-template-actor-ref">审批人</FieldProLabel>
              <InputPro id="approval-template-actor-ref" v-model="templateForm.approverRef" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="approval-template-due">处理时限（小时）</FieldProLabel>
              <InputPro id="approval-template-due" v-model="templateForm.dueInHours" type="number" min="1" step="1" placeholder="可选" />
            </FieldPro>
          </FieldProGroup>
          <FieldProError v-if="templateError" :errors="[templateError]" />
          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="approval.saveTemplatePending.value">
              <Spinner v-if="approval.saveTemplatePending.value" aria-hidden="true" />
              保存模板
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
