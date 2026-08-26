import { describe, expect, it } from 'vitest'

import {
  createServerPaginationState,
  reduceServerPagination,
  serverPaginationIdentity,
} from './serverPaginationController'

describe('server pagination controller', () => {
  it('总量收缩时校正到末个有效页并等待唯一目标请求结算', () => {
    let state = createServerPaginationState(200, 'org-a:env-a')
    state = reduceServerPagination(state, { type: 'navigate', targetPage: 5, pageCount: 5 })

    state = reduceServerPagination(state, {
      type: 'response-succeeded',
      identity: serverPaginationIdentity('org-a:env-a', 5),
      responsePage: 5,
      total: 401,
    })

    expect(state).toMatchObject({
      page: 3,
      navigationPending: true,
      correctionIdentity: 'org-a:env-a:page:3',
      lastSuccessfulTotal: 401,
    })

    state = reduceServerPagination(state, {
      type: 'response-succeeded',
      identity: serverPaginationIdentity('org-a:env-a', 3),
      responsePage: 3,
      total: 401,
    })

    expect(state).toMatchObject({ page: 3, navigationPending: false, correctionIdentity: '' })
  })

  it('校正期间切换 scope 会清空旧身份且忽略迟到结算', () => {
    let state = createServerPaginationState(200, 'org-a:env-a')
    state = { ...state, page: 2, navigationPending: true }
    state = reduceServerPagination(state, {
      type: 'response-succeeded',
      identity: serverPaginationIdentity('org-a:env-a', 2),
      responsePage: 2,
      total: 200,
    })

    state = reduceServerPagination(state, { type: 'scope-changed', scopeIdentity: 'org-b:env-b' })
    expect(state).toMatchObject({
      scopeIdentity: 'org-b:env-b',
      page: 1,
      navigationPending: false,
      correctionIdentity: '',
      lastSuccessfulTotal: 0,
    })

    const afterLateOldScope = reduceServerPagination(state, {
      type: 'response-failed',
      identity: serverPaginationIdentity('org-a:env-a', 1),
    })
    expect(afterLateOldScope).toEqual(state)
  })

  it('校正目标请求失败时解除 pending 并保留上次成功总量用于返回', () => {
    let state = createServerPaginationState(200, 'org-a:env-a')
    state = { ...state, page: 2, navigationPending: true, lastSuccessfulTotal: 401 }
    state = reduceServerPagination(state, {
      type: 'response-succeeded',
      identity: serverPaginationIdentity('org-a:env-a', 2),
      responsePage: 2,
      total: 200,
    })

    state = reduceServerPagination(state, {
      type: 'response-failed',
      identity: serverPaginationIdentity('org-a:env-a', 1),
    })

    expect(state).toMatchObject({
      page: 1,
      navigationPending: false,
      correctionIdentity: '',
      lastSuccessfulTotal: 200,
    })
  })
})
