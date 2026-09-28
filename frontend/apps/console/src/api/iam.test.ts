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

  it('shows the Chinese duplicate-name reason and translates password policy rejections', () => {
    expect(toConsoleIamError({ message: '角色名称「质检员」已被使用。' }).message).toBe(
      '角色名称「质检员」已被使用。',
    )
    expect(toConsoleIamError({ message: 'Password must be at least 8 characters.' }).message).toBe(
      '新密码至少需要 8 个字符。',
    )
  })

  it('does not let a mixed English sentence through just because it quotes a Chinese name', () => {
    expect(toConsoleIamError({ message: "Role name '质检员' is already used." }).message).toBe(
      '操作未完成，请稍后重试。',
    )
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
