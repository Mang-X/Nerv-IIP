<script setup lang="ts">
import type { Component } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import { ChevronRight } from 'lucide-vue-next'
import {
  CollapsibleContent,
  CollapsibleRoot as Collapsible,
  CollapsibleTrigger,
} from 'reka-ui'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import {
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
} from '@nerv-iip/ui'

defineProps<{
  items: {
    title: string
    to?: RouteLocationRaw
    icon?: Component
    isActive?: boolean
    items?: {
      title: string
      to: RouteLocationRaw
    }[]
  }[]
  label?: string
}>()

const route = useRoute()
const router = useRouter()

function isActive(to: RouteLocationRaw): boolean {
  try {
    return router.resolve(to).path === route.path
  }
  catch {
    return false
  }
}
</script>

<template>
  <SidebarGroup>
    <SidebarGroupLabel>{{ label ?? 'Platform' }}</SidebarGroupLabel>
    <SidebarMenu>
      <template v-for="item in items" :key="item.title">
        <SidebarMenuItem v-if="!item.items?.length && item.to">
          <SidebarMenuButton as-child :tooltip="item.title" :is-active="isActive(item.to)">
            <RouterLink :to="item.to">
              <component :is="item.icon" v-if="item.icon" />
              <span>{{ item.title }}</span>
            </RouterLink>
          </SidebarMenuButton>
        </SidebarMenuItem>

        <Collapsible
          v-else
          as-child
          :default-open="item.isActive"
          class="group/collapsible"
        >
          <SidebarMenuItem>
            <CollapsibleTrigger as-child>
              <SidebarMenuButton :tooltip="item.title">
                <component :is="item.icon" v-if="item.icon" />
                <span>{{ item.title }}</span>
                <ChevronRight class="ml-auto transition-transform duration-200 group-data-[state=open]/collapsible:rotate-90" />
              </SidebarMenuButton>
            </CollapsibleTrigger>
            <CollapsibleContent>
              <SidebarMenuSub>
                <SidebarMenuSubItem v-for="subItem in item.items" :key="subItem.title">
                  <SidebarMenuSubButton as-child :is-active="isActive(subItem.to)">
                    <RouterLink :to="subItem.to">
                      <span>{{ subItem.title }}</span>
                    </RouterLink>
                  </SidebarMenuSubButton>
                </SidebarMenuSubItem>
              </SidebarMenuSub>
            </CollapsibleContent>
          </SidebarMenuItem>
        </Collapsible>
      </template>
    </SidebarMenu>
  </SidebarGroup>
</template>
