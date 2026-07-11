// 大屏真实会话 store（MAN-466）：仅在 real 模式启用（main.ts 按 IS_REAL_DATA 实例化）。
// mock 模式仍用 stores/auth.ts 的演示登录，二者互不影响。
import { consoleAuthApi } from '@/api/auth'
import { createAuthStore } from '@nerv-iip/auth'

export const useRealAuthStore = createAuthStore({
  api: consoleAuthApi,
  messages: {
    invalidSession: '认证服务返回了无效会话。',
    loginFailed: '无法登录。',
    unknownUser: '未知用户',
  },
  storageKey: 'nerv-iip.screen.auth',
  storeId: 'screen-real-auth',
})
