<script setup lang="ts">
import LoginForm from '@/components/auth/LoginForm.vue'
import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { computed, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { sanitizeRedirectPath } from '@/router/redirects'

definePage({
  meta: {
    guestOnly: true,
    title: 'Sign in',
  },
})

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
  <main class="login-page">
    <section class="login-page__intro" aria-labelledby="login-title">
      <p class="login-page__eyebrow">Control plane</p>
      <h1 id="login-title">Nerv-IIP Console</h1>
      <p>Sign in to manage application instances and operation tasks through the Gateway.</p>
    </section>

    <LoginForm :error="authError" :pending="pending" @submit="submit" />
  </main>
</template>

<style scoped>
.login-page {
  align-items: center;
  background: var(--background);
  color: var(--foreground);
  display: grid;
  gap: 2rem;
  grid-template-columns: minmax(0, 1fr) minmax(20rem, 28rem);
  min-height: 100vh;
  padding: 2rem;
}

.login-page__intro {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: 42rem;
}

.login-page__intro h1,
.login-page__intro p {
  margin: 0;
}

.login-page__intro h1 {
  font-size: clamp(2rem, 5vw, 4rem);
  line-height: 1;
}

.login-page__intro p {
  color: var(--muted-foreground);
  font-size: 1rem;
  line-height: 1.6;
}

.login-page__eyebrow {
  color: var(--primary);
  font-size: 0.8rem;
  font-weight: 800;
  letter-spacing: 0;
  text-transform: uppercase;
}

@media (max-width: 820px) {
  .login-page {
    grid-template-columns: 1fr;
    padding: 1rem;
  }
}
</style>
