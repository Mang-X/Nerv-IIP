<script setup lang="ts">
import {
  NvRingGauge,
  NvScreenPanel,
  NvScreenScrollArea,
  NvScreenSegmented,
  NvScreenTabs,
  NvScreenStatusLight,
  NvScreenStatusTag,
  useScreenData,
} from '@nerv-iip/ui'
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import DeviceDetailModal from '@/components/equipment/DeviceDetailModal.vue'
import { useBackLink } from '@/composables/useBackLink'
import DeviceStatusWall from '@/components/equipment/DeviceStatusWall.vue'
import type {
  DeviceCell,
  DeviceParamsTick,
  EquipmentOverview,
  RepairOrder,
} from '@/data/contracts/equipment'
import { REPAIR_STAGES } from '@/data/contracts/equipment'
import { fetchDeviceParamsTick, fetchEquipmentOverview } from '@/data/fetchers/equipment'
import ScreenLayout from '@/layouts/ScreenLayout.vue'

// 刷新频率分层（MAN-466 大屏轮询调参，观察 #734 Gateway 限流后再降频）：
// 全景/计数/流 10s · 格上参数 5s 快刷（仅视野内设备；真实模式无 historian 时为空）·
// 详情弹窗 3s（弹窗内部）；页面隐藏时 useScreenData 统一暂停轮询、失败保活标 stale。
const scope = useAccessScope()
const backLink = useBackLink(() => ({ to: '/', label: '返回大屏门厅' }))
const {
  data: ov,
  refresh,
  isStale,
  lastUpdated,
} = useScreenData<EquipmentOverview>(
  () => fetchEquipmentOverview(scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 10000 },
)
/** 视野内设备集（墙体虚拟滚动上报）——视野外不请求、不产生数据变化。 */
const visibleIds = ref<string[]>([])
const {
  data: paramsTick,
  start: tickStart,
  stop: tickStop,
} = useScreenData<DeviceParamsTick>(
  () =>
    fetchDeviceParamsTick(
      scope.currentFactoryId,
      scope.persona.workshopIds,
      visibleIds.value.length ? visibleIds.value : undefined,
    ),
  { intervalMs: 5000 },
)
/** 全景设备 + 高频参数合流：视野内参数以 2s tick 为准，视野外保持上帧（冻结）。 */
const devicesLive = computed<DeviceCell[]>(() => {
  const list = ov.value?.devices ?? []
  const tick = paramsTick.value
  if (!tick) return list
  return list.map((d) => (tick[d.id] ? { ...d, params: tick[d.id] } : d))
})
watch(
  () => [scope.currentFactoryId, scope.personaId],
  async () => {
    await refresh()
    // 撞上在途轮询（inFlight 跳过）时补一拍，避免短暂显示旧工厂数据
    if (ov.value && ov.value.factoryId !== scope.currentFactoryId) await refresh()
  },
)

const factoryName = computed(
  () => scope.factories.find((f) => f.id === scope.currentFactoryId)?.name ?? '全部车间',
)

// —— 墙体排列视图（平铺 / 按车间分组 / 按产线行式）——
type WallView = 'flat' | 'workshop' | 'line'
const view = ref<WallView>('flat')
const viewModel = computed<string | number>({
  get: () => view.value,
  set: (v) => {
    view.value = v as WallView
  },
})
const VIEW_OPTIONS = [
  { label: '平铺', value: 'flat' },
  { label: '按车间', value: 'workshop' },
  { label: '按产线', value: 'line' },
]

// —— 设备详情弹窗（点击设备格打开；弹窗内部 3s 轮询取数）——
// 弹窗打开期间暂停墙体 2s 快刷（被遮挡的更新无意义），关闭即恢复
const selectedId = ref<string | null>(null)
function openDevice(d: DeviceCell) {
  selectedId.value = d.id
  tickStop()
}
function closeDetail() {
  selectedId.value = null
  tickStart()
}

