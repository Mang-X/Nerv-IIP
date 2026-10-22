import { describe, expect, it } from 'vitest'

import { isForbiddenRequestError } from './request-error-status'

describe('isForbiddenRequestError', () => {
  // 两条链路各一格：抛出体自带 status，和由 error 拦截器挂上的 response.status。
  // 任删一支，对应那格必红（收拢前的三份副本正是靠这两支同时成立才等价）。
  it('recognizes 403 carried directly on the thrown error', () => {
    expect(isForbiddenRequestError({ status: 403 })).toBe(true)
  })

  it('recognizes 403 carried on the response attached by the transport error interceptor', () => {
    expect(isForbiddenRequestError({ response: { status: 403 } })).toBe(true)
  })

  it('does not mistake any other status for forbidden', () => {
    for (const status of [400, 401, 404, 409, 422, 500, 503]) {
      expect(isForbiddenRequestError({ status })).toBe(false)
      expect(isForbiddenRequestError({ response: { status } })).toBe(false)
    }
  })

  it('is total on non-object errors instead of throwing', () => {
    for (const error of [undefined, null, '403', 403, new Error('403 Forbidden')]) {
      expect(isForbiddenRequestError(error)).toBe(false)
    }
  })
})
