<script setup lang="ts">
import { Toaster } from '@nerv-iip/ui'
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import UnavailableScreen from '@/components/UnavailableScreen.vue'
import { IS_REAL_DATA } from '@/data/config'
import { SCREENS, screenForPath } from '@/data/screens'
import { useRealAuthStore } from '@/stores/realAuth'

const route = useRoute()
const auth = IS_REAL_DATA ? useRealAuthStore() : undefined
const unavailable = computed(() => {
  const key = screenForPath(route.path)
  if (!IS_REAL_DATA || !key || key === 'equipment' || key === 'warehouse') return
  return SCREENS.find((screen) => screen.key === key)
})
const sessionKey = ref(0)
watch(
  () => [auth?.principal, auth?.sessionId, auth?.accessToken],
  () => {
    sessionKey.value++
  },
  { flush: 'sync' },
)
</script>

<template>
  <UnavailableScreen v-if="unavailable" :key="sessionKey" :screen="unavailable" />
  <RouterView v-else :key="sessionKey" />
  <Toaster />
</template>
