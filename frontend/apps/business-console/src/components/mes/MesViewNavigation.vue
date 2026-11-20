<script setup lang="ts">
import { RouterLink, useRoute } from 'vue-router'

defineProps<{ views: { title: string; to: { path: string } }[] }>()

const route = useRoute()
</script>

<template>
  <nav
    aria-label="页面视图"
    class="flex w-fit flex-wrap items-center gap-1 rounded-lg bg-muted p-1"
  >
    <RouterLink
      v-for="{ title, to } in views"
      :key="to.path"
      :to="route.path === to.path ? route.fullPath || to.path : to.path"
      class="inline-flex min-h-8 items-center rounded-md px-3 text-sm font-medium text-muted-foreground hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring aria-[current=page]:bg-background aria-[current=page]:text-foreground aria-[current=page]:shadow-sm"
    >
      {{ title }}
    </RouterLink>
  </nav>
</template>
