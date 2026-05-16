<script setup lang="ts">
interface NavItem {
  href: string
  label: string
}

defineProps<{
  navItems: NavItem[]
  title: string
}>()
</script>

<template>
  <div class="app-shell">
    <aside class="app-shell__sidebar">
      <a class="app-shell__brand" href="/">
        <span class="app-shell__brand-mark">N</span>
        <span class="app-shell__brand-text">{{ title }}</span>
      </a>

      <nav class="app-shell__nav" aria-label="Primary navigation">
        <a v-for="item in navItems" :key="item.href" class="app-shell__nav-link" :href="item.href">
          {{ item.label }}
        </a>
      </nav>
    </aside>

    <main class="app-shell__main">
      <slot />
    </main>
  </div>
</template>

<style scoped>
.app-shell {
  background: #f6f8fb;
  color: #0f172a;
  display: grid;
  grid-template-columns: 17rem minmax(0, 1fr);
  min-height: 100vh;
}

.app-shell__sidebar {
  background: #111827;
  border-right: 1px solid #1f2937;
  color: #f8fafc;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  padding: 1.25rem;
}

.app-shell__brand {
  align-items: center;
  color: inherit;
  display: flex;
  gap: 0.75rem;
  min-width: 0;
  text-decoration: none;
}

.app-shell__brand-mark {
  align-items: center;
  background: #38bdf8;
  border-radius: 0.5rem;
  color: #082f49;
  display: inline-flex;
  flex: 0 0 auto;
  font-size: 1rem;
  font-weight: 800;
  height: 2.25rem;
  justify-content: center;
  line-height: 1;
  width: 2.25rem;
}

.app-shell__brand-text {
  font-size: 1rem;
  font-weight: 800;
  letter-spacing: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-shell__nav {
  display: grid;
  gap: 0.35rem;
}

.app-shell__nav-link {
  border-radius: 0.5rem;
  color: #cbd5e1;
  display: block;
  font-size: 0.925rem;
  font-weight: 650;
  line-height: 1.35;
  padding: 0.65rem 0.75rem;
  text-decoration: none;
  transition:
    background-color 150ms ease,
    color 150ms ease;
}

.app-shell__nav-link:hover,
.app-shell__nav-link:focus-visible {
  background: #1f2937;
  color: #ffffff;
  outline: none;
}

.app-shell__main {
  min-width: 0;
  padding: 1.5rem;
}

@media (max-width: 760px) {
  .app-shell {
    grid-template-columns: 1fr;
  }

  .app-shell__sidebar {
    border-bottom: 1px solid #1f2937;
    border-right: 0;
    gap: 1rem;
    padding: 1rem;
  }

  .app-shell__nav {
    display: flex;
    gap: 0.5rem;
    overflow-x: auto;
    padding-bottom: 0.15rem;
  }

  .app-shell__nav-link {
    flex: 0 0 auto;
    white-space: nowrap;
  }

  .app-shell__main {
    padding: 1rem;
  }
}
</style>
