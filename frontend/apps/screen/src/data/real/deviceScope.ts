import { getBusinessConsolePrincipalWorkContext } from '@nerv-iip/api-client'
import type { ScreenSession } from '../session'
import { NoWorkScope } from './readError'
import { readRows, readScreenPayload } from './response'

// Device assets accept workCenterCode. MasterData emits the work-center code as the scope ID.
export async function deviceWorkCenter(session: ScreenSession): Promise<string | undefined> {
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
        if (
          scopes.some(
            (scope) => scope.kind === 'organization' && scope.id === session.organizationId,
          )
        )
          return undefined
        return scopes.flatMap((scope) =>
          scope.kind === 'work-center' && scope.id ? [scope.id] : [],
        )
      },
    ),
  )
  const restricted = contexts.filter((codes): codes is string[] => codes !== undefined)
  if (!restricted.length) return undefined
  const common = restricted[0].find((code) => restricted.every((codes) => codes.includes(code)))
  if (!common) throw new NoWorkScope('设备台账和遥测没有可共同读取的工作中心')
  return common
}
