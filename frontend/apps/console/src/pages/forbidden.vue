<script setup lang="ts">
import { firstConsolePage } from '@/router/access'
import { useAuthStore } from '@/stores/auth'
import { Button, NvPageHeader } from '@nerv-iip/ui'
import { computed } from 'vue'
import { RouterLink, useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '访问受限' } })

const auth = useAuthStore()
const router = useRouter()
const firstPage = computed(() => firstConsolePage(auth.principal?.permissionCodes))

async function signOut() {
  await auth.logout()
  await router.replace('/login')
}
</script>

<template>
  <main class="mx-auto grid min-h-svh max-w-lg content-center gap-4 p-6">
    <NvPageHeader title="访问受限" />
    <p class="text-sm text-muted-foreground">当前账号缺少访问此页面所需的权限。</p>
    <div class="flex gap-2">
      <Button v-if="firstPage !== '/forbidden'" as-child>
        <RouterLink :to="firstPage">前往可用页面</RouterLink>
      </Button>
      <Button type="button" variant="outline" @click="signOut">退出登录</Button>
    </div>
  </main>
</template>
