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
    ['idempotency-key-too-long', '操作标识过长，本次未提交；请重新发起，仍失败请联系管理员。'],
    [
      'idempotency-key-invalid-characters',
      '操作标识含不支持的字符，本次未提交；请重新发起，仍失败请联系管理员。',
    ],
    ['request-payload-invalid', '提交的内容有误，请检查后重新提交；仍失败请联系管理员。'],
    ['lifecycle-conflict', '状态已被其他操作更新'],
    // #3155 补登记的四条：此前后端一直在发、本表一直不认，于是裸码上屏。
    ['idempotency-key-mismatch', '本次请求的操作标识前后不一致，请刷新页面后重新发起。'],
    ['forbidden', '没有执行该操作的权限，请联系管理员确认你的作业范围。'],
    ['unprocessable', '当前数据不满足该操作的前置条件，请刷新后核对。'],
    ['ROUTING_SNAPSHOT_MISSING', '工单缺少已发布生产版本的工艺路线快照，请先维护并发布生产版本。'],
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