// —— 异常与待办：报警(open)/维修(进行中)/PM(到期) 合并事件流，严重度排序。
//    正常工厂日应寥寥数条 —— 空即健康，空态显示「运行平稳」。 ——
interface EventRow {
  key: string
  tone: 'red' | 'amber' | 'cyan'
  time?: string
  text: string
  tag: string
  late?: boolean
}
const events = computed<EventRow[]>(() => {
  const s = ov.value
  if (!s) return []
  const out: EventRow[] = []
  for (const a of s.alarms) {
    if (a.status.startsWith('已恢复')) continue
    out.push({
      key: `al-${a.wo}-${a.name}`,
      tone: a.level === 'sev' ? 'red' : 'amber',
      time: a.time,
      text: `${a.line} · ${a.name}`,
      tag: a.status,
      late: a.level === 'sev',
    })
  }
  for (const r of s.repairs) {
    if (r.stage === '已关闭') continue
    out.push({
      key: r.wo,
      tone: r.overdue ? 'red' : r.blockedBy ? 'amber' : 'cyan',
      time: r.reportedAt,
      text: `${r.wo} · ${r.device} ${r.issue}`,
      tag: r.overdue
        ? `已超期 · ${r.assignee}`
        : r.blockedBy
          ? '待备件'
          : `${r.stage} · ${r.assignee}`,
      late: r.overdue,
    })
  }
  for (const t of s.pmTasks) {
    if (t.state === 'done') continue
    out.push({
      key: `pm-${t.device}-${t.task}`,
      tone: t.state === 'overdue' ? 'amber' : 'cyan',
      text: `PM · ${t.device} ${t.task}`,
      tag: t.due,
      late: t.state === 'overdue',
    })
  }
  const rank = { red: 0, amber: 1, cyan: 2 }
  return out.sort((a, b) => rank[a.tone] - rank[b.tone])
})

// —— 事件分类 tab：每类用各自合适的形态（合并流/报警行/维修状态机/保养台账）——
const evTab = ref<string | number>('all')
const openAlarms = computed(
  () => ov.value?.alarms.filter((a) => !a.status.startsWith('已恢复')) ?? [],
)
const activeRepairs = computed(() => ov.value?.repairs.filter((r) => r.stage !== '已关闭') ?? [])
const duePm = computed(() => ov.value?.pmTasks.filter((t) => t.state !== 'done') ?? [])
const evTabs = computed(() => [
  { label: `全部 ${events.value.length}`, value: 'all' },
  { label: `报警 ${openAlarms.value.length}`, value: 'alarm' },
  { label: `维修 ${activeRepairs.value.length}`, value: 'repair' },
  { label: `保养 ${duePm.value.length}`, value: 'pm' },
])
/** 维修状态机当前阶段序号（步进器用） */
function stageIdx(r: RepairOrder): number {
  return REPAIR_STAGES.indexOf(r.stage)
}

// —— 可靠性四格（MTBF/MTTR 无样本 null → 「—」）——
const relCells = computed(() => {
  const r = ov.value?.reliability
  if (!r) return []
  return [
    { label: 'MTBF（小时）', value: r.mtbfHours === null ? '—' : String(r.mtbfHours) },
    { label: 'MTTR（分钟）', value: r.mttrMinutes === null ? '—' : String(r.mttrMinutes) },
    { label: '故障次数', value: String(r.failures) },
    { label: '修复次数', value: String(r.repairs) },
  ]
})

// —— 数据新鲜度：断流/无数据诚实标 stale（页脚实时灯 + 最后更新时刻），不白屏 ——
function hhmmss(ms: number): string {
  const d = new Date(ms)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}
const freshness = computed<{ tone: 'live' | 'stale' | 'wait'; text: string }>(() => {
  if (isStale.value) {
    return {
      tone: 'stale',
      text: lastUpdated.value
        ? `数据滞留 · 最后更新 ${hhmmss(lastUpdated.value)}`
        : '后端不可达 · 数据滞留',
    }
  }
  if (lastUpdated.value) return { tone: 'live', text: `实时 · 更新于 ${hhmmss(lastUpdated.value)}` }
  return { tone: 'wait', text: '连接数据…' }
})
</script>

