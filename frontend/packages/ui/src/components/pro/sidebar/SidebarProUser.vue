<script setup lang="ts">
import { ChevronsUpDownIcon } from 'lucide-vue-next'
import { computed } from 'vue'

/**
 * Pro — sidebar footer user row: initial avatar with an online status pip, plus
 * name / role. Place it inside a footer `SidebarMenuButton` (size `lg`); text +
 * caret collapse away with the icon rail.
 */
const props = withDefaults(
  defineProps<{
    name: string
    role?: string
    /** Avatar initials; defaults to the name's first character. */
    initials?: string
    /** Show the online status pip on the avatar. */
    online?: boolean
    caret?: boolean
  }>(),
  { online: true, caret: true },
)

const inits = computed(() => props.initials ?? props.name.slice(0, 1).toUpperCase())
</script>

<template>
  <span data-slot="sidebar-pro-user" class="sb-pro-user-row">
    <span class="sb-pro-avatar">
      {{ inits }}
      <span v-if="online" class="sb-pro-avatar-status" aria-hidden="true" />
    </span>
    <span class="sb-pro-user group-data-[collapsible=icon]:hidden">
      <span class="sb-pro-user-name">{{ name }}</span>
      <span v-if="role" class="sb-pro-user-role">{{ role }}</span>
    </span>
    <ChevronsUpDownIcon
      v-if="caret"
      class="sb-pro-user-caret group-data-[collapsible=icon]:hidden"
      :size="16"
      aria-hidden="true"
    />
  </span>
</template>

<style scoped>
.sb-pro-user-row {
  display: flex;
  width: 100%;
  align-items: center;
  gap: 0.5rem;
}
.sb-pro-avatar {
  position: relative;
  display: grid;
  place-items: center;
  width: 2rem;
  height: 2rem;
  flex-shrink: 0;
  border-radius: 9999px;
  background: var(--muted);
  font-size: 0.75rem;
  font-weight: 600;
}
.sb-pro-avatar-status {
  position: absolute;
  right: -1px;
  bottom: -1px;
  width: 0.625rem;
  height: 0.625rem;
  border-radius: 9999px;
  background: oklch(0.72 0.17 152);
  border: 2px solid var(--sidebar, var(--background));
}
.sb-pro-user {
  display: flex;
  min-width: 0;
  flex: 1;
  flex-direction: column;
  line-height: 1.2;
}
.sb-pro-user-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.8125rem;
  font-weight: 600;
}
.sb-pro-user-role {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.6875rem;
  color: var(--muted-foreground);
}
.sb-pro-user-caret {
  margin-left: auto;
  flex-shrink: 0;
  color: var(--muted-foreground);
}
</style>
