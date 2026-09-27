<script setup lang="ts">
import { NvAppShellMobile, NvMobileButton, NvNavBar } from '@nerv-iip/ui-mobile'
import { storeToRefs } from 'pinia'
import { reactive, shallowRef } from 'vue'
import { useRouter } from 'vue-router'

import { usePdaLogout } from '@/composables/usePdaProfile'
import { useAuthStore } from '@/stores/auth'

definePage({ meta: { requiresAuth: true, title: '修改密码' } })

const auth = useAuthStore()
const { passwordChangeRequired } = storeToRefs(auth)
const { clearCache } = usePdaLogout()
const router = useRouter()
const form = reactive({ currentPassword: '', newPassword: '', confirmPassword: '' })
const error = shallowRef('')
const submitting = shallowRef(false)

function validate() {
  if (!form.currentPassword) return '请输入当前密码。'
  if (!form.newPassword) return '请输入新密码。'
  if (form.confirmPassword !== form.newPassword) return '两次输入的新密码不一致。'
  return ''
}

async function onSubmit() {
  if (submitting.value) return
  error.value = validate()
  if (error.value) return

  submitting.value = true
  try {
    await auth.changePassword(form.currentPassword, form.newPassword)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '修改密码失败，请稍后重试。'
    return
  } finally {
    submitting.value = false
  }

  await router.push('/')
}

// 须改密时哪里都去不了；换人或放弃时只能退出登录。
async function logout() {
  clearCache()
  await auth.logoutAndRevoke({ timeoutMs: 3_000 })
  await router.push('/login')
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <NvNavBar title="修改密码" :back="!passwordChangeRequired" @back="router.back()" />
    </template>

    <div class="space-y-4 p-4">
      <p
        v-if="passwordChangeRequired"
        class="rounded-lg border border-warning/40 bg-warning/10 px-4 py-3 text-sm text-foreground"
        role="status"
      >
        首次登录或密码已被管理员重置，请先修改密码后再继续作业。
      </p>

      <form class="space-y-4" novalidate @submit.prevent="onSubmit">
        <label class="block space-y-1.5">
          <span class="text-sm font-medium text-foreground">当前密码</span>
          <input
            v-model="form.currentPassword"
            name="currentPassword"
            type="password"
            autocomplete="current-password"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-4 text-base text-foreground outline-none focus:border-brand"
          />
        </label>
        <label class="block space-y-1.5">
          <span class="text-sm font-medium text-foreground">新密码</span>
          <input
            v-model="form.newPassword"
            name="newPassword"
            type="password"
            autocomplete="new-password"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-4 text-base text-foreground outline-none focus:border-brand"
          />
        </label>
        <label class="block space-y-1.5">
          <span class="text-sm font-medium text-foreground">确认新密码</span>
          <input
            v-model="form.confirmPassword"
            name="confirmPassword"
            type="password"
            autocomplete="new-password"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-4 text-base text-foreground outline-none focus:border-brand"
          />
        </label>

        <p v-if="error" class="text-sm text-destructive" role="alert">{{ error }}</p>

        <NvMobileButton type="submit" variant="primary" size="lg" block :disabled="submitting">
          {{ submitting ? '正在提交…' : '修改密码' }}
        </NvMobileButton>
      </form>

      <NvMobileButton
        v-if="passwordChangeRequired"
        variant="outline"
        size="lg"
        block
        @click="logout"
      >
        退出登录
      </NvMobileButton>
    </div>
  </NvAppShellMobile>
</template>
