import { describe, expect, it, vi } from 'vitest'
import { nextTick, reactive } from 'vue'

import { useIncludeDisabledFilter } from './masterDataIncludeDisabled'

describe('「包含停用」开关', () => {
  it('默认关闭，且不预先写脏过滤器', () => {
    const filters = reactive<{ includeDisabled?: boolean }>({})
    const includeDisabled = useIncludeDisabledFilter([filters])

    expect(includeDisabled.value).toBe(false)
    // 未切换前不写值：避免把 `includeDisabled=false` 当成显式查询参数发出去。
    expect(filters.includeDisabled).toBeUndefined()
  })

  it('切换后同步到该页的每一张列表过滤器', async () => {
    const uoms = reactive<{ includeDisabled?: boolean }>({})
    const conversions = reactive<{ includeDisabled?: boolean }>({})
    const includeDisabled = useIncludeDisabledFilter([uoms, conversions])

    includeDisabled.value = true
    await nextTick()
    expect(uoms.includeDisabled).toBe(true)
    expect(conversions.includeDisabled).toBe(true)

    includeDisabled.value = false
    await nextTick()
    expect(uoms.includeDisabled).toBe(false)
    expect(conversions.includeDisabled).toBe(false)
  })

  it('切换时回调一次，供页面把分页重置回第 1 页', async () => {
    const filters = reactive<{ includeDisabled?: boolean }>({})
    const onChange = vi.fn()
    const includeDisabled = useIncludeDisabledFilter([filters], onChange)

    includeDisabled.value = true
    await nextTick()

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith(true)
  })
})
