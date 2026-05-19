import type { Router } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import { handleUnauthorized } from './unauthorized'

function createRouterStub(path: string, fullPath = path) {
  return {
    currentRoute: {
      value: {
        fullPath,
        path,
      },
    },
    push: vi.fn(),
  } as unknown as Router
}

describe('unauthorized api handling', () => {
  it('clears session and redirects to login with the current route', () => {
    const auth = {
      clearSession: vi.fn(),
    }
    const router = createRouterStub('/operations/task-001')

    handleUnauthorized(auth, router)

    expect(auth.clearSession).toHaveBeenCalledWith('api-unauthorized')
    expect(router.push).toHaveBeenCalledWith({
      path: '/login',
      query: { redirect: '/operations/task-001' },
    })
  })

  it('does not rewrite an existing login redirect', () => {
    const auth = {
      clearSession: vi.fn(),
    }
    const router = createRouterStub('/login', '/login?redirect=/')

    handleUnauthorized(auth, router)

    expect(auth.clearSession).toHaveBeenCalledWith('api-unauthorized')
    expect(router.push).not.toHaveBeenCalled()
  })
})
