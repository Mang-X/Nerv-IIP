<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvMetricCard,
  NvMetricStrip,
  NvPageHeader,
  NvTabs,
  NvTabsList,
  NvTabsTrigger,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import ApprovalTasksTab from './components/ApprovalTasksTab.vue'
import ApprovalChainsTab from './components/ApprovalChainsTab.vue'
import ApprovalDecisionsTab from './components/ApprovalDecisionsTab.vue'
import ApprovalDelegationsTab from './components/ApprovalDelegationsTab.vue'
import ApprovalTemplatesTab from './components/ApprovalTemplatesTab.vue'
import ApprovalTaskDecisionDialog from './components/ApprovalTaskDecisionDialog.vue'
import ApprovalDelegationDialog from './components/ApprovalDelegationDialog.vue'
import ApprovalTemplateDialog from './components/ApprovalTemplateDialog.vue'
import { useApprovalPage } from './useApprovalPage'

definePage({
  meta: {
    requiresAuth: true,
    title: '审批中心',
    requiredPermissions: ['business.approvals.read', 'business.approvals.manage'],
  },
})

const {
  APPROVAL_AWAITING_MESSAGE,
  activeDelegations,
  activeTab,
  approval,
  approverLabel,
  approverOptions,
  canManageApprovals,
  canReadApprovals,
  chainColumns,
  chainPager,
  chainsState,
  decisionColumns,
  decisionContextItems,
  decisionForm,
  decisionLabel,
  decisionPager,
  decisionTarget,
  decisionsState,
  delegateOptions,
  delegationColumns,
  delegationDocumentType,
  delegationError,
  delegationForm,
  delegationOpen,
  delegationPager,
  delegationsState,
  departmentsPending,
  documentLabel,
  documentTypeOptions,
  formatDateTime,
  formatStatus,
  headerCount,
  openDelegation,
  openTaskDecision,
  openTemplate,
  pendingTasks,
  quickResolveTask,
  revokeDelegation,
  rowKey,
  runningChains,
  submitDelegation,
  submitTaskDecision,
  submitTemplate,
  taskColumns,
  taskDecisionOpen,
  taskPager,
  tasksState,
  templateCodeSuggestions,
  templateColumns,
  templateError,
  templateForm,
  templateOpen,
  templatePager,
  templatesState,
  viewChain,
  workersPending,
} = useApprovalPage()
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="审批中心" :breadcrumbs="[{ label: '审批中心' }]" :count="headerCount">
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
      <div class="grid gap-4 lg:grid-cols-[minmax(0,26rem)_minmax(0,1fr)]">
        <!-- 读不到任务时不许说"已清空"：那是在没有依据的情况下断言"没你的事"。 -->
        <NvMetricCard
          variant="alert"
          label="待我处理"
          :value="tasksState.trustworthy ? pendingTasks : '—'"
          :unit="tasksState.trustworthy ? '项' : ''"
          :tone="tasksState.trustworthy && pendingTasks > 0 ? 'warning' : 'neutral'"
          :status="
            !tasksState.trustworthy
              ? { label: '无法判断', tone: 'neutral' }
              : pendingTasks > 0
                ? { label: '等待决策', tone: 'warning' }
                : { label: '已清空', tone: 'success' }
          "
          :foot-start="
            !tasksState.trustworthy
              ? tasksState.emptyMessage
              : pendingTasks > 0
                ? '单据在你这一步停着，下游的执行也会一起等。'
                : '当前没有等你决策的审批。'
          "
          :action="tasksState.trustworthy && pendingTasks > 0 ? { label: '去处理' } : undefined"
          @action="activeTab = 'tasks'"
        />
        <NvMetricStrip
          :cells="[
            {
              key: 'running',
              label: '进行中审批',
              value: chainsState.trustworthy ? runningChains : '—',
              unit: chainsState.trustworthy ? '单' : '',
              meta: chainsState.trustworthy ? undefined : chainsState.emptyMessage,
            },
            {
              key: 'delegation',
              label: '生效中的委托',
              value: delegationsState.trustworthy ? activeDelegations : '—',
              unit: delegationsState.trustworthy ? '条' : '',
              meta: delegationsState.trustworthy
                ? '代批期间由被委托人决策'
                : delegationsState.emptyMessage,
            },
          ]"
        />
      </div>

      <div
        v-if="!canManageApprovals"
        class="rounded-md border border-dashed bg-muted/30 p-3 text-sm text-muted-foreground"
        role="status"
      >
        没有审批处理权限；仅展示模板、流程、决策和委托记录。
      </div>

      <NvTabs v-model="activeTab">
        <NvTabsList>
          <!-- 每个页签的数字各自独立：某一路取不到就显 `—` / 「取不到」，不显 0 -->
          <NvTabsTrigger value="tasks">我的任务 ({{ tasksState.tabCount }})</NvTabsTrigger>
          <NvTabsTrigger value="chains">审批中的单据 ({{ chainsState.tabCount }})</NvTabsTrigger>
          <NvTabsTrigger value="decisions">决策记录 ({{ decisionsState.tabCount }})</NvTabsTrigger>
          <NvTabsTrigger value="delegations"
            >委托设置 ({{ delegationsState.tabCount }})</NvTabsTrigger
          >
          <NvTabsTrigger value="templates">模板配置 ({{ templatesState.tabCount }})</NvTabsTrigger>
        </NvTabsList>

        <ApprovalTasksTab
          :-a-p-p-r-o-v-a-l_-a-w-a-i-t-i-n-g_-m-e-s-s-a-g-e="APPROVAL_AWAITING_MESSAGE"
          :approval="approval"
          :can-manage-approvals="canManageApprovals"
          :document-label="documentLabel"
          :open-task-decision="openTaskDecision"
          :quick-resolve-task="quickResolveTask"
          :row-key="rowKey"
          :task-columns="taskColumns"
          :task-pager="taskPager"
          :tasks-state="tasksState"
          :view-chain="viewChain"
        />

        <ApprovalChainsTab
          :-a-p-p-r-o-v-a-l_-a-w-a-i-t-i-n-g_-m-e-s-s-a-g-e="APPROVAL_AWAITING_MESSAGE"
          :approval="approval"
          :approver-label="approverLabel"
          :chain-columns="chainColumns"
          :chain-pager="chainPager"
          :chains-state="chainsState"
          :format-date-time="formatDateTime"
          :row-key="rowKey"
          :view-chain="viewChain"
        />

        <ApprovalDecisionsTab
          :-a-p-p-r-o-v-a-l_-a-w-a-i-t-i-n-g_-m-e-s-s-a-g-e="APPROVAL_AWAITING_MESSAGE"
          :approval="approval"
          :decision-columns="decisionColumns"
          :decision-pager="decisionPager"
          :decisions-state="decisionsState"
          :row-key="rowKey"
        />

        <ApprovalDelegationsTab
          :-a-p-p-r-o-v-a-l_-a-w-a-i-t-i-n-g_-m-e-s-s-a-g-e="APPROVAL_AWAITING_MESSAGE"
          :approval="approval"
          :can-manage-approvals="canManageApprovals"
          :delegation-columns="delegationColumns"
          :delegation-pager="delegationPager"
          :delegations-state="delegationsState"
          :open-delegation="openDelegation"
          :revoke-delegation="revokeDelegation"
          :row-key="rowKey"
        />

        <ApprovalTemplatesTab
          :-a-p-p-r-o-v-a-l_-a-w-a-i-t-i-n-g_-m-e-s-s-a-g-e="APPROVAL_AWAITING_MESSAGE"
          :approval="approval"
          :can-manage-approvals="canManageApprovals"
          :format-status="formatStatus"
          :open-template="openTemplate"
          :row-key="rowKey"
          :template-columns="templateColumns"
          :template-pager="templatePager"
          :templates-state="templatesState"
        />
      </NvTabs>
    </template>

    <!-- 仅有审批处理权限时才装载：只读账号既开不了这个弹窗，也不该出现它的决策按钮。 -->
    <ApprovalTaskDecisionDialog
      v-model:task-decision-open="taskDecisionOpen"
      :approval="approval"
      :can-manage-approvals="canManageApprovals"
      :decision-context-items="decisionContextItems"
      :decision-form="decisionForm"
      :decision-label="decisionLabel"
      :decision-target="decisionTarget"
      :document-label="documentLabel"
      :submit-task-decision="submitTaskDecision"
    />

    <ApprovalDelegationDialog
      v-model:delegation-open="delegationOpen"
      v-model:delegation-document-type="delegationDocumentType"
      :approval="approval"
      :delegate-options="delegateOptions"
      :delegation-error="delegationError"
      :delegation-form="delegationForm"
      :document-type-options="documentTypeOptions"
      :submit-delegation="submitDelegation"
      :workers-pending="workersPending"
    />

    <ApprovalTemplateDialog
      v-model:template-open="templateOpen"
      :approval="approval"
      :approver-options="approverOptions"
      :departments-pending="departmentsPending"
      :document-type-options="documentTypeOptions"
      :submit-template="submitTemplate"
      :template-code-suggestions="templateCodeSuggestions"
      :template-error="templateError"
      :template-form="templateForm"
      :workers-pending="workersPending"
    />
  </BusinessLayout>
</template>
