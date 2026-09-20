import { describe, expect, it } from 'vitest'
import { failureDetail } from '../e2e/issue1912-walkthrough-evidence'

describe('walkthrough retained failure privacy', () => {
  it.each([
    'customer email alice@example.com',
    'phone 13800138000; customer 张三',
    'Bearer fixture-access-value password=fixture-secret',
    'unrecognized free-form response body',
  ])('retains only a closed digest for %s', (message) => {
    const detail = failureDetail(message)
    expect(detail).toMatch(/^failure-sha256:[a-f0-9]{64}$/)
    expect(detail).not.toContain(message)
    expect(failureDetail(message)).toBe(detail)
  })
})
