import {
  addBusinessConsoleTeamMemberMutationOptions,
  assignBusinessConsolePersonnelSkillMutationOptions,
  createBusinessConsoleWorkerMutationOptions,
  listBusinessConsolePersonnelSkillMatrixQueryOptions,
  listBusinessConsoleTeamMembersQueryOptions,
  listBusinessConsoleWorkers,
  listBusinessConsoleWorkersQueryOptions,
  removeBusinessConsoleTeamMemberMutationOptions,
  type BusinessConsoleCreateWorkerRequest,
  type BusinessConsolePersonnelSkillMatrixEnvelope,
  type BusinessConsolePersonnelSkillMatrixRow,
  type BusinessConsoleTeamMemberItem,
  type BusinessConsoleTeamMemberListEnvelope,
  type BusinessConsoleUpdateMasterDataResourceRequest,
  type BusinessConsoleWorkerDirectoryEnvelope,
  type BusinessConsoleWorkerDirectoryItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseMutationOptions } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../shared/businessContextBinding'
import {
  DEFAULT_TAKE,
  defaultContext,
  fetchCompleteCatalog,
  ignoreBackgroundError,
  isBusinessQuery,
  optionalQuery,
  type BusinessContextFilters,
} from './masterDataShared'
import {
  type MasterDataLifecyclePatch,
  useMasterDataResourceActions,
} from './useMasterDataResources'

/**
 * 人员目录单页上限，由网关校验器定死：
 * `BusinessConsoleMasterDataEndpoints.cs` 的 `RuleFor(x => x.PageSize).InclusiveBetween(1, 200)`。
 *
 * 超了就是 400，而调用方大多把人员目录当查表用（userId → 姓名），失败时整列回落成占位符——
 * **界面看着「没数据」，其实是请求被拒**。第五轮走查在待检工作台实际踩到（`pageSize: 500`，
 * 整列「当前持有人」变 `—`，连「已被他人认领」都看不出来）。
 *
 * 所以这里既导出常量给调用方引用，也在 composable 内部夹紧——下一个调用方就算随手写个大数
 * 也不会把页面打成静默空态。
 */
export const WORKER_DIRECTORY_MAX_PAGE_SIZE = 200

export interface WorkerDirectoryFilters extends BusinessContextFilters {
  keyword?: string
  /** 精确匹配单个工人标识，用于回填已选人员。 */
  userId?: string
  departmentCode?: string
  /** 按班组过滤候选人。 */
  teamCode?: string
  /** 按车间过滤候选人——班组是车间级的。 */
  workshopCode?: string
  /** 按工作中心过滤候选人——经该工作中心所属车间解析到班组成员。 */
  workCenterCode?: string
  /** 按技能过滤候选人，只保留当前有效的技能记录。 */
  skillCode?: string
  /** 在岗状态：active / on-leave / resigned。 */
  employmentStatus?: string
  includeDisabled?: boolean
  /**
   * 员工名册页要求带出关联账号的登录名（#3924）。只有员工维护权 + 组织级授权的主体才真的拿得到，
   * 是否拿到看返回的 `loginNamesVisible`；各处员工选择器不要开，免得每次多一跳 IAM。
   */
  includeLoginNames?: boolean
  pageIndex: number
  pageSize: number
}

