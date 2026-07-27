import { watchDebounced } from '@vueuse/core'
import { ref } from 'vue'

/**
 * MES 列表页的关键字搜索桥：本地输入 → 去抖 → 写回 facade 的 `keyword` 过滤器。
 *
 * MES 各列表 facade 都收 `keyword`，但多数页面此前只挂了一个状态下拉、没有搜索框，
 * 现场要在几百行里靠翻页找一张单。这里统一口径，免得每页各写一遍去抖与 trim：
 * 空串一律写回 `undefined`（别把空字符串发给后端当过滤条件）。
 */
export function useMesKeywordFilter(filters: { keyword?: string }) {
  const keyword = ref('')

  watchDebounced(
    keyword,
    (value) => {
      const trimmed = value.trim()
      filters.keyword = trimmed ? trimmed : undefined
    },
    { debounce: 300, maxWait: 1000 },
  )

  function resetKeyword() {
    keyword.value = ''
    filters.keyword = undefined
  }

  return { keyword, resetKeyword }
}
