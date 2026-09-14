<script setup lang="ts">
/**
 * PDA 接班：待接班交接单列表。点进去看三类明细 + 附件，再确认接班。
 */
import { shiftHandoverStatusLabel } from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvListRow,
  NvMobileEmpty,
  NvMobileTag,
  NvMobileTabs,
  NvNoticeBar,
  type MobileTabItem,
} from '@nerv-iip/ui-mobile'
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import ListScopeMeta from '@/components/ListScopeMeta.vue'
import RetryableListError from '@/components/RetryableListError.vue'
import {
  formatHandoverTimestamp,
  HANDOVER_OPEN_STATUS_FILTER,
  incomingUserLabel,
  outgoingUserLabel,
  useMesShiftHandovers,
  useShiftHandoverDirectoryLabels,
  type ShiftHandoverRow,
} from '@/composables/useBusinessShiftHandover'

definePage({
  meta: {
    requiresAuth: true,
    title: '接班',
  },
})

const router = useRouter()
const {
  filters,
  canRead,
  canManage,
  hasScope,
  handovers,
  total,
  pending,
  error,
  lastUpdatedAt,
  hasSuccessfulResponse,
  hasFailedResponse,
  refresh,
} = useMesShiftHandovers()
const { resolveShiftLabel, resolveTeamLabel } = useShiftHandoverDirectoryLabels()

const tabs: MobileTabItem[] = [
  { value: HANDOVER_OPEN_STATUS_FILTER, label: '待接班' },
  { value: 'accepted', label: '已接班' },
]
const activeTab = computed<string>({
  get: () => filters.status ?? HANDOVER_OPEN_STATUS_FILTER,
  set: (value) => {
    filters.status = value
  },
})

const blocker = computed(() => {
  if (!hasScope.value) return '缺少组织或环境范围，未发起查询。请重新登录后重试。'
  if (!canRead.value) return '当前账号不能查看交接班记录。请联系班组长或管理员开通。'
  return ''
})

const showEmpty = computed(
  () =>
    canRead.value &&
    !pending.value &&
    !error.value &&
    hasSuccessfulResponse.value &&
    handovers.value.length === 0,
)

/**
 * 行标题用交接单号。
 *
 * 同一班组同一班次一天内会交多次班，用班组名当标题时列表上两行**在屏上完全无法区分**
 * ——操作工点进去之前不知道哪张是哪张。单号是这批数据里唯一天然互异的业务字段，
 * 也与 PDA 其它列表（工单号当标题）一致。班组/班次降到副标题。
 */
function rowTitle(row: ShiftHandoverRow) {
  return row.handoverId?.trim() || '无单号'
}

function rowSubtitle(row: ShiftHandoverRow) {
  // 班组名优先用交班时点的快照（teamName），目录改名不会改写历史交接单上的称呼。
  const team = row.teamName?.trim() || resolveTeamLabel(row.teamId)
  return `${team} · ${resolveShiftLabel(row.shiftId)}`
}

function rowParties(row: ShiftHandoverRow) {
  return `交班 ${outgoingUserLabel(row)} · 接班 ${incomingUserLabel(row)}`
}

function rowCounts(row: ShiftHandoverRow) {
  return `在制 ${row.wipItemCount ?? 0} · 未完工单 ${row.unfinishedWorkOrderCount ?? 0} · 遗留 ${row.openIssueDetailCount ?? 0}`
}

function isAccepted(row: ShiftHandoverRow) {
  return (row.handoverStatus ?? '').toLowerCase() === 'accepted'
}

/** 与状态配对的那个时点：待接班看交班时间，已接班看接班时间。解不出就不渲染。 */
function rowTimestamp(row: ShiftHandoverRow) {
  return formatHandoverTimestamp(isAccepted(row) ? row.acceptedAtUtc : row.createdAtUtc, true)
}

function openDetail(row: ShiftHandoverRow) {
  const handoverId = row.handoverId?.trim()
  if (!handoverId) return
  router.push(`/mes/handovers/${encodeURIComponent(handoverId)}`).catch(() => {})
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
          @click="router.push('/').catch(() => {})"
        >
          返回
        </button>
        <h1 class="text-lg font-semibold text-foreground">接班</h1>
        <button
          v-if="canManage"
          type="button"
          data-testid="go-handover-entry"
          class="ml-auto text-sm text-brand"
          @click="router.push('/mes/handover').catch(() => {})"
        >
          去交班
        </button>
      </div>
    </template>

    <div class="space-y-3 p-4">
      <NvNoticeBar v-if="blocker" tone="danger" data-testid="handovers-blocker">{{
        blocker
      }}</NvNoticeBar>

      <NvMobileTabs v-model="activeTab" :items="tabs" data-testid="handover-status-tabs" />

      <ListScopeMeta
        :scope="hasScope ? '当前登录组织 / 当前业务环境' : '组织/环境范围未就绪'"
        source="班次交接服务（组织/环境范围，按所选状态过滤）"
        :loaded="handovers.length"
        :total="total"
        :updated-at="lastUpdatedAt"
        :failed="hasFailedResponse || Boolean(error)"
        failure-explanation="班次交接服务未成功返回，请刷新重试。"
        :empty="!canRead || showEmpty"
        :empty-explanation="
          canRead
            ? '当前组织/环境范围内没有该状态的交接单。'
            : '没有交接班读取权限或范围未就绪，未发起查询。'
        "
      />

      <RetryableListError
        v-if="error || hasFailedResponse"
        :error="error ?? '班次交接服务未成功返回'"
        :pending="pending"
        fallback="交接单加载失败，请重试。"
        test-id="handovers-error"
        @retry="() => refresh()"
      />

      <div
        v-if="handovers.length"
        class="overflow-hidden rounded-xl border border-border"
        data-testid="handover-rows"
      >
        <NvListRow
          v-for="row in handovers"
          :key="row.handoverId"
          :title="rowTitle(row)"
          :subtitle="rowSubtitle(row)"
          @select="openDetail(row)"
        >
          <template #meta>
            <p class="truncate text-xs text-muted-foreground">{{ rowParties(row) }}</p>
            <p class="truncate text-xs text-muted-foreground">{{ rowCounts(row) }}</p>
          </template>
          <template #trailing>
            <div class="flex shrink-0 flex-col items-end gap-1">
              <NvMobileTag size="sm" :variant="isAccepted(row) ? 'success' : 'warning'">
                {{ shiftHandoverStatusLabel(row.handoverStatus) }}
              </NvMobileTag>
              <span
                v-if="rowTimestamp(row)"
                class="text-[11px] tabular-nums text-muted-foreground"
                >{{ rowTimestamp(row) }}</span
              >
            </div>
          </template>
        </NvListRow>
      </div>

      <NvMobileEmpty
        v-else-if="showEmpty"
        data-testid="handovers-empty"
        description="当前组织/环境范围内没有该状态的交接单。"
      />
    </div>
  </NvAppShellMobile>
</template>
