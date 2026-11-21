import type {
  BusinessConsoleApprovalChainItem,
  BusinessConsoleApprovalDecisionListItem,
  BusinessConsoleApprovalDelegationItem,
  BusinessConsoleApprovalTaskItem,
  BusinessConsoleApprovalTemplateItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useBusinessApproval } from '@/composables/approval/useBusinessApproval'
import { useBusinessMasterDataResources, useBusinessWorkers } from '@/composables/master-data'
import {
  APPROVAL_DECISION_ACTION_LABELS,
  APPROVAL_DOCUMENT_TYPE_OPTIONS,
  type ApprovalDecisionValue,
} from '@/data/approvalReference'
import { DOCUMENT_TYPE_LABELS, labelFor } from '@/data/businessLabels'
import { usePagedList } from '@/composables/shared/usePagedList'
import { useAuthStore } from '@/stores/auth'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { computed, reactive, shallowRef, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useRoute } from 'vue-router'

export function useApprovalPage() {
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
  // 所选审批任务行：弹窗里的单据/步骤/到期全部从它带出，不让审批人再找一遍。
  const decisionTarget = shallowRef<BusinessConsoleApprovalTaskItem | null>(null)
  const decisionForm = reactive({
    chainId: '',
    stepNo: 0,
    // 权威取值是小写 approve/reject/return（`APPROVAL_DECISION_VALUES`）——曾经写死大写导致一切裁决必 400（#1311）。
    decision: 'approve' as ApprovalDecisionValue,
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

  // ── 模板 / 单据类型 / 审批人 的受控取值 ──────────────────────────
  const documentTypeOptions = APPROVAL_DOCUMENT_TYPE_OPTIONS
  /**
   * 单据类型码值 → 中文。
   *
   * 两级查表：先查发起审批的受控值（措辞必须与新建下拉一致），再退到跨域显示词表——
   * 审批列表会回显历史链路上的其它单据类型，那些不在受控值里，但同样不能把英文码印上屏。
   */
  function documentTypeLabel(value?: string | null, fallback = '') {
    const code = (value ?? '').trim()
    if (!code) return fallback
    const controlled = documentTypeOptions.find((option) => option.value === code)?.label
    return controlled ?? labelFor(DOCUMENT_TYPE_LABELS, code, fallback || '—')
  }

  // 委托的「单据范围」可留空代表全部单据；NvSelect 不接受空串值，用 `all` 哨兵代理。
  const delegationDocumentType = computed({
    get: () => (delegationForm.documentType.trim() ? delegationForm.documentType : 'all'),
    set: (value: string) => {
      delegationForm.documentType = value === 'all' ? '' : value
    },
  })

  // 审批人类型是上游：换了类型，原来的审批人取值必然作废。
  watch(
    () => templateForm.approverType,
    () => {
      templateForm.approverRef = ''
    },
  )

  // 模板编码既可能复用已有模板（改版本 / 改步骤），也可能是本次新建的编码——
  // 给已有模板编码做建议，同时保留录入新编码的能力。
  const templateCodeSuggestions = computed(() => {
    const byCode = new Map<string, { value: string; label: string; hint?: string }>()
    for (const template of approval.templates.value) {
      const code = template.templateCode?.trim()
      if (!code || byCode.has(code)) continue
      byCode.set(code, {
        value: code,
        label: code,
        hint: [documentTypeLabel(template.documentType), `v${template.version ?? 1}`]
          .filter(Boolean)
          .join(' · '),
      })
    }
    return [...byCode.values()]
  })

  // 审批人取值随「审批人类型」切换目录：人员走员工名录，部门走部门主数据；
  // 角色目录在平台管理台（IAM）维护，业务网关无读面，暂保留手工录入（后端缺口已登记）。
  const { workers, workersPending } = useBusinessWorkers()
  const { resources: departments, resourcesPending: departmentsPending } =
    useBusinessMasterDataResources('department')

  const workerOptions = computed(() =>
    workers.value
      .filter((row) => !!row.userId && row.active !== false)
      .map((row) => ({
        value: row.userId as string,
        label: row.displayName?.trim() || '—',
        hint: [row.employeeNo, row.departmentName ?? row.departmentCode]
          .filter(Boolean)
          .join(' · '),
      })),
  )
  const departmentOptions = computed(() =>
    departments.value
      .filter((row) => !!row.code && row.active !== false)
      .map((row) => ({
        value: row.code as string,
        label: row.displayName || (row.code as string),
        hint: row.code ?? undefined,
      })),
  )
  // 代理人从员工名录里选；委托人固定为当前登录人（openDelegation 已带入），不做二次挑选。
  const delegateOptions = computed(() => {
    const current = delegationForm.delegateActorRef.trim()
    const options = workerOptions.value.filter(
      (option) => option.value !== delegationForm.delegatorActorRef.trim(),
    )
    if (current && !options.some((option) => option.value === current)) {
      return [{ value: current, label: '—', hint: undefined }, ...options]
    }
    return options
  })

  const approverOptions = computed(() => {
    const options =
      templateForm.approverType === 'user'
        ? workerOptions.value
        : templateForm.approverType === 'department'
          ? departmentOptions.value
          : []
    const current = templateForm.approverRef.trim()
    if (current && !options.some((option) => option.value === current)) {
      return [{ value: current, label: '—', hint: undefined }, ...options]
    }
    return options
  })

  const taskColumns: NvDataTableColumn<BusinessConsoleApprovalTaskItem>[] = [
    { key: 'documentId', header: '单据', cellClass: 'font-medium', accessor: documentLabel },
    {
      key: 'stepName',
      header: '当前步骤',
      accessor: (row) => row.stepName ?? `第 ${row.stepNo ?? '—'} 步`,
    },
    {
      key: 'documentType',
      header: '单据类型',
      accessor: (row) => documentTypeLabel(row.documentType, '—'),
    },
    { key: 'dueAtUtc', header: '到期时间', accessor: (row) => formatDateTime(row.dueAtUtc) },
    { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
  ]

  const chainColumns: NvDataTableColumn<BusinessConsoleApprovalChainItem>[] = [
    { key: 'documentId', header: '单据', cellClass: 'font-medium', accessor: documentLabel },
    { key: 'status', header: '状态', width: 'w-24' },
    { key: 'templateCode', header: '模板', accessor: (row) => row.templateCode ?? '—' },
    { key: 'startedBy', header: '发起人', accessor: (row) => row.startedBy ?? '—' },
    {
      key: 'startedAtUtc',
      header: '发起时间',
      accessor: (row) => formatDateTime(row.startedAtUtc),
    },
    { key: 'actions', header: '步骤', align: 'end', width: 'w-12' },
  ]

  const decisionColumns: NvDataTableColumn<BusinessConsoleApprovalDecisionListItem>[] = [
    { key: 'documentId', header: '单据', cellClass: 'font-medium', accessor: documentLabel },
    { key: 'decision', header: '决策', width: 'w-24' },
    { key: 'actorRef', header: '处理人', accessor: (row) => row.actorRef ?? '—' },
    { key: 'comment', header: '意见', accessor: (row) => row.comment ?? '—' },
    {
      key: 'decidedAtUtc',
      header: '处理时间',
      accessor: (row) => formatDateTime(row.decidedAtUtc),
    },
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
      accessor: (row) => documentTypeLabel(row.documentType, '全部业务单据'),
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
    {
      key: 'documentType',
      header: '单据类型',
      accessor: (row) => documentTypeLabel(row.documentType, '—'),
    },
    {
      key: 'version',
      header: '版本',
      width: 'w-20',
      accessor: (row) => String(row.version ?? '—'),
    },
    { key: 'isActive', header: '状态', width: 'w-24' },
    { key: 'steps', header: '步骤', accessor: (row) => `${row.steps?.length ?? 0} 步` },
  ]

  const activeDelegations = computed(
    () =>
      approval.delegations.value.filter((item) => (item.status ?? '').toLowerCase() === 'active')
        .length,
  )
  const pendingTasks = computed(() => approval.tasks.value.length)
  const activeTab = shallowRef('tasks')
  const runningChains = computed(
    () =>
      approval.chains.value.filter((item) =>
        ['running', 'pending', 'open'].includes((item.status ?? '').toLowerCase()),
      ).length,
  )

  /**
   * 五张读面表各自的状态。
   *
   * 五张表原本一律只有 `:loading` + `empty-message`，composable 早就导出了 `*Error`
   * 却从没被消费——「接口 500」和「真的没有待办」渲染成同一句「当前没有…」，等于用故障
   * 冒充"审批都清空了"。这里把「未选范围 / 在途 / 失败 / 真的 0 条」拆开。
   *
   * `contextReady` 必须单独判：上下文未就绪时查询 `enabled:false`，pinia-colada 的
   * `asyncStatus` 停在 `idle`，`isLoading` 为 **false**，pending 兜不住这一态。
   */
  interface ApprovalReadState {
    trustworthy: boolean
    tabCount: string
    emptyMessage: string
    error: unknown
    errorMessage: string | undefined
    awaitingScope: boolean
  }

  const APPROVAL_AWAITING_MESSAGE = '尚未选择业务范围，还没有发起查询——请先在顶部选择。'

  function approvalReadState(
    noun: string,
    pending: boolean,
    error: unknown,
    total: number,
    emptyHint: string,
  ): ApprovalReadState {
    // 只有「没就绪 **且** 手上确实没有结果」才算未查询；已有行就按实际数据走。
    if (!approval.contextReady.value && total === 0 && error == null) {
      return {
        trustworthy: false,
        tabCount: '—',
        emptyMessage: APPROVAL_AWAITING_MESSAGE,
        error: undefined,
        errorMessage: undefined,
        awaitingScope: true,
      }
    }
    if (error != null) {
      return {
        trustworthy: false,
        tabCount: '取不到',
        emptyMessage: `没有取到${noun}，无法判断当前是否有需要处理的事项。`,
        error,
        errorMessage: `没有取到${noun}，当前无法判断是否有待处理的审批。请重试，或稍后再看。`,
        awaitingScope: false,
      }
    }
    if (pending && total === 0) {
      return {
        trustworthy: false,
        tabCount: '…',
        emptyMessage: `正在读取${noun}…`,
        error: undefined,
        errorMessage: undefined,
        awaitingScope: false,
      }
    }
    return {
      trustworthy: true,
      tabCount: String(total),
      emptyMessage: emptyHint,
      error: undefined,
      errorMessage: undefined,
      awaitingScope: false,
    }
  }

  const tasksState = computed(() =>
    approvalReadState(
      '审批任务',
      approval.tasksPending.value,
      approval.tasksError.value,
      approval.tasksTotal.value,
      '没有待你处理的审批任务。有单据流转到你这一步时会出现在这里。',
    ),
  )
  const chainsState = computed(() =>
    approvalReadState(
      '审批流程',
      approval.chainsPending.value,
      approval.chainsError.value,
      approval.chainsTotal.value,
      '没有正在审批中的单据。发起审批后会在这里跟踪流转。',
    ),
  )
  const decisionsState = computed(() =>
    approvalReadState(
      '审批决策记录',
      approval.decisionsPending.value,
      approval.decisionsError.value,
      approval.decisionsTotal.value,
      '还没有审批决策记录。任何一次通过或驳回都会在这里留痕。',
    ),
  )
  const delegationsState = computed(() =>
    approvalReadState(
      '审批委托',
      approval.delegationsPending.value,
      approval.delegationsError.value,
      approval.delegationsTotal.value,
      '还没有审批委托。休假或出差前可在这里把审批权临时交出去。',
    ),
  )
  const templatesState = computed(() =>
    approvalReadState(
      '审批模板',
      approval.templatesPending.value,
      approval.templatesError.value,
      approval.templatesTotal.value,
      '还没有审批模板。先配置单据类型的审批链路，之后的单据才能自动进入审批。',
    ),
  )

  /** 页头读数：任务这一路取不到就直说，不显 0。 */
  const headerCount = computed(() => {
    if (tasksState.value.awaitingScope) return '—'
    if (!tasksState.value.trustworthy) return '待处理任务数取不到'
    return `${approval.tasksTotal.value} 个待处理任务`
  })

  applyRouteApprovalFilters()

  /** 审批人类型码值 → 中文；UI 不直出 role/user/department 之类的原文。 */
  const APPROVER_TYPE_LABELS: Record<string, string> = {
    role: '角色',
    user: '人员',
    department: '部门',
    team: '班组',
    position: '岗位',
  }
  function approverLabel(approverType?: string | null, approverRef?: string | null) {
    const type = (approverType ?? '').trim().toLowerCase()
    const label = type ? (APPROVER_TYPE_LABELS[type] ?? approverType) : '处理人'
    const ref = (approverRef ?? '').trim()
    return ref ? `${label} ${ref}` : `${label}待指定`
  }

  const decisionContextItems = computed(() => {
    const row = decisionTarget.value
    if (!row) return []
    return [
      { label: '单据', value: documentLabel(row) },
      { label: '单据类型', value: documentTypeLabel(row.documentType, '—') },
      {
        label: '当前步骤',
        value: row.stepName ?? (row.stepNo ? `第 ${row.stepNo} 步` : undefined),
      },
      { label: '到期时间', value: row.dueAtUtc ? formatDateTime(row.dueAtUtc) : undefined },
    ]
  })

  function documentLabel(row: { documentType?: string | null; documentId?: string | null }) {
    const id = row.documentId ?? ''
    const type = documentTypeLabel(row.documentType, '业务单据')
    return id ? `${type} · ${id}` : type
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

  function openTaskDecision(row: BusinessConsoleApprovalTaskItem, decision: ApprovalDecisionValue) {
    if (!canManageApprovals.value || !row.chainId || row.stepNo === undefined) return
    decisionTarget.value = row
    decisionForm.chainId = row.chainId
    decisionForm.stepNo = row.stepNo
    decisionForm.decision = decision
    decisionForm.comment = ''
    taskDecisionOpen.value = true
  }

  async function quickResolveTask(
    row: BusinessConsoleApprovalTaskItem,
    decision: ApprovalDecisionValue,
  ) {
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
      notifyOperationFailure('审批处理失败', error, '审批处理失败，请稍后重试。')
    }
  }

  async function submitTaskDecision() {
    if (!decisionForm.chainId || decisionForm.stepNo <= 0) return
    try {
      await approval.resolveTask({ ...decisionForm })
      taskDecisionOpen.value = false
      notifySuccess(`审批任务已${decisionLabel(decisionForm.decision)}`)
    } catch (error) {
      notifyOperationFailure('审批处理失败', error, '审批处理失败，请稍后重试。')
    }
  }

  function decisionLabel(decision: ApprovalDecisionValue) {
    return APPROVAL_DECISION_ACTION_LABELS[decision]
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
      notifyOperationFailure('委托保存失败', error, '委托保存失败，请稍后重试。')
    }
  }

  async function revokeDelegation(row: BusinessConsoleApprovalDelegationItem) {
    if (!row.delegationId || !canManageApprovals.value) return
    try {
      await approval.revokeDelegation(row.delegationId)
      notifySuccess('审批委托已撤销')
    } catch (error) {
      notifyOperationFailure('委托撤销失败', error, '委托撤销失败，请稍后重试。')
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
      notifyOperationFailure('模板保存失败', error, '模板保存失败，请稍后重试。')
    }
  }

  function toIsoFromLocalInput(value: string) {
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toISOString()
  }

  return {
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
  }
}
