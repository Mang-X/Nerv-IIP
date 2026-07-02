<script setup lang="ts">
/**
 * Pro — animated collapsible submenu wrapper. Wraps a `SidebarMenuSub` and
 * expands/collapses it via a `grid-template-rows: 0fr → 1fr` transition (the
 * jank-free height animation), with a hairline indent guide. Drive `open` with
 * your own ref on the parent `SidebarMenuButton`.
 */
defineProps<{ open?: boolean }>()
</script>

<template>
  <div data-slot="sidebar-pro-sub" class="sb-pro-sub" :class="{ 'is-open': open }">
    <div class="sb-pro-sub-clip"><slot /></div>
  </div>
</template>

<style scoped>
.sb-pro-sub {
  display: grid;
  grid-template-rows: 0fr;
  transition: grid-template-rows 0.26s var(--ease-out-quart, cubic-bezier(0.25, 1, 0.5, 1));
}
.sb-pro-sub.is-open {
  grid-template-rows: 1fr;
}
/* indent + guide on the wrapper (Tailwind preflight resets <ul> padding). */
.sb-pro-sub-clip {
  overflow: hidden;
  margin-top: 0.1875rem;
  margin-left: 0.9rem;
  padding-left: 0.8rem;
  border-left: 1px solid var(--sidebar-border);
}
@media (prefers-reduced-motion: reduce) {
  .sb-pro-sub {
    transition: none;
  }
}
</style>
