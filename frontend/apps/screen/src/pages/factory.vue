<script setup lang="ts">
import { RingGauge, ScreenPanel, StatusTag } from '@nerv-iip/ui'
import { computed, watch } from 'vue'
import { useAccessScope } from '@/access/useAccessScope'
import WorkshopHealthCard from '@/components/factory/WorkshopHealthCard.vue'
import type { FactoryOverview } from '@/data/contracts/factory'
import { fetchFactoryOverview } from '@/data/fetchers/factory'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { ScrollBoard, useScreenData } from '@/screen-kit'

const scope = useAccessScope()
const { data: ov, isStale, refresh } = useScreenData<FactoryOverview>(
  () => fetchFactoryOverview(scope.currentFactoryId, scope.persona.workshopIds),
  { intervalMs: 4000 },
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

// —— 顶部 KPI 带右侧单元（语义色只落在异常数字上）——
interface BandCell {
  label: string
  value: string
  sub?: string
  tone?: 'bad' | 'warn'
}
const bandCells = computed<BandCell[]>(() => {
  const k = ov.value?.kpis
  if (!k) return []
  return [
    { label: '在产工单', value: String(k.wipOrders) },
    { label: '超期 / 风险工单', value: String(k.riskOrders), tone: k.riskOrders > 0 ? 'bad' : undefined },
    {
      label: '未恢复告警',
      value: String(k.openAlarms),
      sub: `严重 ${k.criticalAlarms}`,
      tone: k.criticalAlarms > 0 ? 'bad' : k.openAlarms > 0 ? 'warn' : undefined,
    },
    { label: 'Open 停机', value: String(k.openDowntime), tone: k.openDowntime > 0 ? 'warn' : undefined },
    { label: 'Open NCR', value: String(k.openNcr), tone: k.openNcr > 0 ? 'warn' : undefined },
  ]
})
</script>

<template>
  <ScreenLayout title="Nerv-IIP 工厂运营大屏" :line="factoryName" screen="指挥中心大屏 01">
    <div v-if="ov" class="fx">
      <!-- 全厂 KPI 带：达成率大号进度环为焦点，其余为语义色数字 -->
      <ScreenPanel class="band">
        <div class="band-in">
          <div class="hero">
            <div class="hero-ring" :style="{ '--p': ov.kpis.achievement }" />
            <div class="hero-c">
              <div class="hero-v">{{ ov.kpis.achievement }}<small>%</small></div>
              <div class="hero-l">今日全厂达成率</div>
            </div>
          </div>
          <div class="band-cells">
            <div v-for="c in bandCells" :key="c.label" class="band-cell">
              <div class="band-v" :class="c.tone">{{ c.value }}</div>
              <div class="band-l">
                {{ c.label }}<span v-if="c.sub" class="band-sub" :class="c.tone">· {{ c.sub }}</span>
              </div>
            </div>
          </div>
        </div>
      </ScreenPanel>

      <div class="main">
        <!-- 车间状态矩阵：红卡置顶（数据层排序），图例即健康度合成规则 -->
        <ScreenPanel title="车间运行状态" class="matrix">
          <template #extra>
            <span class="legend">
              <i class="lg green" />正常
              <i class="lg yellow" />关注 · 停机/达成率低
              <i class="lg red" />异常 · critical/超期
            </span>
          </template>
          <div class="matrix-grid">
            <WorkshopHealthCard v-for="w in ov.workshops" :key="w.id" :cell="w" />
            <div class="more-card">
              <span class="more-t">更多指标接入中</span>
              <span class="more-d">不良率 · 设备在线率 · 产出良率趋势</span>
              <StatusTag tone="amber" label="待 #570" />
            </div>
          </div>
        </ScreenPanel>

        <div class="side">
          <ScreenPanel title="综合效率 OEE">
            <template #extra>
              <StatusTag tone="amber" label="综合 ≈ 可用率 · 待 #570" />
            </template>
            <div class="rings">
              <RingGauge v-for="o in ov.oee" :key="o.label" :value="o.value" :label="o.label" :size="112" />
            </div>
            <p class="oee-note">性能率 / 良品率为占位值，#570 接入后启用真实综合 OEE</p>
          </ScreenPanel>

          <ScreenPanel title="实时告警" class="feed">
            <template #extra>
              <span :class="['live', { stale: isStale }]">{{ isStale ? '数据滞留' : '实时' }}</span>
            </template>
            <div class="feed-scroll">
              <ScrollBoard :items="ov.alarms" :row-key="(a) => a.id" :speed="22">
                <template #row="{ item }">
                  <div class="row" :class="item.level">
                    <span class="dot" />
                    <span class="txt">{{ item.text }}</span>
                    <span class="time">{{ item.time }}</span>
                  </div>
                </template>
              </ScrollBoard>
            </div>
          </ScreenPanel>

          <ScreenPanel title="停机事件" class="feed">
            <div class="feed-scroll">
              <ScrollBoard :items="ov.downtimes" :row-key="(a) => a.id" :speed="18">
                <template #row="{ item }">
                  <div class="row" :class="item.level">
                    <span class="dot" />
                    <span class="txt">{{ item.text }}</span>
                    <span class="time">{{ item.time }}</span>
                  </div>
                </template>
              </ScrollBoard>
            </div>
          </ScreenPanel>
        </div>
      </div>
    </div>
    <div v-else class="fx-loading">连接数据…</div>
  </ScreenLayout>
</template>

<style scoped>
.fx {
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.fx-loading {
  height: 100%;
  display: grid;
  place-content: center;
  color: var(--sb-faint);
  font-size: 15px;
}

/* —— KPI 带 —— */
.band-in {
  display: flex;
  align-items: center;
  gap: 34px;
}
.hero {
  position: relative;
  width: 148px;
  height: 148px;
  flex: none;
}
.hero-ring {
  position: absolute;
  inset: 0;
  border-radius: 50%;
  background: conic-gradient(var(--sb-cyan) calc(var(--p) * 1%), rgba(255, 255, 255, 0.07) 0);
  -webkit-mask: radial-gradient(circle farthest-side, transparent calc(100% - 9px), #000 calc(100% - 8px));
  mask: radial-gradient(circle farthest-side, transparent calc(100% - 9px), #000 calc(100% - 8px));
  filter: drop-shadow(0 0 5px var(--sb-cyan-dim));
}
.hero-c {
  position: absolute;
  inset: 0;
  display: grid;
  place-content: center;
  text-align: center;
}
.hero-v {
  font-size: 42px;
  font-weight: 700;
  line-height: 1;
  color: #fff;
  text-shadow: var(--sb-value-glow);
  font-variant-numeric: tabular-nums;
}
.hero-v small {
  font-size: 19px;
  font-weight: 600;
}
.hero-l {
  margin-top: 6px;
  font-size: 12px;
  color: var(--sb-muted);
}
.band-cells {
  flex: 1;
  display: flex;
  align-items: center;
}
.band-cell {
  flex: 1;
  padding: 6px 26px;
  position: relative;
}
.band-cell + .band-cell::before {
  content: '';
  position: absolute;
  left: 0;
  top: 8px;
  bottom: 8px;
  width: 1px;
  background: var(--sb-divider);
}
.band-v {
  font-size: 36px;
  font-weight: 700;
  line-height: 1;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.band-v.bad {
  color: var(--sb-red);
}
.band-v.warn {
  color: var(--sb-amber);
}
.band-l {
  margin-top: 8px;
  font-size: 13px;
  color: var(--sb-muted);
}
.band-sub {
  margin-left: 4px;
}
.band-sub.bad {
  color: var(--sb-red);
}
.band-sub.warn {
  color: var(--sb-amber);
}

/* —— 主体 —— */
.main {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 1.9fr 1fr;
  gap: 16px;
}
.matrix {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.matrix-grid {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  grid-auto-rows: 1fr;
  gap: 14px;
}
.legend {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: 12.5px;
  color: var(--sb-faint);
}
.legend .lg {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  margin-left: 10px;
}
.legend .lg.green {
  background: var(--sb-green);
}
.legend .lg.yellow {
  background: var(--sb-amber);
}
.legend .lg.red {
  background: var(--sb-red);
}

/* 🟠 占位卡：接入中的指标，虚线边示意非实数据 */
.more-card {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  justify-content: center;
  gap: 9px;
  padding: 18px 20px;
  border-radius: var(--sb-radius);
  border: 1px dashed var(--sb-line-2);
  color: var(--sb-faint);
}
.more-t {
  font-size: 15px;
  color: var(--sb-muted);
}
.more-d {
  font-size: 12.5px;
}

/* —— 右列 —— */
.side {
  display: grid;
  grid-template-rows: auto 1fr 1fr;
  gap: 16px;
  min-height: 0;
}
.rings {
  display: flex;
  justify-content: space-around;
  align-items: center;
  padding: 8px 0 2px;
}
.oee-note {
  margin: 10px 0 0;
  font-size: 12px;
  color: var(--sb-faint);
  text-align: center;
}
.feed {
  display: flex;
  flex-direction: column;
  min-height: 0;
}
.feed-scroll {
  flex: 1;
  min-height: 0;
}
.live {
  font-size: 12px;
  color: var(--sb-green);
}
.live.stale {
  color: var(--sb-amber);
}
.row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 2px;
  border-bottom: 1px solid var(--sb-divider);
  font-size: 15px;
}
.row .dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex: none;
  background: var(--sb-faint);
}
.row.critical .dot {
  background: var(--sb-red);
  box-shadow: 0 0 8px var(--sb-red);
}
.row.warning .dot {
  background: var(--sb-amber);
  box-shadow: 0 0 8px var(--sb-amber);
}
.row .txt {
  flex: 1;
  color: var(--sb-text-2);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.row.critical .txt {
  color: var(--sb-text);
}
.row .time {
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
</style>