<template>
  <ScreenLayout title="Nerv-IIP 设备监控大屏" :line="factoryName" screen="指挥中心大屏 02">
    <div v-if="ov" class="eq">
      <div class="main">
        <!-- 主体：设备状态全景墙（视图焦点，占绝对主导） -->
        <section class="wall-wrap">
          <div class="sec-h">
            <i class="sec-glyph" aria-hidden="true" />
            <span class="sec-t">设备状态全景墙</span>
            <span class="sec-rule" aria-hidden="true" />
            <NvScreenSegmented v-model="viewModel" :options="VIEW_OPTIONS" />
          </div>
          <DeviceStatusWall
            :devices="devicesLive"
            :counts="ov.counts"
            :view="view"
            :factory-id="scope.currentFactoryId"
            :workshop-ids="scope.persona.workshopIds"
            @select="openDevice"
            @visible="(ids) => (visibleIds = ids)"
          />
        </section>

        <!-- 右窄栏：可靠性 + 异常与待办（正常日寥寥数条，空即健康） -->
        <div class="side">
          <NvScreenPanel title="可靠性">
            <template #extra>
              <NvScreenStatusTag tone="amber" label="≈ 可用率 · 待 #570" />
            </template>
            <div class="rel">
              <NvRingGauge :value="ov.reliability.availability" label="时间稼动率" :size="104" />
              <dl class="rel-grid">
                <div v-for="c in relCells" :key="c.label">
                  <dt>{{ c.label }}</dt>
                  <dd>{{ c.value }}</dd>
                </div>
              </dl>
            </div>
          </NvScreenPanel>

          <NvScreenPanel title="异常与待办" class="events">
            <template #extra>
              <span class="ev-count" :class="{ calm: events.length === 0 }">
                {{ events.length === 0 ? '全部正常' : `${events.length} 项` }}
              </span>
            </template>
            <NvScreenTabs v-model="evTab" :items="evTabs" class="ev-tabs" />
            <NvScreenScrollArea class="ev-list">
              <!-- 全部：合并流（严重度排序） -->
              <template v-if="evTab === 'all'">
                <div v-for="e in events" :key="e.key" class="ev-row">
                  <i class="ev-dot" :class="e.tone" />
                  <span v-if="e.time" class="ev-time">{{ e.time }}</span>
                  <span class="ev-txt">{{ e.text }}</span>
                  <span class="ev-tag" :class="{ late: e.late }">{{ e.tag }}</span>
                </div>
                <div v-if="events.length === 0" class="ev-empty">
                  <NvScreenStatusLight tone="run" label="设备运行平稳" />
                  <p>无未恢复报警 · 无进行中维修 · 无到期保养</p>
                </div>
              </template>

              <!-- 报警：级别 + 产线·内容 + 工单联动 + 处置状态 + 响应状态（#686：未确认高亮 / 已确认+人） -->
              <template v-else-if="evTab === 'alarm'">
                <div
                  v-for="a in openAlarms"
                  :key="a.wo + a.name"
                  class="al-row"
                  :class="{ unacked: !a.acked }"
                >
                  <div class="al-top">
                    <i class="ev-dot" :class="a.level === 'sev' ? 'red' : 'amber'" />
                    <b class="al-name">{{ a.line }} · {{ a.name }}</b>
                    <span v-if="a.escalated" class="al-esc">已升级</span>
                    <span class="al-time">{{ a.time }}</span>
                  </div>
                  <div class="al-sub">
                    <span class="al-wo">{{ a.wo }}</span>
                    <span class="al-right">
                      <span class="al-ack" :class="a.acked ? 'done' : 'pending'">
                        {{ a.acked ? `已确认 · ${a.ackBy || '—'}` : '未确认' }}
                      </span>
                      <span class="al-status" :class="{ late: a.level === 'sev' }">{{
                        a.status
                      }}</span>
                    </span>
                  </div>
                </div>
                <div v-if="!openAlarms.length" class="ev-empty">
                  <NvScreenStatusLight tone="run" label="无未恢复报警" />
                </div>
              </template>

              <!-- 维修：状态机步进 + 报修/历时/SLA + 责任人 -->
              <template v-else-if="evTab === 'repair'">
                <div v-for="r in activeRepairs" :key="r.wo" class="rp-row">
                  <div class="rp-top">
                    <span class="rp-wo">{{ r.wo }}</span>
                    <span class="rp-dev">{{ r.device }} · {{ r.issue }}</span>
                    <NvScreenStatusTag v-if="r.overdue" tone="red" label="超时" />
                    <NvScreenStatusTag v-else-if="r.blockedBy" tone="amber" label="待备件" />
                    <NvScreenStatusTag v-else-if="r.awaitingConfirm" tone="cyan" label="待确认" />
                  </div>
                  <div class="rp-meta">
                    <span class="rp-steps" :aria-label="`当前阶段 ${r.stage}`">
                      <i
                        v-for="(s, i) in REPAIR_STAGES"
                        :key="s"
                        class="rp-step"
                        :class="{
                          on: i <= stageIdx(r),
                          cur: i === stageIdx(r),
                          late: r.overdue && i === stageIdx(r),
                        }"
                        :title="s"
                      />
                    </span>
                    <b class="rp-stage" :class="{ late: r.overdue }">{{ r.stage }}</b>
                    <span class="rp-eta" :class="{ late: r.overdue }">{{ r.etaText }}</span>
                  </div>
                  <div class="rp-sub">
                    报修 {{ r.reportedAt }} · 已 {{ r.elapsedMin }} min · {{ r.assignee
                    }}<template v-if="r.blockedBy"> · {{ r.blockedBy }}</template>
                  </div>
                </div>
                <div v-if="!activeRepairs.length" class="ev-empty">
                  <NvScreenStatusLight tone="run" label="无进行中维修" />
                </div>
              </template>

              <!-- 保养：PM 计划（含已完成）+ 今日点检台账 -->
              <template v-else>
                <div v-for="t in ov.pmTasks" :key="t.device + t.task" class="pm-row">
                  <span class="pm-dev">{{ t.device }}</span>
                  <span class="pm-task">{{ t.task }}</span>
                  <span class="pm-due" :class="t.state">{{ t.due }}</span>
                </div>
                <h5 class="insp-h">今日点检台账</h5>
                <div v-for="i in ov.inspections" :key="i.time + i.device" class="insp-row">
                  <span class="insp-time">{{ i.time }}</span>
                  <span class="insp-txt">{{ i.device }} · {{ i.item }} · {{ i.by }}</span>
                  <span class="insp-res" :class="{ bad: i.result === '异常' }">{{ i.result }}</span>
                </div>
              </template>
            </NvScreenScrollArea>
            <p class="ev-note">维修历史与参数趋势见设备详情 · 帕累托/备件联动 待 #570</p>
          </NvScreenPanel>
        </div>
      </div>

      <footer class="scr-foot">
        <RouterLink :to="backLink.to" class="scr-back">‹ {{ backLink.label }}</RouterLink>
        <div class="scr-foot-r">
          <span class="scr-fresh" :class="freshness.tone"
            ><i aria-hidden="true" />{{ freshness.text }}</span
          >
          <span>实时参数与趋势待 historian · #570 · OEE 性能率/良品率占位 · #738</span>
        </div>
      </footer>
    </div>
    <div v-else class="eq-loading">连接数据…</div>

    <DeviceDetailModal
      v-if="selectedId"
      :device-id="selectedId"
      :factory-id="scope.currentFactoryId"
      :workshop-ids="scope.persona.workshopIds"
      @close="closeDetail"
    />
  </ScreenLayout>
