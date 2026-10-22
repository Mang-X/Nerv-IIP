<script setup lang="ts">
import { computed, watch } from 'vue'
import { useNow } from '@vueuse/core'
import { NvScreenFreshness, NvScreenPanel, useScreenData } from '@nerv-iip/ui'
import { andonErrorMessage, type AndonScope, type AndonSession } from '@/data/contracts/andon'
import { fetchAndonQueue } from '@/data/fetchers/andon'
import { formatScreenFreshness } from '@/data/freshness'

const props = defineProps<{ session: AndonSession; scope: AndonScope; page: number }>()
const emit = defineEmits<{ total: [value: number | undefined] }>()
const { data, error, lastUpdated } = useScreenData(
  () => fetchAndonQueue(props.session, props.scope, props.page),
  { intervalMs: 4000 },
)
const now = useNow({ interval: 1000 })
// 三个轮询周期未成功更新即撤下旧行，挂起请求也不能让旧空队列保持正常。
const expired = computed(
  () => lastUpdated.value !== undefined && now.value.getTime() - lastUpdated.value > 12_000,
)
const unavailable = computed(() => Boolean(error.value) || expired.value)
const freshness = computed(() => formatScreenFreshness(unavailable.value, lastUpdated.value))
watch([data, unavailable], ([value, stale]) => emit('total', stale ? undefined : value?.total), {
  immediate: true,
})
const categories = { materialShortage: '缺料', equipment: '设备', quality: '质量', process: '工艺' }
function time(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—'
}
</script>

<template>
  <NvScreenPanel :title="scope.displayName" class="andon-panel">
    <template #extra><NvScreenFreshness :tone="freshness.tone" :label="freshness.text" /></template>
    <p v-if="unavailable" class="andon-state" role="alert">
      {{ error ? andonErrorMessage(error) : '失联 · 安灯数据超过 12 秒未更新' }}
    </p>
    <p v-else-if="!data" class="andon-state" role="status">正在读取活动呼叫…</p>
    <p v-else-if="data.total === 0" class="andon-state" role="status">当前范围无活动呼叫</p>
    <div v-else class="andon-rows">
      <article
        v-for="call in data.items"
        :key="call.id"
        class="andon-row"
        :class="{ escalated: call.escalatedAtUtc }"
      >
        <div class="andon-kind">
          <strong>{{ call.category ? categories[call.category] : '—' }}</strong>
          <span :class="call.status">{{
            call.status === 'open' ? '待响应' : call.status === 'claimed' ? '处理中' : '已关闭'
          }}</span>
        </div>
        <div class="andon-context">
          <b>{{ call.workOrderId }}</b
          ><span>工作中心 {{ call.workCenterId }} · 工序 {{ call.operationTaskId }}</span>
          <small>呼叫 {{ call.id }} · 发起 {{ time(call.raisedAtUtc) }}</small>
        </div>
        <div class="andon-response">
          <span>{{ call.responderId ? `响应人 ${call.responderId}` : '尚未响应' }}</span>
          <span v-if="call.responseDurationSeconds != null"
            >首次响应 {{ call.responseDurationSeconds }} 秒</span
          >
        </div>
        <div class="andon-escalation">
          <template v-if="call.escalatedAtUtc"
            ><strong>已升级</strong><span>{{ time(call.escalatedAtUtc) }}</span
            ><small>接收人 {{ call.escalationRecipientId }}</small></template
          >
          <span v-else>未升级</span>
        </div>
      </article>
    </div>
  </NvScreenPanel>
</template>

<style scoped>
@layer app {
  .andon-panel {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
  }
  .andon-state {
    height: 100%;
    display: grid;
    place-content: center;
    font-size: 28px;
    color: var(--nv-scr-muted);
  }
  .andon-rows {
    display: flex;
    flex-direction: column;
    gap: 10px;
    flex: 1;
    min-height: 0;
    overflow-y: auto;
  }
  .andon-row {
    flex: none;
    line-height: 1.25;
    display: grid;
    grid-template-columns: 130px minmax(0, 1fr) 250px 290px;
    gap: 24px;
    align-items: center;
    padding: 15px 20px;
    border: 1px solid var(--nv-scr-line);
    border-radius: var(--nv-scr-radius);
    color: var(--nv-scr-text);
  }
  .andon-row.escalated {
    border-color: var(--nv-scr-red);
  }
  .andon-kind,
  .andon-context,
  .andon-response,
  .andon-escalation {
    display: flex;
    flex-direction: column;
    gap: 5px;
    min-width: 0;
    overflow-wrap: anywhere;
  }
  .andon-kind strong {
    font-size: 26px;
  }
  .andon-kind span,
  .andon-context b {
    font-size: 23px;
  }
  .andon-kind .open,
  .andon-escalation strong {
    color: var(--nv-scr-red);
  }
  .andon-kind .claimed {
    color: var(--nv-scr-amber);
  }
  .andon-context span,
  .andon-response {
    font-size: 19px;
  }
  .andon-context small,
  .andon-escalation {
    font-size: 17px;
    color: var(--nv-scr-muted);
  }
  .andon-escalation strong {
    font-size: 23px;
  }
}
</style>
