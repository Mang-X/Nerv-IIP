<script setup lang="ts">
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useWmsInbound } from '@/composables/useBusinessWms'
import { inboundOrderStatusLabel, inboundReceiveFlow } from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileResult,
  NvScanBar,
} from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '收货入库',
  },
})

const router = useRouter()
const { filters, orders, pending, error, completeInbound, completePending } = useWmsInbound()

// 选中的收货单号 + GUID（GUID 仅用于 complete 调用与 :key，绝不展示）。
const selectedOrderId = ref('')
const selectedOrderNo = ref('')
const sheetOpen = ref(false)
const completed = ref(false)

// 每次用户发起操作（点单开抽屉）生成一次稳定幂等键，跨重试复用以防丢响应重复入库；
// 选新单/继续后再点单才换新键。绝不在重试时重新生成。
const operationKey = ref('')

// inboundReceiveFlow 驱动进度：selectOrder→complete。
const flowCtx = computed(() => ({
  orderId: selectedOrderId.value || undefined,
  completed: completed.value,
}))
const flowStep = computed(() => inboundReceiveFlow.currentStep(flowCtx.value).id)

// 抽屉或结果展示时停止扫码焦点抢夺，避免破坏浮层 focus-trap。
const scanActive = computed(() => !sheetOpen.value && !completed.value)

const submitError = ref('')

// 空态仅在「无待收货单据且无加载/错误」时出现，避免与错误/加载态打架。
const showEmpty = computed(() => !pending.value && !error.value && orders.value.length === 0)

function onScan(value: string) {
  filters.keyword = value
}

function selectOrder(inboundOrderId: string | undefined, inboundOrderNo: string | undefined) {
  if (!inboundOrderId) return
  selectedOrderId.value = inboundOrderId
  selectedOrderNo.value = inboundOrderNo ?? ''
  // 新操作开始：换一把新幂等键。
  operationKey.value = makeIdempotencyKey()
  submitError.value = ''
  sheetOpen.value = true
}

function closeSheet() {
  sheetOpen.value = false
}

async function confirmComplete() {
  // 防重：幂等键已由组合式注入，但 UI 仍守一道——pending 中直接早退。
  if (completePending.value) return
  submitError.value = ''
  try {
    // 重试复用同一 operationKey（不重新生成），#188 客户端去重可识别为同一操作。
    await completeInbound(selectedOrderId.value, operationKey.value)
    // 成功后立刻关抽屉并切到结果态，重复点击无法再触发。
    sheetOpen.value = false
    completed.value = true
  } catch (e) {
    submitError.value = e instanceof Error ? e.message : '完成收货入库失败'
  }
}

function resetFlow() {
  completed.value = false
  selectedOrderId.value = ''
  selectedOrderNo.value = ''
  // 清空操作键：下次点单会铸新键，保证新操作 ≠ 旧键。
  operationKey.value = ''
  submitError.value = ''
}

function backToList() {
  resetFlow()
}

function goHome() {
  router.push('/').catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">收货入库</h1>
      </div>
    </template>

    <!-- 成功结果态 -->
    <NvMobileResult
      v-if="completed"
      status="success"
      title="入库已完成"
      :description="selectedOrderNo ? `收货单 ${selectedOrderNo}` : undefined"
    >
      <template #actions>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="backToList"
        >
          继续
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="goHome"
        >
          返回
        </button>
      </template>
    </NvMobileResult>

    <div v-else class="space-y-4 p-4">
      <NvScanBar placeholder="扫描收货单号" :active="scanActive" @scan="onScan" />

      <p
        v-if="error"
        data-testid="error-banner"
        class="rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive"
      >
        单据加载失败，请下拉重试或检查网络。
      </p>

      <div
        v-if="showEmpty"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无待收货单据
      </div>

      <div v-else class="overflow-hidden rounded-lg border border-border">
        <NvListRow
          v-for="order in orders"
          :key="order.inboundOrderId"
          :title="order.inboundOrderNo ?? ''"
          :subtitle="inboundOrderStatusLabel(order.status)"
          @select="selectOrder(order.inboundOrderId, order.inboundOrderNo)"
        />
      </div>
    </div>

    <!-- 完成入库确认抽屉 -->
    <NvBottomSheet :open="sheetOpen" title="完成收货入库" @update:open="(v) => (sheetOpen = v)">
      <div class="space-y-4">
        <p v-if="flowStep === 'complete'" class="text-xs text-muted-foreground">
          已选单，待完成入库
        </p>
        <p class="text-base text-foreground">确认完成收货入库？</p>
        <p v-if="selectedOrderNo" class="text-sm text-muted-foreground">
          收货单 {{ selectedOrderNo }}
        </p>

        <p v-if="submitError" class="text-sm text-destructive">{{ submitError }}</p>

        <div class="space-y-2 pt-2">
          <button
            type="button"
            data-testid="confirm-complete"
            :disabled="completePending"
            class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
            @click="confirmComplete"
          >
            {{ completePending ? '提交中…' : '确认完成' }}
          </button>
          <button
            type="button"
            class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
            @click="closeSheet"
          >
            取消
          </button>
        </div>
      </div>
    </NvBottomSheet>
  </NvAppShellMobile>
</template>
