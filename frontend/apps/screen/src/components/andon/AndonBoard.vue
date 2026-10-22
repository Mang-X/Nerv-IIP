<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { NvScreenButton, NvScreenSelect, useScreenData } from '@nerv-iip/ui'
import { andonErrorMessage, type AndonSession } from '@/data/contracts/andon'
import { fetchAndonScopes } from '@/data/fetchers/andon'
import AndonQueue from './AndonQueue.vue'

const props = defineProps<{ session: AndonSession }>()
const {
  data: scopes,
  error,
  loading,
  refresh,
} = useScreenData(() => fetchAndonScopes(props.session))
const selectedKey = shallowRef<string | number>('')
const options = computed(() =>
  (scopes.value ?? []).map((scope) => ({
    label: scope.displayName,
    value: JSON.stringify([scope.kind, scope.id]),
  })),
)
const selected = computed(() =>
  scopes.value?.find((scope) => JSON.stringify([scope.kind, scope.id]) === selectedKey.value),
)
</script>

<template>
  <section class="andon-board">
    <header class="andon-toolbar">
      <h2 class="andon-heading">安灯活动队列</h2>
      <span class="andon-source">真实数据 · 仅查看</span>
      <NvScreenSelect
        class="andon-scope-select"
        v-model="selectedKey"
        :options="options"
        :disabled="Boolean(error) || !scopes?.length"
        placeholder="请选择真实作业范围"
        aria-label="真实作业范围"
      />
      <NvScreenButton variant="ghost" :disabled="loading" @click="refresh">刷新范围</NvScreenButton>
    </header>
    <p v-if="error" class="andon-message" role="alert">{{ andonErrorMessage(error) }}</p>
    <p v-else-if="!scopes" class="andon-message" role="status">正在核验真实作业范围…</p>
    <p v-else-if="!scopes.length" class="andon-message" role="status">
      未配置真实范围 · 当前账号没有可读取的安灯作业范围
    </p>
    <AndonQueue
      v-else-if="selected"
      :key="String(selectedKey)"
      :session="session"
      :scope="selected"
    />
    <p v-else class="andon-message" role="status">
      请选择真实作业范围；演示产线与历史选择不会作为授权依据。
    </p>
    <footer class="andon-boundary">
      本看板仅接通安灯队列；产量、OEE、节拍与班组聚合尚未接通真实数据。
    </footer>
  </section>
</template>

<style scoped>
@layer app {
  .andon-board {
    height: 100%;
    display: flex;
    flex-direction: column;
    gap: 20px;
    min-height: 0;
  }
  .andon-toolbar {
    display: flex;
    align-items: center;
    gap: 22px;
  }
  .andon-heading {
    white-space: nowrap;
    margin: 0;
    font-size: 28px;
    color: var(--nv-scr-text);
  }
  .andon-source {
    white-space: nowrap;
    color: var(--nv-scr-cyan);
    font-size: 18px;
    margin-right: auto;
  }
  .andon-scope-select {
    width: 440px;
    flex: none;
  }
  .andon-message {
    flex: 1;
    display: grid;
    place-content: center;
    color: var(--nv-scr-muted);
    font-size: 26px;
  }
  .andon-boundary {
    margin-top: auto;
    padding-top: 14px;
    border-top: 1px solid var(--nv-scr-divider);
    color: var(--nv-scr-muted);
    font-size: 18px;
  }
}
</style>
