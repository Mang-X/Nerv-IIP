import { expect, it } from 'vitest'
import { resolveMode } from './config'

it.each([undefined, '', 'production', 'typo', 'real'])('正常配置 %s 使用真实会话', (value) => {
  expect(resolveMode(value)).toBe('real')
})
it.each([' mock ', 'demo'])('明确指定演示模式 %s', (value) =>
  expect(resolveMode(value)).toBe('mock'),
)
