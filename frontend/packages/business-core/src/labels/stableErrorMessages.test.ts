import { describe, expect, it } from 'vitest'
import { stableErrorMessage } from './stableErrorMessages'

describe('stableErrorMessage', () => {
  it.each([
    [
      'stored-maintenance-work-order-receipt-is-invalid',
      '工单创建回执异常，请刷新后重试；仍失败请联系管理员。',
    ],
    [
      'source-alarm-already-bound-to-a-different-create-intent',
      '该报警已关联其他维护工单，请刷新后核对。',
    ],
    [
      'stored-maintenance-completion-receipt-is-invalid',
      '工单完工回执异常，请刷新后重试；仍失败请联系管理员。',
    ],
    ['idempotency-conflict', '该操作标识已用于其他内容，请刷新后重新发起。'],
    ['lifecycle-conflict', '状态已被其他操作更新'],
  ])('maps the exact stable wire value %s to actionable Chinese', (wireValue, message) => {
    expect(stableErrorMessage(wireValue)).toBe(message)
  })

  it.each([
    'unknown-stable-error',
    '',
    ' lifecycle-conflict',
    'lifecycle-conflict ',
    undefined,
    null,
    409,
    {},
  ])('returns an empty string for an unknown, empty, or non-string value', (value) => {
    expect(stableErrorMessage(value)).toBe('')
  })
})
