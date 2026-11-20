<script setup lang="ts">
import { Toaster } from '@nerv-iip/ui'
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import UnavailableScreen from '@/components/UnavailableScreen.vue'
import { IS_REAL_DATA } from '@/data/config'
import { SCREENS, screenForPath } from '@/data/screens'
import { resetDeviceRosterCache, setScreenSession } from '@/data/session'
import { useRealAuthStore } from '@/stores/realAuth'

const route = useRoute()
const auth = IS_REAL_DATA ? useRealAuthStore() : undefined
const unavailable = computed(() => {
  const key = screenForPath(route.path)
  if (!IS_REAL_DATA || !key || key === 'equipment' || key === 'warehouse') return
  return SCREENS.find((screen) => screen.key === key)
})
const sessionKey = computed(() => {
  const principal = auth?.principal
  return JSON.stringify([
    auth?.sessionId,
    principal?.principalId,
    principal?.principalType,
    principal?.organizationId,
    principal?.environmentId,
    principal?.permissionVersion,
    [...(principal?.permissionCodes ?? [])].sort(),
    [...(principal?.roleIds ?? [])].sort(),
  ])
})
if (auth) {
  watch(
    sessionKey,
    () => {
      resetDeviceRosterCache()
      setScreenSession({
        organizationId: auth.principal?.organizationId ?? '',
        environmentId: auth.principal?.environmentId ?? '',
      })
    },
    { immediate: true, flush: 'sync' },
  )
}
</script>

<template>
  <template v-if="!auth || auth.principal || route.path === '/login'">
    <UnavailableScreen v-if="unavailable" :key="sessionKey" :screen="unavailable" />
    <RouterView v-else :key="sessionKey" />
  </template>
  <Toaster />
</template>
