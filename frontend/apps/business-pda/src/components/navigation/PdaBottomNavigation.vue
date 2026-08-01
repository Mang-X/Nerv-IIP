<script setup lang="ts">
import { ClipboardList, House, ScanLine, UserRound } from '@lucide/vue'
import { NvTabBar, type TabItem } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const items: TabItem[] = [
  { value: '/', label: '工作台', icon: House },
  { value: '/tasks', label: '任务', icon: ClipboardList },
  { value: '/scan', label: '扫码', icon: ScanLine },
  { value: '/me', label: '我的', icon: UserRound },
]

const activeEntrance = computed(() => {
  const exact = items.find((item) => item.value === route.path)
  return exact?.value ?? '/'
})

function navigate(value: string) {
  if (value === route.path) return
  router.push(value).catch(() => undefined)
}
</script>

<template>
  <footer
    class="pb-safe px-safe fixed inset-x-0 bottom-0 z-40 border-t border-border bg-background"
  >
    <NvTabBar :model-value="activeEntrance" :items="items" @update:model-value="navigate" />
  </footer>
</template>
