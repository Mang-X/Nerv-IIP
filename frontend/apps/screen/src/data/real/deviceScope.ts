import { getBusinessConsolePrincipalWorkContext } from '@nerv-iip/api-client'
import type { ScreenSession } from '../session'
import { NoWorkScope } from './readError'
import { readRows, readScreenPayload } from './response'

// Device assets accept workCenterCode. MasterData emits the work-center code as the scope ID.
export async function deviceOptions(session: ScreenSession) {
  const contexts = await Promise.all(
    ['business.masterdata.resources.read', 'business.iiot.telemetry.read'].map(
      async (permissionCode) => {
        const context = readScreenPayload(
          await getBusinessConsolePrincipalWorkContext({
            throwOnError: true,
            signal: AbortSignal.timeout(12000),
            query: { ...session, permissionCode },
          }),
        )
        const scopes = readRows(context.authorizedScopes)
        return {
          organization: scopes.some(s => s.kind === 'organization' && s.id === session.organizationId),
          centers: scopes.flatMap(s => s.kind === 'work-center' && s.id
            ? [{ kind: 'work-center', id: s.id, label: s.displayName || s.id }] : []),
        }
      },
    ),
  )
  const centers = new Map(contexts.flatMap(ctx => ctx.centers).map(s => [s.id, s]))
  const common = [...centers.values()].filter(s =>
    contexts.every(ctx => ctx.organization || ctx.centers.some(c => c.id === s.id)))
  return contexts.every(ctx => ctx.organization)
    ? [{ kind: 'organization', id: session.organizationId, label: '当前组织' }, ...common]
    : common
}

export async function deviceWorkCenter(session: ScreenSession, requested?: string) {
  const scopes = await deviceOptions(session)
  const scope = requested ? scopes.find(s => s.kind === 'work-center' && s.id === requested) : scopes[0]
  if (!scope) throw new NoWorkScope('设备台账和遥测没有可共同读取的工作中心')
  return scope.kind === 'work-center' ? scope.id : undefined
}
