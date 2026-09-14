<script setup lang="ts">
/**
 * PDA 交班录入：选班次/班组 → 填在制清点/未完工单/遗留问题 → 拍照加附件 → 提交。
 *
 * 交班人身份**不在这个页面组装**：网关 `CreateBusinessConsoleMesShiftHandoverEndpoint`
 * 从认证 principal 注入 outgoingUserId 并去 MasterData 解析显示名。页面只报「谁在操作」
 * 给操作工看，不把它塞进请求体。
 */
import { shiftHandoverFlow, type ShiftHandoverCtx } from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvMobileButton,
  NvMobileResult,
  NvNoticeBar,
  NvPicker,
  type PickerOption,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import RetryableListError from '@/components/RetryableListError.vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useNonIdempotentWriteResult } from '@/composables/useNonIdempotentWriteResult'
import {
  useShiftHandoverDirectory,
  useShiftHandoverSubmission,
  type ShiftHandoverAttachment,
  type ShiftHandoverOpenIssue,
  type ShiftHandoverUnfinishedWorkOrder,
  type ShiftHandoverWipItem,
} from '@/composables/useBusinessShiftHandover'
import ShiftHandoverEntryForm from './components/ShiftHandoverEntryForm.vue'
import ShiftHandoverPhotoCapture from './components/ShiftHandoverPhotoCapture.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '交班',
  },
})

const router = useRouter()
const directory = useShiftHandoverDirectory()
const submission = useShiftHandoverSubmission()

const shiftId = ref('')
const teamId = ref('')
const wipItems = ref<ShiftHandoverWipItem[]>([])
const unfinishedWorkOrders = ref<ShiftHandoverUnfinishedWorkOrder[]>([])
const openIssues = ref<ShiftHandoverOpenIssue[]>([])
const attachments = ref<ShiftHandoverAttachment[]>([])
const reviewed = ref(false)

const shiftPickerOpen = ref(false)
const teamPickerOpen = ref(false)

// 幂等键：一次交班意图只生成一次，重试沿用同一把键（服务端 CodeAllocator 按它去重）。
const idempotencyKey = ref(makeIdempotencyKey())

const ctx = reactive<ShiftHandoverCtx>({
  shiftId: undefined,
  teamId: undefined,
  reviewed: false,
  submitted: false,
})

const shiftOptions = computed<PickerOption[]>(() =>
  directory.shiftOptions.value.map((option) => ({ label: option.label, value: option.value })),
)
const teamOptions = computed<PickerOption[]>(() =>
  directory.teamOptions.value.map((option) => ({ label: option.label, value: option.value })),
)
const selectedShift = computed(() =>
  directory.shiftOptions.value.find((option) => option.value === shiftId.value),
)
const selectedTeam = computed(() =>
  directory.teamOptions.value.find((option) => option.value === teamId.value),
)

const currentStep = computed(() => {
  ctx.shiftId = shiftId.value || undefined
  ctx.teamId = teamId.value || undefined
  ctx.reviewed = reviewed.value
  return shiftHandoverFlow.currentStep(ctx).id
})
const progress = computed(() => shiftHandoverFlow.progress(ctx))

/**
 * 开工阻断原因。交班需要两个权限，缺哪个说哪个——只说「无权限」会让班组长不知道去要哪一个。
 */
const blocker = computed(() => {
  if (!submission.hasScope.value) return '缺少组织或环境范围，无法交班。请重新登录后重试。'
  if (!submission.canManage.value)
    return '当前账号没有交接班管理权限（business.mes.handovers.manage）。'
  if (!directory.enabled.value)
    return '当前账号没有主数据读取权限（business.masterdata.resources.read），无法选择班次与班组。'
  return ''
})

const detailCount = computed(
  () =>
    wipItems.value.length +
    unfinishedWorkOrders.value.length +
    openIssues.value.length +
    attachments.value.length,
)

const write = useNonIdempotentWriteResult({
  failureTitle: '交班提交失败',
  verifyListLabel: '待接班交接单',
  verifyVerb: '提交',
  onVerify: () => {
    router.push('/mes/handovers').catch(() => {})
  },
  // 服务端按 idempotencyKey 去重（MesCodingService.AllocateAsync），重试不会重复建单。
  idempotent: true,
})

const submitting = ref(false)

async function submit() {
  if (blocker.value || submitting.value) return
  if (!shiftId.value || !teamId.value || !reviewed.value) return
  submitting.value = true
  const ok = await write.run(() =>
    submission.createHandover({
      shiftId: shiftId.value,
      teamId: teamId.value,
      teamName: selectedTeam.value?.label,
      idempotencyKey: idempotencyKey.value,
      wipItems: wipItems.value,
      unfinishedWorkOrders: unfinishedWorkOrders.value,
      openIssues: openIssues.value,
      attachments: attachments.value,
    }),
  )
  submitting.value = false
  ctx.submitted = ok
}

function startAnother() {
  shiftId.value = ''
  teamId.value = ''
  wipItems.value = []
  unfinishedWorkOrders.value = []
  openIssues.value = []
  attachments.value = []
  reviewed.value = false
  ctx.submitted = false
  // 新的一次交班意图 = 新的幂等键。
  idempotencyKey.value = makeIdempotencyKey()
  write.reset()
}

