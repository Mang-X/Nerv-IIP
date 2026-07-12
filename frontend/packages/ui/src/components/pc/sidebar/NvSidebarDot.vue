<script setup lang="ts">
/**
 * Pro — trailing status dot for a sidebar menu row (online / warning / alarm),
 * with a soft matching halo. Drop it as the last child of a `SidebarMenuButton`.
 */
withDefaults(defineProps<{ tone?: 'ok' | 'warn' | 'danger' }>(), { tone: 'ok' })
</script>

<template>
  <span
    data-slot="nv-sidebar-dot"
    :class="['nv-sidebar-dot', `nv-sidebar-dot-${tone}`]"
    aria-hidden="true"
  />
</template>

<style scoped>
@layer nv-components {
  .nv-sidebar-dot {
    margin-left: auto;
    width: 7px;
    height: 7px;
    flex-shrink: 0;
    border-radius: 9999px;
  }
  .nv-sidebar-dot-ok {
    background: oklch(0.72 0.17 152);
    box-shadow: 0 0 0 3px color-mix(in oklch, oklch(0.72 0.17 152) 16%, transparent);
  }
  .nv-sidebar-dot-warn {
    background: oklch(0.78 0.16 75);
    box-shadow: 0 0 0 3px color-mix(in oklch, oklch(0.78 0.16 75) 16%, transparent);
  }
  .nv-sidebar-dot-danger {
    background: oklch(0.62 0.22 25);
    box-shadow: 0 0 0 3px color-mix(in oklch, oklch(0.62 0.22 25) 16%, transparent);
  }
}
</style>
