# Page: Login Page

Two-column authentication page. Left: brand/product intro. Right: login form card.

## Reference implementation

`frontend/apps/console/src/pages/login.vue` + `frontend/apps/console/src/components/auth/LoginForm.vue`

## Structure

Login page uses `<style scoped>` with a CSS Grid layout (this is an exception to the Tailwind-only rule — the fluid `clamp()` heading requires it).

```vue
<template>
  <main class="login-page">
    <section class="login-page__intro" aria-labelledby="login-title">
      <p class="login-page__eyebrow">Control plane</p>
      <h1 id="login-title">Nerv-IIP Console</h1>
      <p>Sign in to manage application instances through the Gateway.</p>
    </section>

    <LoginForm :error="authError" :pending="pending" @submit="handleSubmit" />
  </main>
</template>

<style scoped>
.login-page {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(20rem, 28rem);
  gap: 2rem;
  align-items: center;
  min-height: 100vh;
  padding: 2rem;
  background: var(--background);
  color: var(--foreground);
}
.login-page__eyebrow {
  color: var(--primary);
  font-size: 0.8rem;
  font-weight: 800;
  text-transform: uppercase;
}
.login-page__intro h1 {
  font-size: clamp(2rem, 5vw, 4rem);
  line-height: 1;
}
@media (max-width: 820px) {
  .login-page { grid-template-columns: 1fr; padding: 1rem; }
}
</style>
```

## LoginForm component interface

```vue
<!-- Props -->
error?: string       -- displays Alert variant="destructive" above fields
pending?: boolean    -- disables form, shows Spinner in button

<!-- Emits -->
submit: { loginName: string, password: string }
```

## Route meta

```ts
definePage({
  meta: {
    guestOnly: true,   // redirects to / if already authenticated
    title: 'Sign in',
  },
})
```

## Do NOT

- Do not redirect to an arbitrary URL from `?redirect=` without sanitizing — use `sanitizeRedirectPath()`.
- Do not show the topbar/sidebar shell on the login page (it has no `DefaultLayout` wrapper).
- Do not add social login buttons or password recovery links until auth flow supports them.
- Do not use `var(--legacy-color-*)` in new auth pages.
