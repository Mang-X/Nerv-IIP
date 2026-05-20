<script setup lang="ts">
import {
  Avatar,
  AvatarFallback,
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@nerv-iip/ui'
import { LogOutIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import type { RouteLocationRaw } from 'vue-router'

interface NavLinkItem {
  label: string
  to: RouteLocationRaw
}

interface NavGroupItem {
  children: NavLinkItem[]
  label: string
}

type NavItem = NavLinkItem | NavGroupItem

function isNavGroup(item: NavItem): item is NavGroupItem {
  return 'children' in item
}

function isNavLink(item: NavItem): item is NavLinkItem {
  return 'to' in item
}

const props = defineProps<{
  navItems: NavItem[]
  title: string
  user?: {
    email?: string
    loginName: string
  }
}>()

const emit = defineEmits<{
  signOut: []
}>()

const userInitials = computed(() => props.user?.loginName.slice(0, 2).toUpperCase() ?? 'U')
</script>

<template>
  <div class="app-shell">
    <aside class="app-shell__sidebar">
      <RouterLink class="app-shell__brand" :to="{ path: '/' }">
        <span class="app-shell__brand-mark">N</span>
        <span class="app-shell__brand-text">{{ title }}</span>
      </RouterLink>

      <nav class="app-shell__nav" aria-label="Primary navigation">
        <template v-for="item in navItems" :key="item.label">
          <div v-if="isNavGroup(item)" class="app-shell__nav-group">
            <span class="app-shell__nav-group-label">{{ item.label }}</span>
            <RouterLink
              v-for="child in item.children"
              :key="child.label"
              class="app-shell__nav-link app-shell__nav-link--child"
              :to="child.to"
            >
              {{ child.label }}
            </RouterLink>
          </div>
          <RouterLink v-else-if="isNavLink(item)" class="app-shell__nav-link" :to="item.to">
            {{ item.label }}
          </RouterLink>
        </template>
      </nav>
    </aside>

    <div class="app-shell__workspace">
      <header class="app-shell__topbar">
        <DropdownMenu v-if="user">
          <DropdownMenuTrigger as-child>
            <Button class="app-shell__user-button" variant="ghost">
              <Avatar class="app-shell__avatar">
                <AvatarFallback>{{ userInitials }}</AvatarFallback>
              </Avatar>
              <span class="app-shell__user-name">{{ user.loginName }}</span>
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuLabel>
              <span>{{ user.loginName }}</span>
              <span v-if="user.email" class="app-shell__user-email">{{ user.email }}</span>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuGroup>
              <DropdownMenuItem @select="emit('signOut')">
                <LogOutIcon data-icon />
                Sign out
              </DropdownMenuItem>
            </DropdownMenuGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      </header>

      <main class="app-shell__main">
        <slot />
      </main>
    </div>
  </div>
</template>

<style scoped>
.app-shell {
  background: var(--background);
  color: var(--foreground);
  display: grid;
  grid-template-columns: 17rem minmax(0, 1fr);
  min-height: 100vh;
}

.app-shell__sidebar {
  background: var(--sidebar);
  border-right: 1px solid var(--sidebar-border);
  color: var(--sidebar-foreground);
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  padding: 1.25rem;
}

.app-shell__brand,
.app-shell__nav-link {
  color: inherit;
  text-decoration: none;
}

.app-shell__brand {
  align-items: center;
  display: flex;
  gap: 0.75rem;
  min-width: 0;
}

.app-shell__brand-mark {
  align-items: center;
  background: var(--primary);
  border-radius: var(--radius-sm);
  color: var(--primary-foreground);
  display: inline-flex;
  flex: 0 0 auto;
  font-weight: 800;
  height: 2.25rem;
  justify-content: center;
  line-height: 1;
  width: 2.25rem;
}

.app-shell__brand-text {
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

.app-shell__nav-group {
  display: grid;
  gap: 0.25rem;
}

.app-shell__nav-group-label {
  color: color-mix(in oklab, var(--sidebar-foreground) 58%, transparent);
  font-size: 0.72rem;
  font-weight: 750;
  letter-spacing: 0;
  line-height: 1.3;
  padding: 0.55rem 0.75rem 0.2rem;
  text-transform: uppercase;
}

.app-shell__nav-link {
  border-radius: var(--radius-sm);
  color: color-mix(in oklab, var(--sidebar-foreground) 78%, transparent);
  display: block;
  font-size: 0.925rem;
  font-weight: 650;
  line-height: 1.35;
  padding: 0.65rem 0.75rem;
  transition:
    background-color 150ms ease,
    color 150ms ease;
}

.app-shell__nav-link--child {
  font-size: 0.875rem;
  font-weight: 600;
  padding-left: 1.25rem;
}

.app-shell__nav-link:hover,
.app-shell__nav-link:focus-visible {
  background: var(--sidebar-accent);
  color: var(--sidebar-accent-foreground);
  outline: none;
}

.app-shell__workspace {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  min-width: 0;
}

.app-shell__topbar {
  align-items: center;
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
  min-height: 4rem;
  padding: 0.75rem 1.5rem;
}

.app-shell__user-button {
  max-width: min(18rem, 100%);
}

.app-shell__avatar {
  flex: 0 0 auto;
}

.app-shell__user-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-shell__main {
  min-width: 0;
  padding: 1.5rem;
}

.app-shell__user-email {
  color: var(--muted-foreground);
  display: block;
  font-size: 0.75rem;
  margin-top: 0.15rem;
}

@media (max-width: 760px) {
  .app-shell {
    grid-template-columns: 1fr;
  }

  .app-shell__sidebar {
    border-bottom: 1px solid var(--sidebar-border);
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

  .app-shell__nav-group {
    display: flex;
    flex: 0 0 auto;
    gap: 0.35rem;
  }

  .app-shell__nav-group-label {
    align-self: center;
    padding: 0.65rem 0.25rem;
  }

  .app-shell__nav-link--child {
    padding-left: 0.75rem;
  }

  .app-shell__topbar,
  .app-shell__main {
    padding: 1rem;
  }
}
</style>