function workerItems(envelope: BusinessConsoleWorkerDirectoryEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function workerTotal(envelope: BusinessConsoleWorkerDirectoryEnvelope | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.totalCount ?? 0
}

/**
 * 员工目录（人员选择器与员工维护页的共同数据源）。读自 MasterData 员工主数据
 * `/master-data/workers`（注意分页用 pageIndex/pageSize，非 skip/take）。除关键词外还支持
 * 部门 / 班组 / 工作中心 / 技能 / 在岗状态过滤——派工候选就是靠 workCenterCode 收敛的。
 * UI 只呈现姓名 / 工号 / 部门 / 班组 / 技能，userId 仅作为绑定值。
 */
export function useBusinessWorkers(initial: Partial<WorkerDirectoryFilters> = {}, all = false) {
  const filters = bindBusinessContext(
    reactive<WorkerDirectoryFilters>({
      organizationId: '',
      environmentId: '',
      keyword: undefined,
      // 网关 BusinessConsoleWorkerDirectoryRequestValidator 校验 PageIndex > 0（1-based，
      // 默认 PageIndex=1，与 useBusinessScheduling 等一致）。发 0 会被后端拒为 400，人员选择器静默空。
      pageIndex: 1,
      ...initial,
      // 夹紧到网关上限：调用方传超了就是 400，而这里失败通常表现为「整列空」而不是报错，
      // 极难从界面看出来（见 WORKER_DIRECTORY_MAX_PAGE_SIZE 注释里的实测）。
      pageSize: Math.min(
        Math.max(1, initial.pageSize ?? DEFAULT_TAKE),
        WORKER_DIRECTORY_MAX_PAGE_SIZE,
      ),
    }),
  )

  const workersQuery = useQuery(() => {
    const query = {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      ...optionalQuery('keyword', filters.keyword),
      ...optionalQuery('userId', filters.userId),
      ...optionalQuery('departmentCode', filters.departmentCode),
      ...optionalQuery('teamCode', filters.teamCode),
      ...optionalQuery('workshopCode', filters.workshopCode),
      ...optionalQuery('workCenterCode', filters.workCenterCode),
      ...optionalQuery('skillCode', filters.skillCode),
      ...optionalQuery('employmentStatus', filters.employmentStatus),
      ...optionalQuery('includeDisabled', filters.includeDisabled),
      ...optionalQuery('includeLoginNames', filters.includeLoginNames),
      pageIndex: filters.pageIndex,
      pageSize: filters.pageSize,
    }
    const options = listBusinessConsoleWorkersQueryOptions({ query })
    return withBusinessContextEnabled(
      all
        ? {
            ...options,
            key: [...options.key, 'all'],
            query: ({ signal }): Promise<BusinessConsoleWorkerDirectoryEnvelope> =>
              fetchCompleteCatalog<BusinessConsoleWorkerDirectoryItem>(async (skip) => {
                const { data } = await listBusinessConsoleWorkers({
                  query: { ...query, pageIndex: Math.floor(skip / query.pageSize) + 1 },
                  signal,
                  throwOnError: true,
                })
                return data
              }),
          }
        : options,
      filters,
    )
  })

  return {
    filters,
    refresh: () => refetchWithBusinessContext(filters, workersQuery),
    workers: computed<BusinessConsoleWorkerDirectoryItem[]>(() =>
      workerItems(workersQuery.data.value),
    ),
    workersError: workersQuery.error,
    workersPending: workersQuery.isLoading,
    workersTotal: computed(() => workerTotal(workersQuery.data.value)),
    /** 本次名册的 `loginName` 是否可信：为假时空值不代表「未关联」，只是当前主体看不到。 */
    loginNamesVisible: computed(() => workersQuery.data.value?.data?.loginNamesVisible === true),
  }
}

/**
 * 员工维护页的读写面：目录列表 + 新建 + 编辑/停用/启用（后两者走通用资源端点
 * `master-data/resources/worker/{code}`，code 即工号）。
 */
export function useWorkerRegistry(initial: Partial<WorkerDirectoryFilters> = {}) {
  const directory = useBusinessWorkers({
    includeDisabled: true,
    includeLoginNames: true,
    pageSize: 50,
    ...initial,
  })
  const actions = useMasterDataResourceActions('worker')
  const queryCache = useQueryCache()

  function invalidate() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleWorkers') })
      .catch(ignoreBackgroundError)
  }

  const createMutation = useMutation({
    ...createBusinessConsoleWorkerMutationOptions(),
    onSuccess: invalidate,
  } as unknown as UseMutationOptions)

  async function runAndRefresh<T>(action: Promise<T>) {
    const result = await action
    invalidate()
    return result
  }

  return {
    ...directory,
    ...actions,
    create: (body: BusinessConsoleCreateWorkerRequest) =>
      runAndRefresh(
        (
          createMutation.mutateAsync as unknown as (vars: {
            body: BusinessConsoleCreateWorkerRequest
          }) => Promise<unknown>
        )({ body }),
      ),
    createPending: createMutation.isLoading,
    createError: createMutation.error,
    update: (code: string, patch: Partial<BusinessConsoleUpdateMasterDataResourceRequest>) =>
      runAndRefresh(actions.update(code, patch)),
    // 生命周期原因必须透传：后端对空原因稳定拒绝，且原因是审计事实的一部分（#878）。
    disable: (code: string, patch: MasterDataLifecyclePatch) =>
      runAndRefresh(actions.disable(code, patch)),
    enable: (code: string, patch: MasterDataLifecyclePatch) =>
      runAndRefresh(actions.enable(code, patch)),
  }
}

function teamMemberItems(envelope: BusinessConsoleTeamMemberListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.members ?? []
}

export interface TeamMemberAddInput {
  userId: string
  isLeader?: boolean
  effectiveFrom?: string
}

/**
 * 某班组的成员维护：按 teamCode 列成员 + 添加成员 + 移除成员。teamCode 以 getter/ref 传入
 * 以便随选中行切换；增删成功后互相失效，列表即时刷新。移除走 DELETE，必须带用户填写的真实原因。
 */
