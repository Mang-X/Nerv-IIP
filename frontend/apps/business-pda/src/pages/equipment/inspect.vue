<script setup lang="ts">
import { useBusinessMaintenance } from '@/composables/useBusinessMaintenance'
import {
  inspectionFlow,
  inspectionResultLabel,
  inspectionResultLabels,
  type InspectCtx,
} from '@nerv-iip/business-core'
import { AppShellMobile, ListRow, Result, ScanBar } from '@nerv-iip/ui-mobile'
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'

// 保养计划行（composable 的 plans 为 unknown[]，此处收窄为业务字段）。
interface PlanRow {
  planId?: string
  deviceAssetId?: string
  planCode?: string
  interval?: string
  startsOn?: string
}

// 点检记录行。
interface InspectionRow {
  inspectionId?: string
  planId?: string | null
  workOrderId?: string | null
  result?: string
  inspectedAtUtc?: string
}

definePage({
  meta: {
    requiresAuth: true,
    title: '点检',
  },
})

const router = useRouter()

const maintenance = useBusinessMaintenance()
const {
  plansPending,
  plansError,
  plansTotal,
  loadMorePlans,
  recordInspection,
  recordPending,
  inspectionsPending,
  inspectionsError,
} = maintenance

// plans/inspections 收窄为业务行类型。
const allPlans = computed<PlanRow[]>(() => maintenance.plans.value as PlanRow[])
const inspections = computed<InspectionRow[]>(() => maintenance.inspections.value as InspectionRow[])

// 扫码/手输关键字 → 对**已加载**的 plans 做客户端过滤（facade 无 keyword/device 查询参数）。
const scanKeyword = ref('')
const plans = computed<PlanRow[]>(() => {
  const kw = scanKeyword.value.trim().toLowerCase()
  if (!kw) return allPlans.value
  return allPlans.value.filter((p) => {
    const code = (p.planCode ?? '').toLowerCase()
    const device = (p.deviceAssetId ?? '').toLowerCase()
    return code.includes(kw) || device.includes(kw)
  })
})

// 已加载条数 / 服务端总数：facade 仅支持 org/env/skip/take，关键字命中第一页之外时
// 不能把"已加载页内未命中"当作"不存在"——还有更多页可加载（loadMorePlans）。
const loadedPlans = computed(() => allPlans.value.length)
const hasMorePlans = computed(() => loadedPlans.value < plansTotal.value)

// 点检表单 = inspectionFlow 的上下文（selectPlan → enterResult → record）。
const form = reactive<InspectCtx>({
  planId: '',
  result: '',
})

// 结果选项（中文经 inspectionResultLabel）。
const resultOptions = Object.keys(inspectionResultLabels)

// 流程驱动校验：planId + result 必填（完成前两步）。
const valid = computed(() => inspectionFlow.progress(form).completed >= 2)

type Phase = 'form' | 'success' | 'error'
const phase = ref<Phase>('form')
const submitError = ref('')

// ScanBar 在浮层（成功/失败 Result）展示时停止抢焦。
const scanActive = computed(() => phase.value === 'form')

function onScan(value: string) {
  // 扫设备码/计划号 → 客户端过滤已加载的保养计划列表。
  scanKeyword.value = value
}

function selectPlan(planId: string | undefined) {
  if (!planId) return
  form.planId = planId
  // 切换计划时清空已选结果，避免沿用上一计划的结果。
  form.result = ''
}

function chooseResult(value: string) {
  form.result = value
}

async function submit() {
  if (!valid.value || recordPending.value) return
  submitError.value = ''
  try {
    await recordInspection({
      planId: form.planId as string,
      result: form.result as string,
    })
    phase.value = 'success'
  } catch (e) {
    submitError.value = e instanceof Error ? e.message : '点检记录失败'
    phase.value = 'error'
  }
}

function resetForm() {
  // 成功后清空，避免重复记录相同点检（端点无服务端幂等）。
  form.planId = ''
  form.result = ''
  submitError.value = ''
  phase.value = 'form'
}

function goBack() {
  router.push('/').catch(() => {})
}

function retry() {
  phase.value = 'form'
}

function planSubtitle(item: { deviceAssetId?: string, interval?: string }) {
  const parts: string[] = []
  if (item.deviceAssetId) parts.push(`设备 ${item.deviceAssetId}`)
  if (item.interval) parts.push(`周期 ${item.interval}`)
  return parts.join(' · ')
}

function inspectionTitle(item: { planId?: string | null, workOrderId?: string | null }) {
  if (item.planId) return `计划 ${item.planId}`
  if (item.workOrderId) return `工单 ${item.workOrderId}`
  return '点检记录'
}