</template>

<style scoped>
@layer app {
  .eq {
    height: 100%;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .eq-loading {
    height: 100%;
    display: grid;
    place-content: center;
    color: var(--nv-scr-muted);
    font-size: 15px;
  }
  /* 统一页脚：按来路返回 + 口径注记 */
  .scr-foot {
    flex: none;
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    border-top: 1px solid var(--nv-scr-divider);
    padding-top: 10px;
    font-size: 12.5px;
    color: var(--nv-scr-faint);
  }
  .scr-back {
    color: var(--nv-scr-cyan);
    text-decoration: none;
    font-size: 13.5px;
    flex: none;
  }
  .scr-foot-r {
    display: flex;
    align-items: center;
    gap: 16px;
    min-width: 0;
  }
  .scr-fresh {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    flex: none;
    font-variant-numeric: tabular-nums;
  }
  .scr-fresh i {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--nv-scr-faint);
  }
  .scr-fresh.live {
    color: var(--nv-scr-green);
  }
  .scr-fresh.live i {
    background: var(--nv-scr-green);
    box-shadow: 0 0 7px var(--nv-scr-green);
    animation: breathe 4.5s ease-in-out infinite;
  }
  .scr-fresh.stale {
    color: var(--nv-scr-amber);
  }
  .scr-fresh.stale i {
    background: var(--nv-scr-amber);
    box-shadow: 0 0 7px var(--nv-scr-amber);
  }
  @keyframes breathe {
    0%,
    100% {
      opacity: 0.55;
    }
    50% {
      opacity: 1;
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .scr-fresh.live i {
      animation: none;
    }
  }
  .main {
    flex: 1;
    min-height: 0;
    display: grid;
    grid-template-columns: 2.7fr 1fr;
    gap: 16px;
  }
  .wall-wrap {
    display: flex;
    flex-direction: column;
    min-height: 0;
  }

  /* 区块标题（无外壳区域用）：与 ScreenPanel 标题同款语言 */
  .sec-h {
    display: flex;
    align-items: center;
    gap: 11px;
    margin-bottom: 12px;
    min-height: 24px;
  }
  .sec-glyph {
    width: 8px;
    height: 18px;
    flex: none;
    border-radius: 2px;
    transform: skewX(-16deg);
    background: linear-gradient(180deg, var(--nv-scr-cyan), rgba(74, 166, 238, 0.25));
    box-shadow: 0 0 11px rgba(74, 166, 238, 0.55);
  }
  .sec-t {
    font-size: 17px;
    font-weight: 700;
    letter-spacing: 0.1em;
    color: #fff;
    text-shadow: 0 0 16px rgba(96, 180, 255, 0.4);
    white-space: nowrap;
  }
  .sec-rule {
    flex: 1;
    height: 1px;
    margin: 0 6px;
    background: linear-gradient(
      90deg,
      rgba(135, 208, 255, 0.28),
      rgba(255, 255, 255, 0.05) 45%,
      transparent
    );
  }

  /* —— 右窄栏 —— */
  .side {
    display: grid;
    grid-template-rows: auto 1fr;
    gap: 16px;
    min-height: 0;
  }
  .rel {
    display: flex;
    align-items: center;
    gap: 16px;
  }
  .rel-grid {
    flex: 1;
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 10px 8px;
    margin: 0;
  }
  .rel-grid dt {
    font-size: 12px;
    color: var(--nv-scr-muted);
  }
  .rel-grid dd {
    margin: 3px 0 0;
    font-size: 21px;
    font-weight: 700;
    font-variant-numeric: tabular-nums;
    color: var(--nv-scr-text);
  }

  /* 异常与待办：合并事件流（报警/维修/PM），空即健康 */
  .events {
    display: flex;
    flex-direction: column;
    min-height: 0;
  }
  .ev-count {
    font-size: 13px;
    color: var(--nv-scr-amber);
    font-variant-numeric: tabular-nums;
  }
  .ev-count.calm {
    color: var(--nv-scr-green);
  }
  .ev-tabs {
    margin-bottom: 6px;
    flex: none;
  }
  .ev-list {
    flex: 1;
    min-height: 0;
  }

  /* 报警 tab：两行行式（产线·内容 / 工单·处置状态） */
  .al-row {
    padding: 8px 2px;
    border-bottom: 1px solid var(--nv-scr-divider);
  }
  .al-top {
    display: flex;
    align-items: center;
    gap: 9px;
    min-width: 0;
  }
  .al-name {
    flex: 1;
    min-width: 0;
    font-size: 13px;
    font-weight: 600;
    color: var(--nv-scr-text);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .al-time {
    flex: none;
    font-size: 12px;
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
  }
  .al-sub {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 10px;
    margin: 4px 0 0 17px;
    font-size: 12px;
  }
  .al-wo {
    font-family: ui-monospace, monospace;
    color: var(--nv-scr-cyan);
  }
  .al-status {
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
  }
  .al-status.late {
    color: var(--nv-scr-red);
  }
  /* #686 未确认高亮：左侧红条 + 极淡红底，一眼看出"还没人响应" */
  .al-row.unacked {
    background: linear-gradient(90deg, rgba(239, 90, 99, 0.1), transparent 60%);
    box-shadow: inset 2px 0 0 var(--nv-scr-red);
    padding-left: 8px;
  }
  /* 已升级徽标 */
  .al-esc {
    flex: none;
    font-size: 10.5px;
    padding: 1px 6px;
    border-radius: 999px;
    color: var(--nv-scr-red);
    border: 1px solid rgba(239, 90, 99, 0.5);
    background: rgba(239, 90, 99, 0.12);
    letter-spacing: 0.04em;
  }
  /* 处置状态 + 响应状态右簇 */
  .al-right {
    display: inline-flex;
    align-items: baseline;
    gap: 10px;
    flex: none;
  }
  /* #686 响应状态胶囊：未确认红 / 已确认（含确认人）青 */
  .al-ack {
    flex: none;
    font-size: 11.5px;
    padding: 1px 7px;
    border-radius: 999px;
    white-space: nowrap;
  }
  .al-ack.pending {
    color: var(--nv-scr-red);
    background: rgba(239, 90, 99, 0.14);
    border: 1px solid rgba(239, 90, 99, 0.4);
  }
  .al-ack.done {
    color: var(--nv-scr-cyan);
    background: rgba(74, 166, 238, 0.12);
    border: 1px solid rgba(74, 166, 238, 0.3);
  }

  /* 维修 tab：状态机步进行 */
  .rp-row {
    padding: 9px 2px;
    border-bottom: 1px solid var(--nv-scr-divider);
  }
  .rp-top {
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 0;
  }
  .rp-wo {
    font-family: ui-monospace, monospace;
    font-size: 12.5px;
    color: var(--nv-scr-cyan);
    flex: none;
  }
  .rp-dev {
    flex: 1;
    min-width: 0;
    font-size: 13px;
    color: var(--nv-scr-text-2);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .rp-meta {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-top: 7px;
    font-size: 12.5px;
    white-space: nowrap;
  }
  .rp-steps {
    display: inline-flex;
    align-items: center;
    flex: none;
  }
  .rp-step {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: rgba(255, 255, 255, 0.14);
    position: relative;
  }
  .rp-step + .rp-step {
    margin-left: 15px;
  }
  .rp-step + .rp-step::before {
    content: '';
    position: absolute;
    right: 10px;
    top: 3px;
    width: 12px;
    height: 1px;
    background: rgba(255, 255, 255, 0.14);
  }
  .rp-step.on {
    background: var(--nv-scr-cyan);
  }
  .rp-step.on + .rp-step.on::before {
    background: var(--nv-scr-cyan-dim);
  }
  .rp-step.cur {
    box-shadow: 0 0 7px var(--nv-scr-cyan-dim);
  }
  .rp-step.cur.late {
    background: var(--nv-scr-red);
    box-shadow: 0 0 7px var(--nv-scr-red);
  }
  .rp-stage {
    font-weight: 600;
    color: var(--nv-scr-text-2);
    flex: none;
  }
  .rp-stage.late {
    color: var(--nv-scr-red);
  }
  .rp-eta {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
  }
  .rp-eta.late {
    color: var(--nv-scr-red);
  }
  .rp-sub {
    margin-top: 4px;
    font-size: 12px;
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  /* 保养 tab：PM 计划 + 点检台账 */
  .pm-row {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 2px;
    border-bottom: 1px solid var(--nv-scr-divider);
    font-size: 13px;
  }
  .pm-dev {
    color: var(--nv-scr-text-2);
    flex: none;
  }
  .pm-task {
    flex: 1;
    min-width: 0;
    color: var(--nv-scr-muted);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .pm-due {
    flex: none;
    font-variant-numeric: tabular-nums;
    color: var(--nv-scr-amber);
  }
  .pm-due.overdue {
    color: var(--nv-scr-red);
  }
  .pm-due.done {
    color: var(--nv-scr-green);
  }
  .insp-h {
    margin: 12px 0 4px;
    font-size: 12px;
    font-weight: 600;
    letter-spacing: 0.06em;
    color: var(--nv-scr-muted);
  }
  .insp-row {
    display: flex;
    align-items: center;
    gap: 9px;
    padding: 7px 2px;
    border-bottom: 1px solid var(--nv-scr-divider);
    font-size: 12.5px;
  }
  .insp-time {
    font-variant-numeric: tabular-nums;
    color: var(--nv-scr-muted);
    flex: none;
  }
  .insp-txt {
    flex: 1;
    min-width: 0;
    color: var(--nv-scr-text-2);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .insp-res {
    flex: none;
    color: var(--nv-scr-green);
  }
  .insp-res.bad {
    color: var(--nv-scr-red);
  }
  .ev-row {
    display: flex;
    align-items: center;
    gap: 9px;
    padding: 9px 2px;
    border-bottom: 1px solid var(--nv-scr-divider);
    font-size: 13px;
  }
  .ev-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    flex: none;
  }
  .ev-dot.red {
    background: var(--nv-scr-red);
    box-shadow: 0 0 7px var(--nv-scr-red);
  }
  .ev-dot.amber {
    background: var(--nv-scr-amber);
    box-shadow: 0 0 7px var(--nv-scr-amber);
  }
  .ev-dot.cyan {
    background: var(--nv-scr-cyan);
  }
  .ev-time {
    flex: none;
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
  }
  .ev-txt {
    flex: 1;
    min-width: 0;
    color: var(--nv-scr-text-2);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .ev-tag {
    flex: none;
    max-width: 40%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
  }
  .ev-tag.late {
    color: var(--nv-scr-red);
  }
  .ev-empty {
    height: 100%;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 10px;
  }
  .ev-empty p {
    margin: 0;
    font-size: 12.5px;
    color: var(--nv-scr-muted);
  }
  .ev-note {
    margin: 8px 0 0;
    font-size: 12px;
    color: var(--nv-scr-faint);
  }
}
</style>
