<script setup lang="ts">
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useWmsCount } from '@/composables/useBusinessWms'
import { countExecutionFlow, countExecutionStatusLabel } from '@nerv-iip/business-core'
import { AppShellMobile, BottomSheet, ListRow, Result, ScanBar } from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '盘点',
  },
})

const router = useRouter()
const { filters, executions, pending, error, completeCount, completePending } = useWmsCount()

// 选中的盘点号 + GUID（GUID 仅用于 complete 调用与 :key，绝不展示）。
const selectedExecutionId = ref('')
const selectedCountNo = ref('')
const expectedQuantity = ref(0)
const sheetOpen = ref(false)
const completed = ref(false)

// 每次用户发起操作（点任务开抽屉）生成一次稳定幂等键，跨重试复用以防丢响应重复提交；
// 选新任务/继续后再点任务才换新键。绝不在重试时重新生成。
const operationKey = ref('')

// 实盘数量录入。type=number 下 v-model 解包可能是 number 或 ''，统一按字符串校验。
const countedQuantityText = ref<string | number>('')
const countedQuantity = computed(() => Number(countedQuantityText.value))
// 有效：非空、可解析为有限数且非负。
const validCount = computed(() => {
  const text = String(countedQuantityText.value).trim()
  if (text === '') return false
  const n = Number(text)
  return Number.isFinite(n) && n >= 0
})
// 差异实时提示（仅在已填有效值时展示）。
const variance = computed(() => countedQuantity.value - expectedQuantity.value)

// countExecutionFlow 驱动进度：selectExecution→enterCount→complete。
const flowCtx = computed(() => ({
  countExecutionId: selectedExecutionId.value || undefined,
  countEntered: validCount.value || undefined,
  completed: completed.value,
}))
const flowStep = computed(() => countExecutionFlow.currentStep(flowCtx.value).id)
const countStepHint = computed(() =>
  flowStep.value === 'complete' ? '实盘数已填，待提交' : '请填写实盘数量',
)

// 抽屉或结果展示时停止扫码焦点抢夺，避免破坏浮层 focus-trap。
const scanActive = computed(() => !sheetOpen.value && !completed.value)

const submitError = ref('')

// 空态仅在「无盘点任务且无加载/错误」时出现，避免与错误/加载态打架。
const showEmpty = computed(() => !pending.value && !error.value && executions.value.length === 0)

function onScan(value: string) {
  filters.locationCode = value
}

function selectExecution(countExecutionId: string | undefined, countNo: string | undefined, expected: number | undefined) {
  if (!countExecutionId) return
  selectedExecutionId.value = countExecutionId
  selectedCountNo.value = countNo ?? ''
  expectedQuantity.value = expected ?? 0
  countedQuantityText.value = ''
  // 新操作开始：换一把新幂等键。
  operationKey.value = makeIdempotencyKey()
  submitError.value = ''
  sheetOpen.value = true
}

function closeSheet() {
  sheetOpen.value = false
}

async function confirmComplete() {
  // 防重：pending 中或实盘数无效直接早退（按钮也已禁用，UI 守双道）。
  if (completePending.value || !validCount.value) return
  submitError.value = ''
  try {
    // 重试复用同一 operationKey（不重新生成），#188 客户端去重可识别为同一操作。
    await completeCount(selectedExecutionId.value, {
      countedQuantity: countedQuantity.value,
      idempotencyKey: operationKey.value,
    })
    // 成功后立刻关抽屉并切到结果态，重复点击无法再触发。
    sheetOpen.value = false
    completed.value = true
  } catch (e) {
    submitError.value = e instanceof Error ? e.message : '提交盘点失败'
  }
}

function resetFlow() {
  completed.value = false
  selectedExecutionId.value = ''
  selectedCountNo.value = ''
  expectedQuantity.value = 0
  countedQuantityText.value = ''
  // 清空操作键：下次点任务会铸新键，保证新操作 ≠ 旧键。
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
  <AppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">盘点</h1>
      </div>
    </template>

    <!-- 成功结果态 -->
    <Result
      v-if="completed"
      status="success"
      title="盘点已提交"
      :description="selectedCountNo ? `盘点 ${selectedCountNo}` : undefined"
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
    </Result>

    <div v-else class="space-y-4 p-4">
      <ScanBar
        placeholder="扫描库位"
        :active="scanActive"
        @scan="onScan"
      />

      <p
        v-if="error"
        data-testid="error-banner"
        class="rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive"
      >
        盘点任务加载失败，请下拉重试或检查网络。
      </p>

      <div
        v-if="showEmpty"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无盘点任务
      </div>

      <div v-else class="overflow-hidden rounded-lg border border-border">
        <ListRow
          v-for="execution in executions"
          :key="execution.countExecutionId"
          :title="`盘点 ${execution.countNo ?? ''}`"
          :subtitle="`SKU ${execution.skuCode ?? ''} · 库位 ${execution.locationCode ?? ''} · 预期 ${execution.expectedQuantity ?? 0} · ${countExecutionStatusLabel(execution.status)}`"
          @select="selectExecution(execution.countExecutionId, execution.countNo, execution.expectedQuantity)"
        />
      </div>
    </div>

    <!-- 完成盘点确认抽屉 -->
    <BottomSheet
      :open="sheetOpen"
      title="完成盘点"
      @update:open="(v) => (sheetOpen = v)"
    >
      <div class="space-y-4">
        <p v-if="selectedCountNo" class="text-sm text-muted-foreground">
          盘点 {{ selectedCountNo }}
        </p>
        <p class="text-xs text-muted-foreground">{{ countStepHint }}</p>

        <label class="block space-y-2">
          <span class="text-sm font-medium text-foreground">预期数量</span>
          <input
            :value="expectedQuantity"
            data-testid="expected-quantity"
            type="number"
            readonly
            class="min-h-touch w-full rounded-lg border border-border bg-muted px-3 text-base text-muted-foreground"
          >
        </label>

        <label class="block space-y-2">
          <span class="text-sm font-medium text-foreground">实盘数量</span>
          <input
            v-model="countedQuantityText"
            data-testid="counted-quantity"
            type="number"
            inputmode="numeric"
            min="0"
            placeholder="请输入实盘数量"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base text-foreground"
          >
        </label>

        <p v-if="validCount" class="text-sm text-muted-foreground">
          差异 {{ variance > 0 ? `+${variance}` : variance }}
        </p>

        <p v-if="submitError" class="text-sm text-destructive">{{ submitError }}</p>

        <div class="space-y-2 pt-2">
          <button
            type="button"
            data-testid="confirm-complete"
            :disabled="completePending || !validCount"
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
    </BottomSheet>
  </AppShellMobile>
</template>
