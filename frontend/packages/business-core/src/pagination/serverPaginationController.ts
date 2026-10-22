import { lastPageForTotal } from './pageBounds'

export interface ServerPaginationState {
  lastSuccessfulTotal: number
  navigationPending: boolean
  page: number
  pageSize: number
  scopeIdentity: string
}

export type ServerPaginationEvent =
  | { type: 'scope-changed'; scopeIdentity: string }
  | { type: 'navigate'; targetPage: number; pageCount: number }
  | {
      type: 'response-succeeded'
      identity: string
      responsePage: number
      total: number
    }
  | { type: 'response-failed'; identity: string }

export function serverPaginationIdentity(scopeIdentity: string, page: number): string {
  return scopeIdentity ? `${scopeIdentity}:page:${page}` : ''
}

export function createServerPaginationState(
  pageSize: number,
  scopeIdentity = '',
): ServerPaginationState {
  return {
    lastSuccessfulTotal: 0,
    navigationPending: false,
    page: 1,
    pageSize,
    scopeIdentity,
  }
}

export function reduceServerPagination(
  state: ServerPaginationState,
  event: ServerPaginationEvent,
): ServerPaginationState {
  if (event.type === 'scope-changed') {
    if (event.scopeIdentity === state.scopeIdentity) return state
    return createServerPaginationState(state.pageSize, event.scopeIdentity)
  }

  if (event.type === 'navigate') {
    if (
      state.navigationPending ||
      !Number.isInteger(event.targetPage) ||
      event.targetPage < 1 ||
      event.targetPage > event.pageCount ||
      event.targetPage === state.page
    ) {
      return state
    }
    return { ...state, page: event.targetPage, navigationPending: true }
  }

  const currentIdentity = serverPaginationIdentity(state.scopeIdentity, state.page)
  if (!currentIdentity || event.identity !== currentIdentity) return state

  if (event.type === 'response-failed' || event.responsePage !== state.page) {
    return { ...state, navigationPending: false }
  }

  const lastSuccessfulTotal = Number.isFinite(event.total) ? Math.max(0, event.total) : 0
  const lastPage = lastPageForTotal(lastSuccessfulTotal, state.pageSize)
  if (state.page > lastPage) {
    return {
      ...state,
      lastSuccessfulTotal,
      navigationPending: true,
      page: lastPage,
    }
  }

  return {
    ...state,
    lastSuccessfulTotal,
    navigationPending: false,
  }
}
