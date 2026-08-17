import { ref, watch } from 'vue'

/** 只要求带 `includeDisabled` 的过滤器形状——各页 filters 类型不同，这里只关心这一个字段。 */
export interface IncludeDisabledFilters {
  includeDisabled?: boolean
}

/**
 * 「包含停用」开关（#1594）。
 *
 * 服务端 `IncludeDisabled` 默认 false，停用后行立刻从列表消失；而 `MasterDataRowActions` 的
 * 「启用」只在 `row.active === false` 时出现——行都没了，启用入口就永远触达不到，
 * 软删除退化成单向操作。
 *
 * 一页往往有多张列表（工厂结构 4 张、组织 3 张、计量单位 2 张…），所以开关按**页**收在这里、
 * 一次同步到该页所有过滤器，避免每页各写一遍 watch 又各漏一张表。
 *
 * @param targets 该页全部需要跟随的过滤器对象（响应式）。
 * @param onChange 切换后的副作用，通常是把分页重置到第 1 页。
 */
export function useIncludeDisabledFilter(
  targets: IncludeDisabledFilters[],
  onChange?: (includeDisabled: boolean) => void,
) {
  const includeDisabled = ref(false)

  watch(includeDisabled, (value) => {
    for (const filters of targets) {
      filters.includeDisabled = value
    }
    onChange?.(value)
  })

  return includeDisabled
}
