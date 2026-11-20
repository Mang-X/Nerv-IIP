import { effectScope } from 'vue'
import { flushPromises } from '@vue/test-utils'
import { expect, it } from 'vitest'
import { scopeKeyOf, useScopedReading } from './useScopedReading'

it('切换范围撤下在途旧样本，暂时失败保留时间，撤权后可改选其余范围', async () => {
  const a = { kind: 'site', id: 'a', label: '甲' }
  const b = { kind: 'site', id: 'b', label: '乙' }
  let scopes = [a, b]
  let fail = false
  let finish!: (value: string) => void
  const delayed = new Promise<string>((resolve) => {
    finish = resolve
  })
  const owner = effectScope()
  try {
    const source = owner.run(() =>
      useScopedReading(
        async () => scopes,
        async (scope) => {
          if (fail) throw new Error('503')
          return scope.id === 'a' ? delayed : '乙的任务'
        },
      ),
    )!
    await flushPromises()
    source.select(scopeKeyOf(b))
    await flushPromises()
    const stamp = source.lastUpdated.value
    expect(stamp).toBeDefined()
    finish('甲的任务')
    await flushPromises()
    expect(source.data.value).toBe('乙的任务')
    fail = true
    await source.refresh()
    expect(source.lastUpdated.value).toBe(stamp)
    expect(source.data.value).toBe('乙的任务')
    scopes = [a]
    await source.refresh()
    expect(source.data.value).toBeUndefined()
    expect(source.lastUpdated.value).toBeUndefined()
    fail = false
    source.select(scopeKeyOf(a))
    await flushPromises()
    expect(source.data.value).toBe('甲的任务')
  } finally {
    owner.stop()
  }
})
