import { defineStore } from 'pinia'
import { ref } from 'vue'

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
