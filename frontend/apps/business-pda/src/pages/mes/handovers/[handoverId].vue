<script setup lang="ts">
/**
 * PDA 接班确认：看完三类明细 + 附件再确认接班。
 *
 * 接班人身份**不在请求体里**——网关 `AcceptBusinessConsoleMesShiftHandoverEndpoint`
 * 从认证 principal 注入 incomingUserId 并解析显示名，请求体已经是空的（#3328）。
 *
 * 确认弹框只负责「问一句」：`NvMobileDialog` 的确认键按下即无条件关框（它 emit 完 confirm
 * 就 emit `update:open(false)`，不看 preventDefault），所以失败原因和 pending 态一律渲染在
 * 页面上而不是框里——把它们写进框里在真 UI 上根本走不到。
 */
import { shiftHandoverStatusLabel } from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvMobileButton,
  NvMobileDialog,
  NvMobileResult,
  NvMobileTag,
  NvNoticeBar,
} from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import RetryableListError from '@/components/RetryableListError.vue'
import { useNonIdempotentWriteResult } from '@/composables/useNonIdempotentWriteResult'
import {
  formatHandoverTimestamp,
  incomingUserLabel,
  outgoingUserLabel,
  useMesShiftHandoverDetail,
  useShiftHandoverAttachmentViewer,
  useShiftHandoverDirectoryLabels,
} from '@/composables/useBusinessShiftHandover'
import ShiftHandoverDetailSections from '../components/ShiftHandoverDetailSections.vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '接班确认',
  },
})

const route = useRoute()
const router = useRouter()

const handoverId = computed(() => {
  const value = (route.params as Record<string, unknown>).handoverId
  return typeof value === 'string' ? value.trim() : ''
})

const detail = useMesShiftHandoverDetail(handoverId)
const viewer = useShiftHandoverAttachmentViewer()
const { resolveShiftLabel, resolveTeamLabel } = useShiftHandoverDirectoryLabels()

const confirmOpen = ref(false)

const blocker = computed(() => {
  if (!detail.hasScope.value) return '缺少组织或环境范围，未发起查询。请重新登录后重试。'
  if (!detail.canRead.value) return '当前账号不能查看交接班记录。请联系班组长或管理员开通。'
  return ''
})

const createdAtText = computed(() => formatHandoverTimestamp(detail.detail.value?.createdAtUtc))
const acceptedAtText = computed(() => formatHandoverTimestamp(detail.detail.value?.acceptedAtUtc))

const isOpenHandover = computed(
  () => (detail.detail.value?.handoverStatus ?? '').toLowerCase() === 'open',
)

/** 接班按钮的阻断原因；说清是哪一条，别让操作工对着一个灰按钮猜。 */
const acceptBlocker = computed(() => {
  if (blocker.value) return blocker.value
  if (!detail.detail.value) return '交接单详情尚未加载完成。'
  if (!detail.canManage.value)
    return '当前账号只能查看交接单，没有接班权限。请联系班组长或管理员开通。'
  if (!isOpenHandover.value) return '这张交接单已被接班，无需重复确认。'
  return ''
})

const write = useNonIdempotentWriteResult({
  failureTitle: '接班失败',
  verifyListLabel: '待接班交接单',
  verifyVerb: '接班',
  onVerify: () => {
    void detail.refresh()
  },
  // ShiftHandover.Accept 首句就是「已接班则直接返回」的幂等早退，重试不会重复写接班人。
  idempotent: true,
})

const accepting = ref(false)

async function acceptHandover() {
  if (acceptBlocker.value || accepting.value) return
  accepting.value = true
  await write.run(() => detail.acceptHandover())
  accepting.value = false
}

function backToList() {
  router.push('/mes/handovers').catch(() => {})
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
          @click="backToList"
        >
          返回
        </button>
        <h1 class="text-lg font-semibold text-foreground">接班确认</h1>
      </div>
    </template>

    <NvMobileResult
      v-if="write.phase.value === 'success'"
      status="success"
      title="接班已确认"
      description="接班人已写入这张交接单。"
    >
      <template #actions>
        <NvMobileButton variant="primary" block data-testid="back-to-list" @click="backToList"
          >返回待接班列表</NvMobileButton
        >
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
          data-testid="retry-accept"
          @click="write.retry()"
          >返回重试</NvMobileButton
        >
        <NvMobileButton variant="outline" block data-testid="verify-accept" @click="write.verify()"
          >刷新核实这张单的状态</NvMobileButton
        >
      </template>
    </NvMobileResult>

    <div v-else class="space-y-4 p-4">
      <NvNoticeBar v-if="blocker" tone="danger" data-testid="detail-blocker">{{
        blocker
      }}</NvNoticeBar>

      <RetryableListError
        v-if="detail.error.value || detail.hasFailedResponse.value"
        :error="detail.error.value ?? '班次交接详情服务未成功返回'"
        :pending="detail.pending.value"
        fallback="交接单详情加载失败，请重试。"
        test-id="handover-detail-error"
        @retry="() => detail.refresh()"
      />

      <section
        v-if="detail.detail.value"
        class="rounded-xl border border-border bg-card p-3"
        data-testid="handover-summary"
      >
        <div class="flex items-center gap-2">
          <h2 class="min-w-0 flex-1 truncate text-base font-semibold text-foreground">
            {{ detail.detail.value.handoverId?.trim() || '无单号' }}
          </h2>
          <NvMobileTag size="sm" :variant="isOpenHandover ? 'warning' : 'success'">{{
            shiftHandoverStatusLabel(detail.detail.value.handoverStatus)
          }}</NvMobileTag>
        </div>
        <p class="mt-1 truncate text-sm text-muted-foreground">
          {{ detail.detail.value.teamName?.trim() || resolveTeamLabel(detail.detail.value.teamId) }}
          · {{ resolveShiftLabel(detail.detail.value.shiftId) }}
        </p>
        <p class="text-sm text-muted-foreground">
          交班 {{ outgoingUserLabel(detail.detail.value) }} · 接班
          {{ incomingUserLabel(detail.detail.value) }}
        </p>
        <p v-if="createdAtText" class="text-xs text-muted-foreground">
          交班时间 {{ createdAtText }}
        </p>
        <p v-if="acceptedAtText" class="text-xs text-muted-foreground">
          接班时间 {{ acceptedAtText }}
        </p>
      </section>

      <ShiftHandoverDetailSections
        :loaded="Boolean(detail.detail.value)"
        :wip-items="detail.wipItems.value"
        :unfinished-work-orders="detail.unfinishedWorkOrders.value"
        :open-issues="detail.openIssues.value"
        :attachments="detail.attachments.value"
        :opening-attachment-file-id="viewer.openingFileId.value"
        :attachment-error="viewer.error.value"
        @open-attachment="viewer.openAttachment"
      />

      <div class="space-y-2">
        <NvMobileButton
          variant="primary"
          size="lg"
          block
          data-testid="open-accept-confirm"
          :disabled="Boolean(acceptBlocker) || accepting"
          @click="confirmOpen = true"
          >{{ accepting ? '接班提交中…' : '确认接班' }}</NvMobileButton
        >
        <p v-if="acceptBlocker" data-testid="accept-blocker" class="text-sm text-muted-foreground">
          {{ acceptBlocker }}
        </p>
      </div>
    </div>

    <NvMobileDialog
      v-model:open="confirmOpen"
      title="确认接班"
      description="确认后将把你写为这张交接单的接班人，且不可撤销。"
      confirm-text="确认接班"
      @confirm="acceptHandover"
    />
  </NvAppShellMobile>
</template>
