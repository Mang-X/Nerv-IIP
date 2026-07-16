<script setup lang="ts">
import type { PrimitiveProps } from 'reka-ui'
import { ChevronsUpDownIcon } from '@lucide/vue'
import { Primitive } from 'reka-ui'
import { computed } from 'vue'

/**
 * Pro — sidebar workspace brand lockup (sits in `SidebarHeader`). A gradient
 * logo tile + workspace name + sub line, with text/caret hidden when the sidebar
 * collapses to the icon rail. Polymorphic via `as` — a `<button>` by default
 * (workspace switcher, with `caret`), or pass `:as="RouterLink" :to=...` to make
 * it a home link (set `:caret="false"`).
 */
const props = withDefaults(
  defineProps<
    PrimitiveProps & {
      name: string
      sub?: string
      /** Logo glyph; defaults to the name's first character. */
      logo?: string
      /** Trailing up/down caret (workspace-switcher affordance). */
      caret?: boolean
    }
  >(),
  { as: 'button', caret: true },
)

const glyph = computed(() => props.logo ?? props.name.slice(0, 1).toUpperCase())
</script>

<template>
  <Primitive
    :as="as"
    :as-child="asChild"
    :type="as === 'button' ? 'button' : undefined"
    data-slot="nv-sidebar-brand"
    class="nv-sidebar-brand group-data-[collapsible=icon]:justify-center"
  >
    <span class="nv-sidebar-logo">{{ glyph }}</span>
    <span class="nv-sidebar-brand-text group-data-[collapsible=icon]:hidden">
      <span class="nv-sidebar-brand-name">{{ name }}</span>
      <span v-if="sub" class="nv-sidebar-brand-sub">{{ sub }}</span>
    </span>
    <ChevronsUpDownIcon
      v-if="caret"
      class="nv-sidebar-brand-caret group-data-[collapsible=icon]:hidden"
      :size="16"
      aria-hidden="true"
    />
  </Primitive>
</template>

<style scoped>
@layer nv-components {
  .nv-sidebar-brand {
    display: flex;
    width: 100%;
    align-items: center;
    gap: 0.625rem;
    border-radius: 0.625rem;
    padding: 0.375rem;
    text-align: left;
    color: inherit;
    text-decoration: none;
    transition: background-color 0.15s var(--nv-ease-out-quart, ease);
  }
  .nv-sidebar-brand:hover {
    background: var(--sidebar-accent, var(--muted));
  }
  .nv-sidebar-logo {
    display: grid;
    place-items: center;
    width: 2rem;
    height: 2rem;
    flex-shrink: 0;
    border-radius: 0.5rem;
    background: linear-gradient(
      140deg,
      var(--nv-brand),
      color-mix(in oklch, var(--nv-brand) 70%, black)
    );
    color: var(--nv-brand-foreground);
    font-size: 0.875rem;
    font-weight: 700;
    box-shadow: inset 0 1px 0 0 color-mix(in oklch, white 22%, transparent);
  }
  .nv-sidebar-brand-text {
    display: flex;
    min-width: 0;
    flex: 1;
    flex-direction: column;
    line-height: 1.2;
  }
  .nv-sidebar-brand-name {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.8125rem;
    font-weight: 600;
  }
  .nv-sidebar-brand-sub {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.6875rem;
    color: var(--muted-foreground);
  }
  .nv-sidebar-brand-caret {
    flex-shrink: 0;
    color: var(--muted-foreground);
  }
}
</style>
