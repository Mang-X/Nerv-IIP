<script setup lang="ts">
import { useAuthStore } from '@/stores/auth'
import { sanitizeRedirectPath } from '@nerv-iip/business-core'
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    guestOnly: true,
    title: '登录',
  },
})

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const loginName = ref('')
const password = ref('')
const error = ref('')
const submitting = ref(false)

async function onSubmit() {
  if (submitting.value) return
  error.value = ''
  submitting.value = true
  try {
    await auth.login(loginName.value, password.value)
    const redirect = sanitizeRedirectPath(route.query.redirect)
    await router.push(redirect)
  } catch (e) {
    error.value = e instanceof Error ? e.message : '登录失败'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div
    class="pt-safe pb-safe flex min-h-dvh flex-col justify-center bg-background pl-[max(1.5rem,env(safe-area-inset-left))] pr-[max(1.5rem,env(safe-area-inset-right))]"
  >
    <div class="mb-8 text-center">
      <h1 class="text-2xl font-semibold text-foreground">Nerv-IIP 手持作业台</h1>
      <p class="mt-1 text-sm text-muted-foreground">请登录以开始作业</p>
    </div>

    <form class="space-y-4" @submit.prevent="onSubmit">
      <input
        name="loginName"
        v-model="loginName"
        aria-label="账号"
        placeholder="账号"
        autocomplete="username"
        autocapitalize="off"
        spellcheck="false"
        class="min-h-touch w-full rounded-lg border border-border bg-card px-4 text-base text-foreground outline-none focus:border-brand"
      />
      <input
        name="password"
        v-model="password"
        aria-label="密码"
        type="password"
        placeholder="密码"
        autocomplete="current-password"
        class="min-h-touch w-full rounded-lg border border-border bg-card px-4 text-base text-foreground outline-none focus:border-brand"
      />

      <p v-if="error" class="text-sm text-destructive">{{ error }}</p>

      <button
        type="submit"
        :disabled="submitting"
        class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
      >
        {{ submitting ? '登录中…' : '登录' }}
      </button>
    </form>
  </div>
</template>
