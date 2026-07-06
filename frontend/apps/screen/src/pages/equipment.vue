<script setup lang="ts">
import { RingGauge, ScreenPanel, ScreenSegmented, StatusLight, StatusTag } from '@nerv-iip/ui'
import { computed, ref, watch } from 'vue'
import { useAccessScope } from '@/access/useAccessScope'
import DeviceDetailModal from '@/components/equipment/DeviceDetailModal.vue'
import DeviceStatusWall from '@/components/equipment/DeviceStatusWall.vue'
import type { DeviceCell, DeviceParamsTick, EquipmentOverview } from '@/data/contracts/equipment'
import { fetchDeviceParamsTick, fetchEquipmentOverview } from '@/data/fetchers/equipment'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { useScreenData } from '@/screen-kit'

// 刷新频率分层：格上参数 2s 快刷（仅视野内设备）· 全景/计数/流 5s ·
// 详情弹窗 3s（弹窗内部）；页面隐藏时 useScreenData 统一暂停轮询。
const scope = useAccessScope()
const { data: ov, refresh } = useScreenData<EquipmentOverview>(
  () => fetchEquipmentOverview(scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 5000 },
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
  { intervalMs: 2000 },
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
      tag: r.overdue ? `已超期 · ${r.assignee}` : r.blockedBy ? '待备件' : `${r.stage} · ${r.assignee}`,
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
            <ScreenSegmented v-model="viewModel" :options="VIEW_OPTIONS" />
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
          <ScreenPanel title="可靠性">
            <template #extra>
              <StatusTag tone="amber" label="≈ 可用率 · 待 #570" />
            </template>
            <div class="rel">
              <RingGauge :value="ov.reliability.availability" label="时间稼动率" :size="104" />
              <dl class="rel-grid">
                <div v-for="c in relCells" :key="c.label">
                  <dt>{{ c.label }}</dt>
                  <dd>{{ c.value }}</dd>
                </div>
              </dl>
            </div>
          </ScreenPanel>

          <ScreenPanel title="异常与待办" class="events">
            <template #extra>
              <span class="ev-count" :class="{ calm: events.length === 0 }">
                {{ events.length === 0 ? '全部正常' : `${events.length} 项` }}
              </span>
            </template>
            <div class="ev-list sb-scroll">
              <div v-for="e in events" :key="e.key" class="ev-row">
                <i class="ev-dot" :class="e.tone" />
                <span v-if="e.time" class="ev-time">{{ e.time }}</span>
                <span class="ev-txt">{{ e.text }}</span>
                <span class="ev-tag" :class="{ late: e.late }">{{ e.tag }}</span>
              </div>
              <div v-if="events.length === 0" class="ev-empty">
                <StatusLight tone="run" label="设备运行平稳" />
                <p>无未恢复报警 · 无进行中维修 · 无到期保养</p>
              </div>
            </div>
            <p class="ev-note">点检台账与维修历史见设备详情 · 帕累托/备件联动 待 #570</p>
          </ScreenPanel>
        </div>
      </div>
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
.eq {
  height: 100%;
  min-height: 0;
}
.eq-loading {
  height: 100%;
  display: grid;
  place-content: center;
  color: var(--sb-muted);
  font-size: 15px;
}
.main {
  height: 100%;
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
  background: linear-gradient(180deg, var(--sb-cyan), rgba(74, 166, 238, 0.25));
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
  background: linear-gradient(90deg, rgba(135, 208, 255, 0.28), rgba(255, 255, 255, 0.05) 45%, transparent);
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
  color: var(--sb-muted);
}
.rel-grid dd {
  margin: 3px 0 0;
  font-size: 21px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text);
}

/* 异常与待办：合并事件流（报警/维修/PM），空即健康 */
.events {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.ev-count {
  font-size: 13px;
  color: var(--sb-amber);
  font-variant-numeric: tabular-nums;
}
.ev-count.calm {
  color: var(--sb-green);
}
.ev-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding-right: 4px;
}
.ev-row {
  display: flex;
  align-items: center;
  gap: 9px;
  padding: 9px 2px;
  border-bottom: 1px solid var(--sb-divider);
  font-size: 13px;
}
.ev-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex: none;
}
.ev-dot.red {
  background: var(--sb-red);
  box-shadow: 0 0 7px var(--sb-red);
}
.ev-dot.amber {
  background: var(--sb-amber);
  box-shadow: 0 0 7px var(--sb-amber);
}
.ev-dot.cyan {
  background: var(--sb-cyan);
}
.ev-time {
  flex: none;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.ev-txt {
  flex: 1;
  min-width: 0;
  color: var(--sb-text-2);
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
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.ev-tag.late {
  color: var(--sb-red);
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
  color: var(--sb-muted);
}
.ev-note {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--sb-faint);
}
</style>
