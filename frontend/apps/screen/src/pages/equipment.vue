<script setup lang="ts">
import { AlarmTable, RingGauge, ScreenPanel, ScreenSegmented, StatusTag } from '@nerv-iip/ui'
import { computed, ref, watch } from 'vue'
import { useAccessScope } from '@/access/useAccessScope'
import DeviceDetailModal from '@/components/equipment/DeviceDetailModal.vue'
import DeviceStatusWall from '@/components/equipment/DeviceStatusWall.vue'
import type { DeviceCell, DeviceParamsTick, EquipmentOverview, RepairOrder } from '@/data/contracts/equipment'
import { REPAIR_STAGES } from '@/data/contracts/equipment'
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
</script>

<template>
  <ScreenLayout title="Nerv-IIP 设备监控大屏" :line="factoryName" screen="指挥中心大屏 02">
    <div v-if="ov" class="eq">
      <div class="main">
        <!-- 左列：设备状态全景墙（无外壳，浮在舱底上）+ 未恢复报警表 -->
        <div class="left">
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

          <AlarmTable title="未恢复报警" :rows="ov.alarms" more="" class="alarms" />
        </div>

        <!-- 右列：可靠性 / 维修工单进度 / 今日保养与点检 -->
        <div class="side">
          <ScreenPanel title="可靠性 · 时间稼动率">
            <template #extra>
              <StatusTag tone="amber" label="≈ 可用率 · 非完整 OEE" />
            </template>
            <div class="rel">
              <RingGauge :value="ov.reliability.availability" label="时间稼动率" :size="118" />
              <dl class="rel-grid">
                <div v-for="c in relCells" :key="c.label">
                  <dt>{{ c.label }}</dt>
                  <dd>{{ c.value }}</dd>
                </div>
              </dl>
            </div>
          </ScreenPanel>

          <ScreenPanel title="维修工单" class="repairs">
            <div class="rp-list sb-scroll">
              <div v-for="r in ov.repairs" :key="r.wo" class="rp-row">
                <div class="rp-top">
                  <span class="rp-wo">{{ r.wo }}</span>
                  <span class="rp-dev">{{ r.device }} · {{ r.issue }}</span>
                  <StatusTag v-if="r.overdue" tone="red" label="超时" />
                  <StatusTag v-else-if="r.blockedBy" tone="amber" label="待备件" />
                  <StatusTag v-else-if="r.awaitingConfirm" tone="cyan" label="待确认" />
                </div>
                <!-- 现实衡量：状态机步进 + 报修时刻/已历时/SLA + 责任人（非百分比进度） -->
                <div class="rp-meta">
                  <span class="rp-steps" :aria-label="`当前阶段 ${r.stage}`">
                    <i
                      v-for="(s, i) in REPAIR_STAGES"
                      :key="s"
                      class="rp-step"
                      :class="{ on: i <= stageIdx(r), cur: i === stageIdx(r), late: r.overdue && i === stageIdx(r) }"
                      :title="s"
                    />
                  </span>
                  <b class="rp-stage" :class="{ late: r.overdue }">{{ r.stage }}</b>
                  <span class="rp-time">报修 {{ r.reportedAt }} · 已 {{ r.elapsedMin }} min</span>
                  <span class="rp-eta" :class="{ late: r.overdue }">{{ r.etaText }}</span>
                  <span class="rp-assignee">{{ r.assignee }}</span>
                </div>
                <div v-if="r.blockedBy" class="rp-blocked">{{ r.blockedBy }}</div>
              </div>
            </div>
          </ScreenPanel>

          <ScreenPanel title="今日保养与点检" class="pm">
            <div class="pm-scroll sb-scroll">
              <div class="pm-list">
                <div v-for="t in ov.pmTasks" :key="t.device + t.task" class="pm-row">
                  <span class="pm-dev">{{ t.device }}</span>
                  <span class="pm-task">{{ t.task }}</span>
                  <span class="pm-due" :class="t.state">{{ t.due }}</span>
                </div>
              </div>
              <div class="insp-list">
                <div v-for="i in ov.inspections" :key="i.time + i.device" class="insp-row">
                  <span class="insp-time">{{ i.time }}</span>
                  <span class="insp-txt">{{ i.device }} · {{ i.item }} · {{ i.by }}</span>
                  <span class="insp-res" :class="{ bad: i.result === '异常' }">{{ i.result }}</span>
                </div>
              </div>
            </div>
            <p class="pm-note">停机帕累托 · 备件库存联动 · PM 达成率 / 点检完成率 · 待 #570</p>
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
  grid-template-columns: 1.55fr 1fr;
  gap: 16px;
}
.left {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 0;
}
.wall-wrap {
  flex: 1.25;
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.alarms {
  flex: 1;
  min-height: 0;
  overflow: hidden;
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

/* —— 右列 —— */
.side {
  display: grid;
  grid-template-rows: auto 1fr 1fr;
  gap: 16px;
  min-height: 0;
}
.rel {
  display: flex;
  align-items: center;
  gap: 22px;
}
.rel-grid {
  flex: 1;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px 10px;
  margin: 0;
}
.rel-grid dt {
  font-size: 12.5px;
  color: var(--sb-muted);
}
.rel-grid dd {
  margin: 4px 0 0;
  font-size: 24px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text);
}

.repairs,
.pm {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.rp-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  padding-right: 4px;
}
.rp-row + .rp-row {
  border-top: 1px solid var(--sb-divider);
  padding-top: 9px;
}
.rp-row {
  padding-bottom: 9px;
}
.rp-top {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}
.rp-wo {
  font-family: ui-monospace, monospace;
  font-size: 13px;
  color: var(--sb-cyan);
  flex: none;
}
.rp-dev {
  flex: 1;
  min-width: 0;
  font-size: 13.5px;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
/* 状态机步进器：达成点亮，当前点带光晕；超时当前点转红 */
.rp-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 8px;
  font-size: 12.5px;
  color: var(--sb-muted);
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
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
  background: var(--sb-cyan);
}
.rp-step.on + .rp-step.on::before {
  background: var(--sb-cyan-dim);
}
.rp-step.cur {
  box-shadow: 0 0 7px var(--sb-cyan-dim);
}
.rp-step.cur.late {
  background: var(--sb-red);
  box-shadow: 0 0 7px var(--sb-red);
}
.rp-stage {
  font-weight: 600;
  color: var(--sb-text-2);
  flex: none;
}
.rp-stage.late {
  color: var(--sb-red);
}
.rp-time {
  flex: none;
}
.rp-eta {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
.rp-eta.late {
  color: var(--sb-red);
}
.rp-assignee {
  flex: none;
  color: var(--sb-text-2);
}
.rp-blocked {
  margin-top: 5px;
  font-size: 12px;
  color: var(--sb-amber);
}

.pm-list {
  display: flex;
  flex-direction: column;
}
.pm-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 2px;
  border-bottom: 1px solid var(--sb-divider);
  font-size: 13.5px;
}
.pm-dev {
  color: var(--sb-text-2);
  flex: none;
}
.pm-task {
  flex: 1;
  min-width: 0;
  color: var(--sb-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.pm-due {
  flex: none;
  font-variant-numeric: tabular-nums;
  color: var(--sb-amber);
}
.pm-due.overdue {
  color: var(--sb-red);
}
.pm-due.done {
  color: var(--sb-green);
}
.pm-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding-right: 4px;
}
.insp-list {
  display: flex;
  flex-direction: column;
  margin-top: 4px;
}
.insp-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 7px 2px;
  border-bottom: 1px solid var(--sb-divider);
  font-size: 13px;
}
.insp-time {
  font-variant-numeric: tabular-nums;
  color: var(--sb-muted);
  flex: none;
}
.insp-txt {
  flex: 1;
  min-width: 0;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.insp-res {
  flex: none;
  color: var(--sb-green);
}
.insp-res.bad {
  color: var(--sb-red);
}
.pm-note {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--sb-faint);
}
</style>
