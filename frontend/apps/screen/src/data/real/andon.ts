import {
  getBusinessConsolePrincipalWorkContext,
  listBusinessConsoleMesAndonCalls,
} from '@nerv-iip/api-client'
import {
  ANDON_PAGE_SIZE,
  type AndonQueue,
  type AndonScope,
  type AndonSession,
} from '@/data/contracts/andon'

const permissionCode = 'business.mes.operations.read'

function readData<T>(result: {
  response?: Response
  data?: { success?: boolean; data?: T | null }
}): T {
  if (!result.response) throw new TypeError('失联')
  if (result.response.status === 401 || result.response.status === 403)
    throw new Error('未授权 · 无权读取当前安灯范围')
  if (!result.response.ok || result.data?.success !== true || !result.data.data)
    throw new Error('读取失败 · 安灯数据不可用')
  return result.data.data
}

export async function fetchAndonScopes(session: AndonSession): Promise<AndonScope[]> {
  const context = readData(
    await getBusinessConsolePrincipalWorkContext({ query: { ...session, permissionCode } }),
  )
  return (context.authorizedScopes ?? []).flatMap((scope) =>
    scope.kind && scope.id
      ? [{ kind: scope.kind, id: scope.id, displayName: scope.displayName || scope.id }]
      : [],
  )
}

export async function fetchAndonQueue(
  session: AndonSession,
  scope: AndonScope,
  page: number,
): Promise<AndonQueue> {
  const selection = { scopeKind: scope.kind, scopeId: scope.id }
  const context = readData(
    await getBusinessConsolePrincipalWorkContext({
      query: { ...session, permissionCode, ...selection },
    }),
  )
  if (context.selectedScope?.kind !== scope.kind || context.selectedScope?.id !== scope.id)
    throw new Error('未配置真实范围 · 请重新选择已授权范围')
  const queue = readData(
    await listBusinessConsoleMesAndonCalls({
      query: {
        ...session,
        ...selection,
        queue: 'unclosed',
        skip: page * ANDON_PAGE_SIZE,
        take: ANDON_PAGE_SIZE,
      },
    }),
  )
  if (!queue.items || queue.total === undefined) throw new Error('读取失败 · 安灯队列数据不完整')
  return { items: queue.items, total: queue.total }
}
