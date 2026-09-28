import { describe, expect, it } from 'vitest'
import { toConsoleIamError } from './iam'

describe('toConsoleIamError', () => {
  it('shows the Chinese rejection reason the gateway passed through from IAM', () => {
    const error = toConsoleIamError({
      code: 400,
      message: '平台管理员账号不能停用。',
      success: false,
    })

    expect(error.message).toBe('平台管理员账号不能停用。')
  })

  it('never puts an English message or a technical code on screen', () => {
    expect(toConsoleIamError({ message: 'iam-bad-request', success: false }).message).toBe(
      '操作未完成，请稍后重试。',
    )
    expect(toConsoleIamError(new Error('Role name is required.')).message).toBe(
      '操作未完成，请稍后重试。',
    )
  })
})
