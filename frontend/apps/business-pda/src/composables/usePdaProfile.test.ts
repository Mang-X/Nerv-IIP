import { describe, expect, it } from 'vitest'

import { aggregatePdaProfileContexts, clearPdaApplicationStorage } from './usePdaProfile'

describe('aggregatePdaProfileContexts', () => {
  it('deduplicates readable roles and authorized scopes across permission-aware contexts', () => {
    const profile = aggregatePdaProfileContexts([
      {
        principal: {
          roles: [
            { id: 'role-operator', displayName: 'PDA 操作员' },
            { id: 'role-shared', displayName: '现场人员' },
          ],
        },
        worker: { employeeNo: 'EMP-010', name: '王建国', jobTitle: '操作工' },
        teams: [{ id: 'team-a', name: '机加早班' }],
        authorizedScopes: [{ kind: 'team', id: 'team-a', displayName: '机加早班' }],
      },
      {
        principal: {
          roles: [
            { id: 'role-shared', displayName: '现场人员' },
            { id: 'role-quality', displayName: '质检员' },
          ],
        },
        worker: { employeeNo: 'EMP-010', name: '王建国', jobTitle: '操作工' },
        teams: [{ id: 'team-a', name: '机加早班' }],
        authorizedScopes: [
          { kind: 'team', id: 'team-a', displayName: '机加早班' },
          { kind: 'work-center', id: 'wc-1', displayName: '数控一组' },
        ],
      },
    ])

    expect(profile.roleNames).toEqual(['PDA 操作员', '现场人员', '质检员'])
    expect(profile.scopeLabels).toEqual(['班组 · 机加早班', '工作中心 · 数控一组'])
    expect(profile.teamNames).toEqual(['机加早班'])
    expect(profile.employeeNo).toBe('EMP-010')
  })
})

describe('clearPdaApplicationStorage', () => {
  it('removes only PDA-owned persisted state and clears session storage', () => {
    localStorage.setItem('nerv-iip.business-pda.auth', 'session')
    localStorage.setItem('nerv-iip.business-pda.filter', 'cached')
    localStorage.setItem('nerv-iip.console.auth', 'keep')
    sessionStorage.setItem('pending-write', 'cached')

    clearPdaApplicationStorage()

    expect(localStorage.getItem('nerv-iip.business-pda.auth')).toBeNull()
    expect(localStorage.getItem('nerv-iip.business-pda.filter')).toBeNull()
    expect(localStorage.getItem('nerv-iip.console.auth')).toBe('keep')
    expect(sessionStorage.length).toBe(0)
  })
})
