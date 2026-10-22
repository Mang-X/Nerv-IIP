import { consoleAuthApi } from '@/api/auth'
import { createAuthStore } from '@nerv-iip/auth'

export const useAuthStore = createAuthStore({
  api: consoleAuthApi,
  messages: {
    invalidSession: '登录失败，请重试。',
    loginFailed: '无法登录。',
    unknownUser: '未知用户',
  },
  storageKey: 'nerv-iip.business-pda.auth',
  storeId: 'pda-auth',
})
