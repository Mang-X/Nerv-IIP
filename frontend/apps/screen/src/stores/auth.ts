import { defineStore } from 'pinia'
import { ref } from 'vue'

/**
 * MVP 本地鉴权（mock）。后端就绪后替换为 @nerv-iip/auth 的 createAuthStore +
 * configureAuthenticatedApiClient（包已在依赖中，接入点见 main.ts 的 TODO）。
 * 当前大屏数据走 mock，先把页面做完。
 */
export const useAuthStore = defineStore('screen-auth', () => {
  const isAuthenticated = ref(false)
  const displayName = ref('')

  function login(account: string) {
    isAuthenticated.value = true
    displayName.value = account || '操作员'
  }

  function logout() {
    isAuthenticated.value = false
    displayName.value = ''
  }

  return { isAuthenticated, displayName, login, logout }
})
