<script setup lang="ts">
import { ChevronsUpDownIcon } from 'lucide-vue-next'
import { computed } from 'vue'

/**
 * Pro — sidebar workspace brand lockup (sits in `SidebarHeader`). A gradient
 * logo tile + workspace name + sub line, with text/caret hidden when the
 * sidebar collapses to the icon rail. Renders a button by default; pass
 * `as-child` semantics by wrapping your own element via the default slot is not
 * needed — use the `to`-less button or compose inside a `SidebarMenuButton`.
 */
const props = withDefaults(
  defineProps<{
    name: string
    sub?: string
    /** Logo glyph; defaults to the name's first character. */
    logo?: string
    /** Trailing up/down caret (workspace-switcher affordance). */
    caret?: boolean
  }>(),
  { caret: true },
)

const glyph = computed(() => props.logo ?? props.name.slice(0, 1).toUpperCase())
</script>

<template>
  <button
    type="button"
    data-slot="sidebar-pro-brand"
    class="sb-pro-brand group-data-[collapsible=icon]:justify-center"
  >
    <span class="sb-pro-logo">{{ glyph }}</span>
    <span class="sb-pro-brand-text group-data-[collapsible=icon]:hidden">
      <span class="sb-pro-brand-name">{{ name }}</span>
      <span v-if="sub" class="sb-pro-brand-sub">{{ sub }}</span>
    </span>
    <ChevronsUpDownIcon
      v-if="caret"
      class="sb-pro-brand-caret group-data-[collapsible=icon]:hidden"
      :size="16"
      aria-hidden="true"
    />
  </button>
</template>

<style scoped>
.sb-pro-brand {
  display: flex;
  width: 100%;
  align-items: center;
  gap: 0.625rem;
  border-radius: 0.625rem;
  padding: 0.375rem;
  text-align: left;
  transition: background-color 0.15s var(--ease-out-quart, ease);
}
.sb-pro-brand:hover {
  background: var(--sidebar-accent, var(--muted));
}
.sb-pro-logo {
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  flex-shrink: 0;
  border-radius: 0.5rem;
  background: linear-gradient(140deg, var(--brand), color-mix(in oklch, var(--brand) 70%, black));
  color: var(--brand-foreground);
  font-size: 0.875rem;
  font-weight: 700;
  box-shadow: inset 0 1px 0 0 color-mix(in oklch, white 22%, transparent);
}
.sb-pro-brand-text {
  display: flex;
  min-width: 0;
  flex: 1;
  flex-direction: column;
  line-height: 1.2;
}
.sb-pro-brand-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.8125rem;
  font-weight: 600;
}
.sb-pro-brand-sub {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.6875rem;
  color: var(--muted-foreground);
}
.sb-pro-brand-caret {
  flex-shrink: 0;
  color: var(--muted-foreground);
}
</style>
