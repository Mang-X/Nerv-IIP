<script setup lang="ts">
import { AlarmTable, RingGauge, ScreenPanel, StatusTag } from '@nerv-iip/ui'
import { computed, watch } from 'vue'
import { useAccessScope } from '@/access/useAccessScope'
import DeviceStatusWall from '@/components/equipment/DeviceStatusWall.vue'
import type { EquipmentOverview } from '@/data/contracts/equipment'
import { fetchEquipmentOverview } from '@/data/fetchers/equipment'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { useScreenData } from '@/screen-kit'

const scope = useAccessScope()
const { data: ov, refresh } = useScreenData<EquipmentOverview>(
  () => fetchEquipmentOverview(scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 5000 },
)
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

// —— 五态计数条（墙体标题行右侧）——
const countItems = computed(() => {
  const c = ov.value?.counts
  if (!c) return []
  return [
    { k: 'run', label: '运行', v: c.run },
    { k: 'idle', label: '待机', v: c.idle },
    { k: 'down', label: '停机', v: c.down },
    { k: 'alarm', label: '报警', v: c.alarm },
    { k: 'offline', label: '断线', v: c.offline },
  ]
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
        <!-- 左列：设备状态全景墙（无外壳，浮在舱底上）+ 未恢复报警表 -->
        <div class="left">
          <section class="wall-wrap">
            <div class="sec-h">
              <i class="sec-glyph" aria-hidden="true" />
              <span class="sec-t">设备状态全景墙</span>
              <span class="sec-rule" aria-hidden="true" />
              <span class="counts">
                <span v-for="c in countItems" :key="c.k" class="count" :class="c.k">
                  <i />{{ c.label }} <b>{{ c.v }}</b>
                </span>
              </span>
            </div>
            <DeviceStatusWall :devices="ov.devices" />
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

          <ScreenPanel title="维修工单进度" class="repairs">
            <div class="rp-list">
              <div v-for="r in ov.repairs" :key="r.wo" class="rp-row">
                <div class="rp-top">
                  <span class="rp-wo">{{ r.wo }}</span>
                  <span class="rp-dev">{{ r.device }} · {{ r.issue }}</span>
                  <StatusTag v-if="r.overdue" tone="red" label="超时" />
                  <StatusTag v-else-if="r.awaitingConfirm" tone="cyan" label="待确认" />
                  <span v-else class="rp-stage">{{ r.stage }}</span>
                </div>
                <div class="rp-bar-row">
                  <div class="rp-bar">
                    <i
                      :class="{ overdue: r.overdue, done: r.progress >= 100 }"
                      :style="{ width: `${r.progress}%` }"
                    />
                  </div>
                  <b class="rp-pct">{{ r.progress }}%</b>
                </div>
              </div>
            </div>
          </ScreenPanel>

          <ScreenPanel title="今日保养与点检" class="pm">
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
            <p class="pm-note">停机帕累托 · 备件库存联动 · PM 达成率 / 点检完成率 · 待 #570</p>
          </ScreenPanel>
        </div>
      </div>
    </div>
    <div v-else class="eq-loading">连接数据…</div>
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

/* 五态计数条 */
.counts {
  display: inline-flex;
  align-items: center;
  gap: 16px;
  white-space: nowrap;
}
.count {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--sb-text-2);
}
.count b {
  font-size: 17px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text);
}
.count i {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--sb-faint);
}
.count.run i {
  background: var(--sb-green);
  box-shadow: 0 0 7px var(--sb-green);
}
.count.idle i {
  background: var(--sb-amber);
  box-shadow: 0 0 7px var(--sb-amber);
}
.count.alarm i {
  background: var(--sb-red);
  box-shadow: 0 0 7px var(--sb-red);
}
.count.alarm b {
  color: var(--sb-red);
}
.count.down i {
  background: var(--sb-muted);
}
.count.offline i {
  background: var(--sb-faint);
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
  display: flex;
  flex-direction: column;
  justify-content: space-evenly;
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
.rp-stage {
  font-size: 12.5px;
  color: var(--sb-muted);
  flex: none;
}
.rp-bar-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 8px;
}
.rp-bar {
  flex: 1;
  height: 6px;
  border-radius: 3px;
  background: rgba(255, 255, 255, 0.07);
  overflow: hidden;
}
.rp-bar i {
  display: block;
  height: 100%;
  border-radius: 3px;
  background: var(--sb-cyan);
  transition: width 0.6s var(--sb-ease-emphasized);
}
.rp-bar i.overdue {
  background: var(--sb-red);
}
.rp-bar i.done {
  background: var(--sb-green);
}
.rp-pct {
  font-size: 14px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text);
  width: 44px;
  text-align: right;
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
.insp-list {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  justify-content: flex-start;
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
