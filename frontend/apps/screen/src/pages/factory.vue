<script setup lang="ts">
import { RingGauge, StatusTag } from '@nerv-iip/ui'
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

// —— 顶部 KPI 带右侧单元（语义色只落在异常数字上；文案全中文业务词）——
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
    { label: '未结停机', value: String(k.openDowntime), tone: k.openDowntime > 0 ? 'warn' : undefined },
    { label: '未结不良单', value: String(k.openNcr), tone: k.openNcr > 0 ? 'warn' : undefined },
  ]
})
</script>

<template>
  <ScreenLayout title="Nerv-IIP 工厂运营大屏" :line="factoryName" screen="指挥中心大屏 01">
    <div v-if="ov" class="fx">
      <!-- 全厂 KPI 带：达成率大号进度环为焦点 -->
      <section class="sec band">
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
      </section>

      <div class="main">
        <!-- 车间状态矩阵：无外壳容器（卡片直接浮在氛围底上），红卡置顶 -->
        <section class="matrix-wrap">
          <div class="sec-h">
            <i class="sec-glyph" aria-hidden="true" />
            <span class="sec-t">车间运行状态</span>
            <span class="sec-rule" aria-hidden="true" />
            <span class="legend">
              <i class="lg green" />正常
              <i class="lg yellow" />关注 · 停机/达成率低
              <i class="lg red" />异常 · 严重告警/超期
            </span>
          </div>
          <div class="matrix-grid">
            <WorkshopHealthCard v-for="w in ov.workshops" :key="w.id" :cell="w" />
            <div class="more-card">
              <span class="more-t">更多指标接入中</span>
              <span class="more-d">不良率 · 设备在线率 · 产出良率趋势</span>
              <StatusTag tone="amber" label="待 #570" />
            </div>
          </div>
        </section>

        <div class="side">
          <section class="sec">
            <div class="sec-h">
              <i class="sec-glyph" aria-hidden="true" />
              <span class="sec-t">综合效率 OEE</span>
              <span class="sec-rule" aria-hidden="true" />
              <StatusTag tone="amber" label="综合 ≈ 可用率 · 待 #570" />
            </div>
            <div class="rings">
              <RingGauge v-for="o in ov.oee" :key="o.label" :value="o.value" :label="o.label" :size="108" />
            </div>
            <p class="oee-note">性能率 / 良品率为占位值，#570 接入后启用真实综合 OEE</p>
          </section>

          <section class="sec feed">
            <div class="sec-h">
              <i class="sec-glyph" aria-hidden="true" />
              <span class="sec-t">实时告警</span>
              <span class="sec-rule" aria-hidden="true" />
              <span :class="['live', { stale: isStale }]">{{ isStale ? '数据滞留' : '实时' }}</span>
            </div>
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
          </section>

          <section class="sec feed">
            <div class="sec-h">
              <i class="sec-glyph" aria-hidden="true" />
              <span class="sec-t">停机事件</span>
              <span class="sec-rule" aria-hidden="true" />
            </div>
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
          </section>
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

/* —— 通透容器：低透明度背景让氛围底透出来，边框整圈近暗、顶边微亮 —— */
.sec {
  background: linear-gradient(180deg, rgba(20, 32, 58, 0.34), rgba(8, 14, 27, 0.26));
  border: 1px solid rgba(148, 190, 255, 0.1);
  border-top-color: rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  padding: 15px 18px;
}

/* —— 特效区块标题：斜切能量块 + 发光标题字 + 渐隐引导线 —— */
.sec-h {
  display: flex;
  align-items: center;
  gap: 11px;
  margin-bottom: 12px;
  min-height: 24px;
}
.sec-glyph {
  width: 9px;
  height: 19px;
  flex: none;
  border-radius: 2px;
  transform: skewX(-16deg);
  background: linear-gradient(180deg, var(--sb-cyan), rgba(74, 166, 238, 0.25));
  box-shadow: 0 0 12px rgba(74, 166, 238, 0.55);
}
.sec-t {
  font-size: 19px;
  font-weight: 700;
  letter-spacing: 0.14em;
  color: #fff;
  text-shadow: 0 0 18px rgba(96, 180, 255, 0.45);
  white-space: nowrap;
}
.sec-rule {
  flex: 1;
  height: 1px;
  margin: 0 8px;
  background: linear-gradient(90deg, rgba(135, 208, 255, 0.3), rgba(255, 255, 255, 0.05) 45%, transparent);
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
.matrix-wrap {
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
  white-space: nowrap;
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
  padding: 6px 0 0;
}
.oee-note {
  margin: 9px 0 0;
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
