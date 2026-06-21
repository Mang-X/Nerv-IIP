<script setup lang="ts">
import { Button, ThemePicker } from '@nerv-iip/ui'
import { Moon, Search, Sun } from 'lucide-vue-next'
import { useData } from 'vitepress'
import DefaultTheme from 'vitepress/theme'
import { onMounted, ref } from 'vue'

// Wrap the default layout and swap VitePress's built-in nav controls for our own
// @nerv-iip/ui components, so the docs chrome matches the system it documents:
//   • the search box  → our Button-styled trigger (opens VitePress's own local
//     search modal via its Ctrl/⌘-K shortcut, so the index/modal still work);
//   • the appearance switch → our Sun/Moon Button bound to `isDark`;
//   • the accent picker (ThemePicker) was already here.
// The originals are hidden in style.css (the search container stays mounted so
// its modal + key listeners keep working — only its button is hidden).
const { Layout } = DefaultTheme
const { isDark } = useData()

const isMac = ref(false)
onMounted(() => {
  isMac.value = /mac|iphone|ipad|ipod/i.test(navigator.userAgent)
})

// Open VitePress's local search by clicking its own (hidden) trigger button,
// which flips the modal's `showSearch` flag. More deterministic than synthesizing
// the Ctrl/⌘-K keystroke, and reuses VitePress's exact open path + search index.
function openSearch() {
  const btn = document.querySelector<HTMLButtonElement>('.VPNavBarSearch #local-search button')
  btn?.click()
}

function toggleAppearance() {
  isDark.value = !isDark.value
}
</script>

<template>
  <Layout>
    <template #nav-bar-content-before>
      <ClientOnly>
        <div class="ds-doc-search-slot">
          <Button
            variant="outline"
            size="lg"
            class="ds-doc-search h-9 text-muted-foreground md:w-56 md:justify-between"
            aria-label="搜索文档"
            @click="openSearch"
          >
            <span class="flex items-center gap-1.5">
              <Search class="size-4" />
              <span class="ds-doc-search-text hidden md:inline">搜索文档…</span>
            </span>
            <kbd class="ds-doc-kbd hidden md:inline-flex">{{ isMac ? '⌘' : 'Ctrl' }} K</kbd>
          </Button>
        </div>
      </ClientOnly>
    </template>

    <template #nav-bar-content-after>
      <ClientOnly>
        <div class="ds-doc-controls">
          <Button
            variant="ghost"
            size="icon-sm"
            class="ds-doc-appearance"
            :aria-label="isDark ? '切换到亮色主题' : '切换到暗色主题'"
            :title="isDark ? '切换到亮色主题' : '切换到暗色主题'"
            @click="toggleAppearance"
          >
            <Moon v-if="isDark" class="size-4" />
            <Sun v-else class="size-4" />
          </Button>
          <ThemePicker class="ds-doc-accent" />
        </div>
      </ClientOnly>
    </template>
  </Layout>
</template>

<style>
/* The search trigger sits with the right-hand control group (title left, tools
   right — a proper header), not stretched across the bar. A small gap separates
   it from the nav menu that follows. */
.ds-doc-search-slot {
  display: flex;
  align-items: center;
}
@media (min-width: 768px) {
  .ds-doc-search-slot {
    margin-inline-end: 0.75rem;
  }
}
.ds-doc-search {
  font-weight: 400;
}
.ds-doc-kbd {
  align-items: center;
  height: 1.25rem;
  padding: 0 0.375rem;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: color-mix(in oklch, var(--muted) 60%, transparent);
  font-size: 0.6875rem;
  font-family: inherit;
  line-height: 1;
  color: var(--muted-foreground);
}

/* Our nav controls, vertically centered next to the social links. */
.ds-doc-controls {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  margin-inline-start: 0.25rem;
}
.ds-doc-accent {
  display: inline-flex;
  align-items: center;
}
</style>
