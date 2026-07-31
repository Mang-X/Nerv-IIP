<script setup lang="ts">
import {
  Building2,
  CircleUserRound,
  IdCard,
  ShieldCheck,
  UsersRound,
  Wifi,
  WifiOff,
} from '@lucide/vue'
import {
  NvAppShellMobile,
  NvCell,
  NvCellGroup,
  NvMobileAvatar,
  NvMobileButton,
  NvMobileTag,
  NvNavBar,
} from '@nerv-iip/ui-mobile'
import { shallowRef } from 'vue'
import { useRouter } from 'vue-router'

import { usePdaLogout, usePdaProfile } from '@/composables/usePdaProfile'
import { useAuthStore } from '@/stores/auth'

definePage({ meta: { requiresAuth: true, title: '我的' } })

const auth = useAuthStore()
const profile = usePdaProfile()
const { clearCache } = usePdaLogout()
const router = useRouter()
const loggingOut = shallowRef(false)

async function logout() {
  if (loggingOut.value) return
  loggingOut.value = true
  clearCache()
  await auth.logout()
  await router.push('/login')
}
</script>

<template>
  <NvAppShellMobile>
    <template #header><NvNavBar title="我的" /></template>

    <div class="space-y-5 p-4">
      <section class="flex items-center gap-3 rounded-xl border border-border bg-card p-4">
        <NvMobileAvatar :name="profile.displayName.value" size="lg" />
        <div class="min-w-0 flex-1">
          <h1 class="truncate text-lg font-semibold text-foreground">
            {{ profile.displayName.value }}
          </h1>
          <p class="truncate text-sm text-muted-foreground">{{ profile.loginName.value }}</p>
        </div>
        <NvMobileTag :variant="profile.online.value ? 'success' : 'warning'" size="sm">
          {{ profile.online.value ? '在线' : '离线' }}
        </NvMobileTag>
      </section>

      <section>
        <h2 class="mb-2 text-sm font-semibold text-foreground">主体与岗位</h2>
        <NvCellGroup class="overflow-hidden rounded-xl border border-border">
          <NvCell title="当前主体" :value="profile.principalId.value || '未返回'">
            <template #icon><CircleUserRound /></template>
          </NvCell>
          <NvCell title="工号" :value="profile.employeeNo.value || '未关联'">
            <template #icon><IdCard /></template>
          </NvCell>
          <NvCell title="岗位" :value="profile.jobTitle.value || '未配置'">
            <template #icon><Building2 /></template>
          </NvCell>
          <NvCell
            title="班组"
            :value="profile.teamNames.value.length ? profile.teamNames.value.join('、') : '未分配'"
          >
            <template #icon><UsersRound /></template>
          </NvCell>
          <NvCell title="网络" :value="profile.online.value ? '在线' : '离线'">
            <template #icon><Wifi v-if="profile.online.value" /><WifiOff v-else /></template>
          </NvCell>
        </NvCellGroup>
      </section>

      <section>
        <h2 class="mb-2 text-sm font-semibold text-foreground">当前角色</h2>
        <div
          class="flex min-h-touch flex-wrap items-center gap-2 rounded-xl border border-border bg-card p-3"
        >
          <ShieldCheck class="size-5 text-brand" aria-hidden="true" />
          <NvMobileTag v-for="role in profile.roleNames.value" :key="role" size="sm">
            {{ role }}
          </NvMobileTag>
          <span v-if="!profile.roleNames.value.length" class="text-sm text-muted-foreground"
            >未返回可读角色</span
          >
        </div>
      </section>

      <section>
        <h2 class="mb-2 text-sm font-semibold text-foreground">当前授权范围</h2>
        <div
          class="flex min-h-touch flex-wrap items-center gap-2 rounded-xl border border-border bg-card p-3"
        >
          <NvMobileTag
            v-for="scope in profile.scopeLabels.value"
            :key="scope"
            variant="brand"
            size="sm"
          >
            {{ scope }}
          </NvMobileTag>
          <span v-if="!profile.scopeLabels.value.length" class="text-sm text-muted-foreground"
            >未返回可用范围</span
          >
        </div>
      </section>

      <NvMobileButton
        data-testid="logout"
        variant="outline"
        size="lg"
        block
        :disabled="loggingOut"
        @click="logout"
      >
        {{ loggingOut ? '正在退出…' : '退出登录' }}
      </NvMobileButton>
    </div>
  </NvAppShellMobile>
</template>
