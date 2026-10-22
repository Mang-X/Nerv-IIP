import {
  addBusinessConsoleWmsWorkPoolMemberMutationOptions,
  assignBusinessConsoleWmsCountExecutionMutationOptions,
  assignBusinessConsoleWmsInboundOrderMutationOptions,
  assignBusinessConsoleWmsOutboundOrderMutationOptions,
  createBusinessConsoleWmsWorkPoolMutationOptions,
  listBusinessConsoleWmsWorkPoolsQueryOptions,
  removeBusinessConsoleWmsWorkPoolMemberMutationOptions,
  type BusinessConsoleWmsWorkPool,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from './businessContextBinding'
import { createWmsIdempotencyKey } from './useBusinessWms'

/**
 * 仓库作业池（#3849）：仓库主管维护作业池与成员，分配入库 / 出库 / 盘点单时从这里取池。
 * 作业池编码由系统按编码规则生成，界面只填名称与工厂。
 */
export function useWmsWorkPools() {
  const context = bindBusinessContext(reactive({ organizationId: '', environmentId: '' }))
  const businessQuery = () => ({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
  })

  const poolsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleWmsWorkPoolsQueryOptions({ query: businessQuery() }),
      context,
    ),
  )
  const refresh = () => refetchWithBusinessContext(context, poolsQuery)

  const createMutation = useMutation({
    ...createBusinessConsoleWmsWorkPoolMutationOptions(),
    onSuccess: () => void refresh(),
  })
  const addMemberMutation = useMutation({
    ...addBusinessConsoleWmsWorkPoolMemberMutationOptions(),
    onSuccess: () => void refresh(),
  })
  const removeMemberMutation = useMutation({
    ...removeBusinessConsoleWmsWorkPoolMemberMutationOptions(),
    onSuccess: () => void refresh(),
  })
  const assignInboundMutation = useMutation(assignBusinessConsoleWmsInboundOrderMutationOptions())
  const assignOutboundMutation = useMutation(assignBusinessConsoleWmsOutboundOrderMutationOptions())
  const assignCountMutation = useMutation(assignBusinessConsoleWmsCountExecutionMutationOptions())

  const pools = computed<BusinessConsoleWmsWorkPool[]>(() => {
    const envelope = poolsQuery.data.value
    return envelope?.success ? (envelope.data?.items ?? []) : []
  })

  type AssignTarget = 'inbound' | 'outbound' | 'count'
  function assign(
    target: AssignTarget,
    resourceId: string,
    body: {
      poolCode: string
      operatorPrincipalId?: string
      idempotencyKey: string
      expectedVersion: number
    },
  ) {
    const query = businessQuery()
    if (target === 'inbound') {
      return assignInboundMutation.mutateAsync({
        path: { inboundOrderId: resourceId },
        query,
        body,
      })
    }
    if (target === 'outbound') {
      return assignOutboundMutation.mutateAsync({
        path: { outboundOrderId: resourceId },
        query,
        body,
      })
    }
    return assignCountMutation.mutateAsync({
      path: { countExecutionId: resourceId },
      query,
      body,
    })
  }

  return {
    pools,
    poolsPending: poolsQuery.isLoading,
    poolsError: poolsQuery.error,
    refresh,
    createPool: (body: { displayName: string; siteCode: string; idempotencyKey: string }) =>
      createMutation.mutateAsync({ query: businessQuery(), body }),
    createPoolPending: createMutation.isLoading,
    addMember: (poolCode: string, principalId: string) =>
      addMemberMutation.mutateAsync({
        path: { poolCode },
        query: businessQuery(),
        body: { principalId },
      }),
    addMemberPending: addMemberMutation.isLoading,
    removeMember: (poolCode: string, principalId: string) =>
      removeMemberMutation.mutateAsync({
        path: { poolCode, principalId },
        query: businessQuery(),
      }),
    removeMemberPending: removeMemberMutation.isLoading,
    assign,
    assignPending: computed(
      () =>
        assignInboundMutation.isLoading.value ||
        assignOutboundMutation.isLoading.value ||
        assignCountMutation.isLoading.value,
    ),
    newIdempotencyKey: createWmsIdempotencyKey,
  }
}
