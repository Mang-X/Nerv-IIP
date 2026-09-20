<script setup lang="ts">
import { computed } from 'vue'
import { IS_REAL_DATA } from '@/data/config'
import { useRealAuthStore } from '@/stores/realAuth'
import AndonBoard from './AndonBoard.vue'

const auth = IS_REAL_DATA ? useRealAuthStore() : undefined
const session = computed(() => {
  const principal = auth?.principal
  return principal?.organizationId && principal.environmentId
    ? { organizationId: principal.organizationId, environmentId: principal.environmentId }
    : undefined
})
const sessionKey = computed(() => JSON.stringify([auth?.sessionId, auth?.principal]))
</script>

<template>
  <AndonBoard v-if="session" :key="sessionKey" :session="session" />
  <p v-else class="andon-unavailable" role="status">
    {{ IS_REAL_DATA ? '未授权 · 请登录具有真实组织环境的账号' : '当前暂无安灯数据' }}
  </p>
</template>

<style scoped>
@layer app {
  .andon-unavailable {
    padding: 32px;
    color: var(--nv-scr-muted);
    font-size: 26px;
  }
}
</style>
