import type {
  BusinessConsoleAuthorizedWorkScope,
  BusinessConsoleMesAndonCallResponse,
  GetBusinessConsolePrincipalWorkContextData,
} from '@nerv-iip/api-client'

export type AndonSession = Pick<
  GetBusinessConsolePrincipalWorkContextData['query'],
  'organizationId' | 'environmentId'
>
export type AndonScope = Required<
  Pick<BusinessConsoleAuthorizedWorkScope, 'kind' | 'id' | 'displayName'>
>
export interface AndonQueue {
  items: BusinessConsoleMesAndonCallResponse[]
  total: number
}
export const ANDON_PAGE_SIZE = 5

export function andonErrorMessage(error: unknown): string {
  return error instanceof TypeError
    ? '失联 · 无法连接安灯数据源'
    : error instanceof Error
      ? error.message
      : '读取失败 · 安灯数据不可用'
}
