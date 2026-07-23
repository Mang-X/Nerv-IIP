<script setup lang="ts">
import type { FulfillmentNode } from '@/composables/useFulfillmentTimeline'
import { NvButton } from '@nerv-iip/ui'
import { AlertTriangleIcon, LockIcon, RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { formatDateTime } from '@/pages/erp/shared'

const props = defineProps<{ node: FulfillmentNode }>()
const emit = defineEmits<{ retry: [] }>()

// 失败子类文案（A1：409 / 超时 / 其它错误可区分）。
const failureText = computed(() => {
  switch (props.node.failureKind) {
    case 'conflict':
      return '数据冲突（409），请刷新后重试。'
    case 'timeout':
      return '请求超时或网络中断，请重试。'
    default:
      return '该来源加载失败，请重试。'
  }
})
</script>

<template>
  <div class="nv-ft-node" :data-status="node.status">
    <div class="nv-ft-node-head">
      <span class="nv-ft-node-title">{{ node.title }}</span>
      <span v-if="node.businessNo" class="nv-ft-node-no">{{ node.businessNo }}</span>
    </div>

    <!-- 已确认 -->
    <template v-if="node.status === 'established'">
      <div class="nv-ft-node-meta">
        <span v-if="node.detailStatus" class="nv-ft-chip">{{ node.detailStatus }}</span>
        <span v-if="node.updatedAt" class="nv-ft-time"
          >更新于 {{ formatDateTime(node.updatedAt) }}</span
        >
      </div>
      <p v-if="node.linkLabel" class="nv-ft-node-link">关联键：{{ node.linkLabel }}</p>
      <RouterLink v-if="node.drill" :to="node.drill" class="nv-ft-drill">查看详情 →</RouterLink>
    </template>

    <!-- 加载中 -->
    <template v-else-if="node.status === 'loading'">
      <p class="nv-ft-node-muted" role="status">加载中…</p>
    </template>

    <!-- 尚未产生（空态，有稳定关联键） -->
    <template v-else-if="node.status === 'pending'">
      <p class="nv-ft-node-muted">尚未产生</p>
      <p v-if="node.ruleNote" class="nv-ft-node-rule">{{ node.ruleNote }}</p>
    </template>

    <!-- 尚未建立关联（无稳定关联键） -->
    <template v-else-if="node.status === 'unlinked'">
      <p class="nv-ft-node-muted">尚未建立关联</p>
      <p v-if="node.ruleNote" class="nv-ft-node-rule">{{ node.ruleNote }}</p>
    </template>

    <!-- 权限受限（403，不泄露数据） -->
    <template v-else-if="node.status === 'restricted'">
      <p class="nv-ft-node-restricted">
        <LockIcon class="size-3.5" aria-hidden="true" />
        权限受限，无权查看该节点数据。
      </p>
    </template>

    <!-- 单源失败（含重试，不拖垮整条时间线） -->
    <template v-else-if="node.status === 'failed'">
      <p class="nv-ft-node-failed">
        <AlertTriangleIcon class="size-3.5" aria-hidden="true" />
        {{ failureText }}
      </p>
      <NvButton size="sm" variant="outline" type="button" @click="emit('retry')">
        <RefreshCwIcon aria-hidden="true" />
        重试
      </NvButton>
    </template>

    <p v-if="node.source" class="nv-ft-node-source">数据源：{{ node.source }}</p>
  </div>
</template>

<style scoped>
.nv-ft-node {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.nv-ft-node-head {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0.5rem;
}
.nv-ft-node-title {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--foreground);
}
.nv-ft-node-no {
  font-size: 0.8125rem;
  font-variant-numeric: tabular-nums;
  color: var(--nv-brand);
}
.nv-ft-node-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}
.nv-ft-chip {
  display: inline-flex;
  align-items: center;
  padding: 0.05rem 0.4rem;
  border-radius: 0.375rem;
  font-size: 0.75rem;
  background-color: var(--muted);
  color: var(--foreground);
}
.nv-ft-time {
  font-size: 0.75rem;
  color: var(--muted-foreground);
}
.nv-ft-node-link {
  margin: 0;
  font-size: 0.75rem;
  color: var(--muted-foreground);
}
.nv-ft-drill {
  align-self: flex-start;
  font-size: 0.8125rem;
  color: var(--nv-brand);
  text-decoration: none;
}
.nv-ft-drill:hover {
  text-decoration: underline;
}
.nv-ft-node-muted {
  margin: 0;
  font-size: 0.8125rem;
  color: var(--muted-foreground);
}
.nv-ft-node-rule {
  margin: 0;
  font-size: 0.75rem;
  line-height: 1.4;
  color: var(--muted-foreground);
  max-width: 60ch;
}
.nv-ft-node-restricted,
.nv-ft-node-failed {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  margin: 0;
  font-size: 0.8125rem;
}
.nv-ft-node-restricted {
  color: var(--muted-foreground);
}
.nv-ft-node-failed {
  color: var(--destructive);
}
.nv-ft-node-source {
  margin: 0.15rem 0 0;
  font-size: 0.6875rem;
  color: var(--muted-foreground);
  opacity: 0.85;
}
</style>
