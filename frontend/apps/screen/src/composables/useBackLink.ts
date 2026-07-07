import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { SCREENS, screenForPath } from '@/data/screens'

/**
 * 页脚返回链按来路识别（跳转闭环）：从哪块屏进来就回哪块屏 ——
 * 门厅进 → 「返回大屏门厅」；工厂总览下钻 → 「返回工厂总览」；
 * 车间下钻产线 → 回原车间。直接输入 URL（无来路）用调用方 fallback。
 * history.state.back 由 vue-router 维护，本身非响应式 —— 挂 route.fullPath 触发重读。
 */
export function useBackLink(fallback: () => { to: string; label: string }) {
  const route = useRoute()
  return computed(() => {
    void route.fullPath
    const back = typeof history.state?.back === 'string' ? history.state.back : ''
    if (back === '/') return { to: '/', label: '返回大屏门厅' }
    const key = screenForPath(back)
    if (key) {
      const s = SCREENS.find((x) => x.key === key)
      if (s) return { to: back, label: `返回${s.title}` }
    }
    return fallback()
  })
}