function goHome() {
  router.push('/').catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <button
          type="button"
          aria-label="返回"
          class="text-sm text-muted-foreground"
          @click="goHome"
        >
          返回
        </button>
        <h1 class="text-lg font-semibold text-foreground">交班</h1>
        <span class="ml-auto text-xs text-muted-foreground">
          第
          {{ progress.completed + 1 > progress.total ? progress.total : progress.completed + 1 }}/{{
            progress.total
          }}
          步
        </span>
      </div>
    </template>

    <NvMobileResult
      v-if="write.phase.value === 'success'"
      status="success"
      title="交班已提交"
      description="接班人可在「接班」里看到这张交接单。"
    >
      <template #actions>
        <NvMobileButton variant="primary" block data-testid="start-another" @click="startAnother"
          >再交一班</NvMobileButton
        >
        <NvMobileButton
          variant="outline"
          block
          data-testid="go-handovers"
          @click="router.push('/mes/handovers').catch(() => {})"
          >查看待接班</NvMobileButton
        >
        <NvMobileButton variant="text" block @click="goHome">返回工作台</NvMobileButton>
      </template>
    </NvMobileResult>

    <NvMobileResult
      v-else-if="write.phase.value === 'error'"
      status="error"
      :title="write.errorTitle.value"
      :description="write.errorDescription.value"
    >
      <template #actions>
        <NvMobileButton
          v-if="write.canRetry.value"
          variant="primary"
          block
          data-testid="retry-submit"
          @click="write.retry()"
          >返回修改后重试</NvMobileButton
        >
        <NvMobileButton variant="outline" block data-testid="verify-submit" @click="write.verify()"
          >去待接班列表核实</NvMobileButton
        >
      </template>
    </NvMobileResult>

    <div v-else class="space-y-4 p-4">
      <NvNoticeBar v-if="blocker" tone="danger" data-testid="handover-blocker">{{
        blocker
      }}</NvNoticeBar>

      <RetryableListError
        v-if="directory.error.value"
        :error="directory.error.value"
        :pending="directory.pending.value"
        fallback="班次与班组目录加载失败，请重试。"
        test-id="handover-directory-error"
        @retry="() => directory.refresh()"
      />

      <!-- 步骤 1：班次与班组 -->
      <section class="space-y-2" data-testid="shift-team-step">
        <h2 class="text-sm font-medium text-muted-foreground">交接的班次与班组</h2>
        <button
          type="button"
          data-testid="shift-cell"
          class="min-h-touch flex w-full items-center justify-between rounded-xl border border-border bg-card px-3.5 text-[15px]"
          :disabled="Boolean(blocker)"
          @click="shiftPickerOpen = true"
        >
          <span class="text-muted-foreground">班次</span>
          <span class="font-medium text-foreground">{{ selectedShift?.label ?? '点击选择' }}</span>
        </button>
        <button
          type="button"
          data-testid="team-cell"
          class="min-h-touch flex w-full items-center justify-between rounded-xl border border-border bg-card px-3.5 text-[15px]"
          :disabled="Boolean(blocker)"
          @click="teamPickerOpen = true"
        >
          <span class="text-muted-foreground">班组</span>
          <span class="font-medium text-foreground">{{ selectedTeam?.label ?? '点击选择' }}</span>
        </button>
        <p
          v-if="!directory.pending.value && shiftOptions.length === 0"
          data-testid="no-shift-options"
          class="text-sm text-muted-foreground"
        >
          主数据里没有可选班次，请联系管理员维护班次主数据后再交班。
        </p>
      </section>

      <!-- 步骤 2：三类明细 + 附件 -->
      <template v-if="currentStep !== 'selectShiftTeam'">
        <ShiftHandoverEntryForm
          v-model:wip-items="wipItems"
          v-model:unfinished-work-orders="unfinishedWorkOrders"
          v-model:open-issues="openIssues"
        />

        <ShiftHandoverPhotoCapture
          v-model:attachments="attachments"
          :upload="submission.uploadAttachment"
          :disabled="Boolean(blocker)"
          :disabled-reason="blocker"
        />

        <NvMobileButton
          v-if="!reviewed"
          variant="primary"
          block
          data-testid="confirm-details"
          @click="reviewed = true"
          >明细已确认，去提交</NvMobileButton
        >
      </template>

      <!-- 步骤 3：提交 -->
      <section v-if="currentStep === 'submit'" class="space-y-2" data-testid="submit-step">
        <div class="rounded-xl border border-border bg-card p-3 text-sm">
          <p class="font-medium text-foreground">
            {{ selectedShift?.label }} · {{ selectedTeam?.label }}
          </p>
          <p class="text-muted-foreground">
            在制 {{ wipItems.length }} 项 · 未完工单 {{ unfinishedWorkOrders.length }} 张 · 遗留问题
            {{ openIssues.length }} 条 · 照片 {{ attachments.length }} 张
          </p>
          <p v-if="detailCount === 0" class="mt-1 text-xs text-muted-foreground">
            本次交班没有登记任何明细——这在业务上是合法的，接班人会看到一张空明细的交接单。
          </p>
        </div>
        <NvMobileButton
          variant="primary"
          size="lg"
          block
          data-testid="submit-handover"
          :disabled="Boolean(blocker) || submitting"
          @click="submit"
          >{{ submitting ? '提交中…' : '提交交班' }}</NvMobileButton
        >
        <NvMobileButton variant="text" block data-testid="back-to-details" @click="reviewed = false"
          >返回修改明细</NvMobileButton
        >
      </section>

      <NvPicker
        v-model="shiftId"
        v-model:open="shiftPickerOpen"
        :options="shiftOptions"
        title="选择班次"
      />
      <NvPicker
        v-model="teamId"
        v-model:open="teamPickerOpen"
        :options="teamOptions"
        title="选择班组"
      />
    </div>
  </NvAppShellMobile>
</template>