function inspectionSubtitle(item: { result?: string, inspectedAtUtc?: string }) {
  const parts = [`结果 ${inspectionResultLabel(item.result)}`]
  if (item.inspectedAtUtc) {
    parts.push(new Date(item.inspectedAtUtc).toLocaleString('zh-CN'))
  }
  return parts.join(' · ')
}
</script>

<template>
  <AppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">点检</h1>
      </div>
    </template>

    <!-- 成功 / 失败：离场态（清空表单，防重复记录） -->
    <Result
      v-if="phase === 'success'"
      status="success"
      title="点检已记录"
      description="点检结果已记录。"
    >
      <template #actions>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="resetForm"
        >
          继续点检
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="goBack"
        >
          返回
        </button>
      </template>
    </Result>

    <Result
      v-else-if="phase === 'error'"
      status="error"
      title="点检记录失败"
      :description="submitError"
    >
      <template #actions>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="retry"
        >
          重试
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="goBack"
        >
          返回
        </button>
      </template>
    </Result>

    <div v-else class="space-y-6 p-4">
      <!-- 新建点检 -->
      <section class="space-y-3">
        <h2 class="text-sm font-medium text-muted-foreground">新建点检</h2>

        <ScanBar placeholder="扫描设备码或计划号" :active="scanActive" @scan="onScan" />

        <!-- 步骤 1：选择保养计划 -->
        <div class="space-y-2">
          <p class="text-sm text-foreground">选择保养计划</p>

          <p
            v-if="plansError"
            data-testid="plans-error"
            class="rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive"
          >
            保养计划加载失败，请稍后重试。
          </p>

          <div v-else-if="plansPending" class="px-4 py-6 text-center text-sm text-muted-foreground">
            加载中…
          </div>

          <div
            v-else-if="allPlans.length === 0"
            class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            暂无保养计划
          </div>

          <!-- 已加载页内未命中，但服务端仍有更多页：诚实提示 + 加载更多，不做死路。 -->
          <div
            v-else-if="plans.length === 0 && hasMorePlans"
            data-testid="plans-partial-no-match"
            class="space-y-3 rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            <p>在已加载的 {{ loadedPlans }} 条计划中未匹配（共 {{ plansTotal }} 条）。</p>
            <button
              data-testid="load-more-plans"
              type="button"
              :disabled="plansPending"
              class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground disabled:opacity-60"
              @click="loadMorePlans"
            >
              加载更多
            </button>
          </div>

          <!-- 全部计划已加载且仍无命中：确定性"未找到"。 -->
          <div
            v-else-if="plans.length === 0"
            class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            未找到匹配的保养计划
          </div>

          <div v-else class="overflow-hidden rounded-lg border border-border">
            <ListRow
              v-for="item in plans"
              :key="item.planId"
              data-testid="plan-option"
              :title="item.planCode ?? '未命名计划'"
              :subtitle="planSubtitle(item)"
              :class="form.planId === item.planId ? 'bg-brand/10' : undefined"
              @select="selectPlan(item.planId)"
            />
          </div>
        </div>

        <!-- 步骤 2：选择结果（仅在已选计划后出现） -->
        <div v-if="form.planId" class="space-y-2">
          <p class="text-sm text-foreground">点检结果</p>
          <div class="grid grid-cols-2 gap-3">
            <button
              v-for="r in resultOptions"
              :key="r"
              :data-testid="`result-${r}`"
              type="button"
              class="min-h-touch w-full rounded-lg border text-base font-medium"
              :class="form.result === r
                ? 'border-brand bg-brand/10 text-foreground'
                : 'border-border bg-card text-foreground'"
              @click="chooseResult(r)"
            >
              {{ inspectionResultLabel(r) }}
            </button>
          </div>
        </div>

        <!-- 步骤 3：提交 -->
        <button
          data-testid="submit"
          type="button"
          :disabled="!valid || recordPending"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
          @click="submit"
        >
          {{ recordPending ? '记录中…' : '提交点检' }}
        </button>
      </section>

      <!-- 近期点检记录 -->
      <section class="space-y-2">
        <h2 class="text-sm font-medium text-muted-foreground">近期点检记录</h2>

        <p
          v-if="inspectionsError"
          data-testid="inspections-error"
          class="rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive"
        >
          点检记录加载失败，请稍后重试。
        </p>

        <div v-else-if="inspectionsPending" class="px-4 py-6 text-center text-sm text-muted-foreground">
          加载中…
        </div>

        <div
          v-else-if="inspections.length === 0"
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          暂无点检记录
        </div>

        <div v-else class="overflow-hidden rounded-lg border border-border">
          <ListRow
            v-for="item in inspections"
            :key="item.inspectionId"
            :title="inspectionTitle(item)"
            :subtitle="inspectionSubtitle(item)"
            :interactive="false"
          />
        </div>
      </section>
    </div>
  </AppShellMobile>
</template>