export function useTeamMembers(teamCode: MaybeRefOrGetter<string | undefined>) {
  const ctx = defaultContext()
  const queryCache = useQueryCache()

  function invalidate() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleTeamMembers') })
      .catch(ignoreBackgroundError)
  }

  const membersQuery = useQuery(() => {
    const code = toValue(teamCode)
    return {
      ...listBusinessConsoleTeamMembersQueryOptions({
        path: { teamCode: code ?? '' },
        query: { organizationId: ctx.organizationId, environmentId: ctx.environmentId },
      }),
      enabled: Boolean(code) && hasBusinessContext(ctx),
    }
  })

  const addMutation = useMutation({
    ...addBusinessConsoleTeamMemberMutationOptions(),
    onSuccess: invalidate,
  } as unknown as UseMutationOptions)
  const removeMutationOptions = removeBusinessConsoleTeamMemberMutationOptions()
  type RemoveTeamMemberMutationVariables = Parameters<typeof removeMutationOptions.mutation>[0]
  const removeMutation = useMutation({
    ...removeMutationOptions,
    onSuccess: invalidate,
  })

  return {
    members: computed<BusinessConsoleTeamMemberItem[]>(() =>
      teamMemberItems(membersQuery.data.value),
    ),
    membersError: membersQuery.error,
    membersPending: membersQuery.isLoading,
    refresh: () =>
      Boolean(toValue(teamCode)) && hasBusinessContext(ctx)
        ? membersQuery.refetch()
        : Promise.resolve(),
    addMember: (input: TeamMemberAddInput) =>
      (addMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { teamCode: toValue(teamCode) ?? '' },
        body: {
          organizationId: ctx.organizationId,
          environmentId: ctx.environmentId,
          userId: input.userId,
          ...optionalQuery('isLeader', input.isLeader),
          ...optionalQuery('effectiveFrom', input.effectiveFrom),
        },
      }),
    addPending: addMutation.isLoading,
    removeMember: (userId: string, reason: string) => {
      const variables: RemoveTeamMemberMutationVariables = {
        path: { teamCode: toValue(teamCode) ?? '', userId },
        body: {
          organizationId: ctx.organizationId,
          environmentId: ctx.environmentId,
          reason,
        },
      }
      return removeMutation.mutateAsync(variables)
    },
    removePending: removeMutation.isLoading,
    memberError: computed(() => addMutation.error.value ?? removeMutation.error.value),
  }
}

export interface PersonnelSkillAssignInput {
  userId: string
  skillCode: string
  level: string
  effectiveFrom?: string
}

/**
 * 人员技能登记：把某工人的某技能登记为某等级（走 `/master-data/personnel-skills`）。
 * 成功后失效通用 resources 列表（人员技能列表读自 `useBusinessMasterDataResources('personnel-skill')`）。
 */
export function usePersonnelSkillAssignment() {
  const ctx = defaultContext()
  const queryCache = useQueryCache()

  const assignMutation = useMutation({
    ...assignBusinessConsolePersonnelSkillMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleMasterDataResources') })
        .catch(ignoreBackgroundError)
    },
  } as unknown as UseMutationOptions)

  return {
    assign: (input: PersonnelSkillAssignInput) =>
      (assignMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        body: {
          organizationId: ctx.organizationId,
          environmentId: ctx.environmentId,
          userId: input.userId,
          skillCode: input.skillCode,
          level: input.level,
          ...optionalQuery('effectiveFrom', input.effectiveFrom),
        },
      }),
    assignPending: assignMutation.isLoading,
    assignError: assignMutation.error,
  }
}

export interface PersonnelSkillMatrixFilters extends BusinessContextFilters {
  userId?: string
  skillCode?: string
  includeDisabled?: boolean
}

function skillMatrixData(envelope: BusinessConsolePersonnelSkillMatrixEnvelope | undefined) {
  if (!envelope?.success) {
    return { skillCodes: [] as string[], rows: [] as BusinessConsolePersonnelSkillMatrixRow[] }
  }

  return {
    skillCodes: envelope.data?.skillCodes ?? [],
    rows: envelope.data?.rows ?? [],
  }
}

/**
 * 人员技能矩阵（读，#375）。读自 `/master-data/personnel-skills/matrix`，返回列头 skillCodes
 * 与每人一行 rows（每行 userId + skills[skillCode/level/effectiveFrom/effectiveTo]）。
 * 支持服务端 userId / skillCode 过滤。登记技能仍走 `usePersonnelSkillAssignment()`。
 */
export function usePersonnelSkillMatrix() {
  const filters = bindBusinessContext(
    reactive<PersonnelSkillMatrixFilters>({
      organizationId: '',
      environmentId: '',
      userId: undefined,
      skillCode: undefined,
      includeDisabled: undefined,
    }),
  )

  const matrixQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsolePersonnelSkillMatrixQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...optionalQuery('userId', filters.userId),
          ...optionalQuery('skillCode', filters.skillCode),
          ...optionalQuery('includeDisabled', filters.includeDisabled),
        },
      }),
      filters,
    ),
  )

  return {
    filters,
    refresh: () => refetchWithBusinessContext(filters, matrixQuery),
    skillCodes: computed<string[]>(() => skillMatrixData(matrixQuery.data.value).skillCodes),
    rows: computed<BusinessConsolePersonnelSkillMatrixRow[]>(
      () => skillMatrixData(matrixQuery.data.value).rows,
    ),
    matrixError: matrixQuery.error,
    matrixPending: matrixQuery.isLoading,
  }
}
