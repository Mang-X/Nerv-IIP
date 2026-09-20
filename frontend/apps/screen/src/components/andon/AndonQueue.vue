<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue'
import { NvScreenButton } from '@nerv-iip/ui'
import { ANDON_PAGE_SIZE, type AndonScope, type AndonSession } from '@/data/contracts/andon'
import AndonQueuePage from './AndonQueuePage.vue'

defineProps<{ session: AndonSession; scope: AndonScope }>()
const page = shallowRef(0)
const total = shallowRef<number>()
const pages = computed(() =>
  total.value === undefined ? undefined : Math.max(1, Math.ceil(total.value / ANDON_PAGE_SIZE)),
)
watch(pages, (count) => {
  if (count !== undefined && page.value >= count) page.value = count - 1
})
</script>

<template>
  <AndonQueuePage
    :key="page"
    :session="session"
    :scope="scope"
    :page="page"
    @total="total = $event"
  />
  <nav class="andon-pagination" aria-label="安灯队列分页">
    <NvScreenButton variant="ghost" :disabled="page === 0" @click="page--">上一页</NvScreenButton>
    <span v-if="total !== undefined"
      >第 {{ page + 1 }} / {{ pages }} 页 · 共 {{ total }} 条活动呼叫</span
    >
    <span v-else>等待当前队列确认</span>
    <NvScreenButton
      variant="ghost"
      :disabled="pages === undefined || page + 1 >= pages"
      @click="page++"
      >下一页</NvScreenButton
    >
  </nav>
</template>

<style scoped>
@layer app {
  .andon-pagination {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 24px;
    color: var(--nv-scr-muted);
    font-size: 20px;
  }
}
</style>
