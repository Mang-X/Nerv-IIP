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
  if (!canRead.value) return '当前账号没有交接班读取权限（business.mes.handovers.read）。'
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

function rowSubtitle(row: ShiftHandoverRow) {
  return `交班 ${outgoingUserLabel(row)} · 接班 ${incomingUserLabel(row)}`
}

function rowTitle(row: ShiftHandoverRow) {
  // 班组名优先用交班时点的快照（teamName），目录改名不会改写历史交接单上的称呼。
  return row.teamName?.trim() || resolveTeamLabel(row.teamId)
}

function rowCounts(row: ShiftHandoverRow) {
  return `在制 ${row.wipItemCount ?? 0} · 未完工单 ${row.unfinishedWorkOrderCount ?? 0} · 遗留 ${row.openIssueDetailCount ?? 0}`
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
            <p class="truncate text-xs text-muted-foreground">
              班次 {{ resolveShiftLabel(row.shiftId) }} · {{ rowCounts(row) }}
            </p>
          </template>
          <template #trailing>
            <NvMobileTag
              size="sm"
              :variant="(row.handoverStatus ?? '').toLowerCase() === 'open' ? 'warning' : 'success'"
            >
              {{ shiftHandoverStatusLabel(row.handoverStatus) }}
            </NvMobileTag>
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
