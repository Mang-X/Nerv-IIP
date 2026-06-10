import { consoleAuthApi } from '@/api/auth'
import { createAuthStore } from '@nerv-iip/auth'

export const useAuthStore = createAuthStore({
  api: consoleAuthApi,
  messages: {
    invalidSession: 'Authentication service returned an invalid session.',
    loginFailed: 'Unable to sign in.',
    unknownUser: 'Unknown user',
  },
  storageKey: 'nerv-iip.console.auth',
  storeId: 'auth',
})
