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
  const outcome = await auth.logoutAndRevoke({ timeoutMs: 3_000 })
  await router.push(
    outcome.status === 'failed' || outcome.status === 'timed-out'
      ? { path: '/login', query: { logout: outcome.status } }
      : { path: '/login' },
  )
}

function formatResolvedAt(value: string) {
  if (!value) return '未返回'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
</script>

<template>
  <NvAppShellMobile>
    <template #header><NvNavBar title="我的" /></template>

    <div class="space-y-5 p-4">
      <section
        v-if="profile.state.value !== 'ready'"
        class="space-y-3 rounded-xl border border-border bg-card p-4 text-sm"
        :class="profile.state.value === 'loading' ? 'text-muted-foreground' : 'text-destructive'"
        :role="profile.state.value === 'loading' ? 'status' : 'alert'"
      >
        <p v-if="profile.state.value === 'loading'">正在加载角色与范围…</p>
        <p v-else-if="profile.state.value === 'error'">加载角色与范围失败，请重试。</p>
        <p v-else>部分角色或范围加载失败，当前仅展示已确认事实。</p>
        <NvMobileButton
          v-if="profile.state.value !== 'loading'"
          data-testid="retry-profile"
          variant="outline"
          size="sm"
          @click="profile.refresh"
        >
          重新加载
        </NvMobileButton>
      </section>

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
          <span
            v-if="profile.state.value === 'ready' && !profile.roleNames.value.length"
            class="text-sm text-muted-foreground"
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
          <span
            v-if="profile.state.value === 'ready' && !profile.scopeLabels.value.length"
            class="text-sm text-muted-foreground"
            >未返回可用范围</span
          >
        </div>
      </section>

      <section>
        <h2 class="mb-2 text-sm font-semibold text-foreground">WMS 授权作业范围</h2>
        <div
          class="flex min-h-touch flex-wrap items-center gap-2 rounded-xl border border-border bg-card p-3"
        >
          <NvMobileTag
            v-for="scope in profile.wmsAuthorizedScopeLabels.value"
            :key="scope"
            variant="brand"
            size="sm"
          >
            {{ scope }}
          </NvMobileTag>
          <span
            v-if="profile.state.value === 'ready' && !profile.wmsAuthorizedScopeLabels.value.length"
            class="text-sm text-muted-foreground"
            >未返回可用 WMS 范围</span
          >
        </div>
      </section>

      <section>
        <h2 class="mb-2 text-sm font-semibold text-foreground">WMS 当前选择</h2>
        <div
          class="flex min-h-touch flex-wrap items-center gap-2 rounded-xl border border-border bg-card p-3"
        >
          <NvMobileTag v-for="scope in profile.wmsCurrentScopeLabels.value" :key="scope" size="sm">
            {{ scope }}
          </NvMobileTag>
          <span
            v-if="profile.state.value === 'ready' && !profile.wmsCurrentScopeLabels.value.length"
            class="text-sm text-muted-foreground"
            >当前未选择 WMS 作业范围</span
          >
        </div>
      </section>

      <p class="text-xs text-muted-foreground">
        角色与范围更新时间：{{ formatResolvedAt(profile.resolvedAtUtc.value) }}
      </p>

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
