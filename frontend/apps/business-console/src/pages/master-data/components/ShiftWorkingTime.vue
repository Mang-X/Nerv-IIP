<script setup lang="ts">
import type { BusinessConsoleMasterDataResourceDetail } from '@nerv-iip/api-client'
import { computed, shallowRef, watch } from 'vue'

const props = defineProps<{
  code?: string
  fetchDetail: (code: string) => Promise<BusinessConsoleMasterDataResourceDetail | undefined>
}>()

const detail = shallowRef<BusinessConsoleMasterDataResourceDetail>()
const state = shallowRef<'pending' | 'loaded' | 'failed'>('pending')

watch(
  () => props.code,
  async (code, _, onCleanup) => {
    let current = true
    onCleanup(() => {
      current = false
    })
    detail.value = undefined
    state.value = 'pending'
    try {
      const result = code ? await props.fetchDetail(code) : undefined
      if (!current) return
      detail.value = result
      state.value = result ? 'loaded' : 'failed'
    } catch {
      if (current) state.value = 'failed'
    }
  },
  { immediate: true },
)

const timeRange = computed(() => {
  const start = detail.value?.startsAt
  const end = detail.value?.endsAt
  if (!start || !end) return ''
  return `${start.slice(0, 5)} – ${end <= start ? '次日 ' : ''}${end.slice(0, 5)}`
})
</script>

<template>
  <section class="grid gap-1 border-t border-border pt-3">
    <h3 class="text-sm font-medium">工作时段</h3>
    <p v-if="state === 'pending'" role="status" class="text-sm text-muted-foreground">
      正在读取工作时段…
    </p>
    <p v-else-if="state === 'failed'" role="alert" class="text-sm text-destructive">
      工作时段读取失败，请关闭详情后重试。
    </p>
    <p v-else-if="!timeRange" class="text-sm text-muted-foreground">未配置工作时段。</p>
    <p v-else class="flex flex-wrap gap-x-3 text-sm tabular-nums">
      <span>{{ timeRange }}</span>
      <span v-if="detail?.paidMinutes != null" class="text-muted-foreground">
        {{ detail.paidMinutes === 0 ? '不计薪' : `计薪 ${detail.paidMinutes} 分钟` }}
      </span>
    </p>
  </section>
</template>
