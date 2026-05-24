<script setup lang="ts">
import LoginForm from '@/components/auth/LoginForm.vue'
import { sanitizeRedirectPath } from '@/router/redirects'
import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    guestOnly: true,
    title: 'routes.login',
  },
})

const { t } = useI18n()
const auth = useAuthStore()
const { authError } = storeToRefs(auth)
const route = useRoute('/login')
const router = useRouter()
const pending = shallowRef(false)
const redirectPath = computed(() => sanitizeRedirectPath(route.query.redirect))

async function submit(credentials: { loginName: string; password: string }) {
  pending.value = true
  try {
    await auth.login(credentials.loginName, credentials.password)
  } catch {
    return
  } finally {
    pending.value = false
  }

  await router.push(redirectPath.value)
}
</script>

<template>
  <main class="grid min-h-svh lg:grid-cols-2">
    <div class="relative hidden overflow-hidden bg-primary lg:flex lg:flex-col lg:items-center lg:justify-center">
      <div
        class="pointer-events-none absolute inset-0"
        style="background-image: radial-gradient(rgba(255 255 255 / 0.06) 1px, transparent 1px); background-size: 24px 24px;"
      />
      <div class="pointer-events-none absolute inset-0 bg-gradient-to-b from-transparent via-transparent to-black/20" />

      <div class="relative z-10 flex flex-col items-center gap-6 px-8 text-center text-primary-foreground">
        <div class="flex size-16 items-center justify-center rounded-2xl bg-white/15 text-2xl font-extrabold backdrop-blur-sm">
          B
        </div>
        <div class="flex flex-col gap-2">
          <h1 class="text-3xl font-bold tracking-tight">{{ t('app.brand') }}</h1>
          <p class="text-sm font-medium uppercase tracking-widest text-primary-foreground/70">
            {{ t('app.tagline') }}
          </p>
        </div>
        <p class="max-w-sm text-sm leading-relaxed text-primary-foreground/60">
          {{ t('app.description') }}
        </p>
      </div>
    </div>

    <div class="flex flex-col items-center justify-center p-6 md:p-10">
      <div class="flex w-full max-w-sm flex-col gap-6">
        <div class="flex items-center gap-3 self-center lg:hidden">
          <div class="flex size-9 items-center justify-center rounded-lg bg-primary text-sm font-extrabold text-primary-foreground">
            B
          </div>
          <span class="text-lg font-semibold">{{ t('app.brand') }}</span>
        </div>
        <LoginForm :error="authError" :pending="pending" @submit="submit" />
        <p class="text-center text-xs text-muted-foreground">
          {{ t('app.tagline') }}
        </p>
      </div>
    </div>
  </main>
</template>
