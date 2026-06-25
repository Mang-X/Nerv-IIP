<script setup lang="ts">
import { LogOut } from 'lucide-vue-next'
import { computed } from 'vue'
import {
  Avatar,
  AvatarFallback,
  DropdownMenuPro,
  DropdownMenuProContent,
  DropdownMenuProGroup,
  DropdownMenuProItem,
  DropdownMenuProLabel,
  DropdownMenuProSeparator,
  DropdownMenuProTrigger,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProUser,
  useSidebar,
} from '@nerv-iip/ui'

const props = defineProps<{
  signOutLabel?: string
  user: {
    name: string
    email?: string
  }
}>()

const emit = defineEmits<{
  signOut: []
}>()

const { isMobile } = useSidebar()

const initials = computed(() => props.user.name.slice(0, 2).toUpperCase())
</script>

<template>
  <SidebarMenu>
    <SidebarMenuItem>
      <DropdownMenuPro>
        <DropdownMenuProTrigger as-child>
          <SidebarMenuButton
            size="lg"
            class="data-[state=open]:bg-sidebar-accent data-[state=open]:text-sidebar-accent-foreground"
          >
            <SidebarProUser :name="user.name" :role="user.email" :initials="initials" />
          </SidebarMenuButton>
        </DropdownMenuProTrigger>
        <DropdownMenuProContent
          class="w-(--reka-dropdown-menu-trigger-width) min-w-56 rounded-lg"
          :side="isMobile ? 'bottom' : 'right'"
          align="end"
          :side-offset="4"
        >
          <DropdownMenuProLabel class="p-0 font-normal">
            <div class="flex items-center gap-2 px-1 py-1.5 text-left text-sm">
              <Avatar class="size-8 rounded-lg">
                <AvatarFallback class="rounded-lg">
                  {{ initials }}
                </AvatarFallback>
              </Avatar>
              <div class="grid flex-1 text-left text-sm leading-tight">
                <span class="truncate font-semibold">{{ user.name }}</span>
                <span v-if="user.email" class="truncate text-xs">{{ user.email }}</span>
              </div>
            </div>
          </DropdownMenuProLabel>
          <DropdownMenuProSeparator />
          <DropdownMenuProGroup>
            <DropdownMenuProItem @click="emit('signOut')">
              <LogOut />
              {{ signOutLabel ?? 'Sign out' }}
            </DropdownMenuProItem>
          </DropdownMenuProGroup>
        </DropdownMenuProContent>
      </DropdownMenuPro>
    </SidebarMenuItem>
  </SidebarMenu>
</template>
