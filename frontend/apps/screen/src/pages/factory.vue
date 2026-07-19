<script setup lang="ts">
import {
  NvRingGauge,
  NvScreenFreshness,
  NvScreenPanel,
  NvScrollBoard,
  NvScreenStatusTag,
  useScreenData,
} from '@nerv-iip/ui'
import {
  AlertTriangle,
  CalendarClock,
  ClipboardList,
  FileWarning,
  PackageCheck,
  PowerOff,
  Target,
} from '@lucide/vue'
import { type Component, computed, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import { useBackLink } from '@/composables/useBackLink'
import WorkshopHealthCard from '@/components/factory/WorkshopHealthCard.vue'
import type { FactoryOverview } from '@/data/contracts/factory'
import { FACTORY_OEE_PLACEHOLDER_BADGE } from '@/data/copy'
import { fetchFactoryOverview } from '@/data/fetchers/factory'
import { formatScreenFreshness } from '@/data/freshness'
import ScreenLayout from '@/layouts/ScreenLayout.vue'

const scope = useAccessScope()
const backLink = useBackLink(() => ({ to: '/', label: '返回大屏门厅' }))
const {
  data: ov,
  isStale,
  lastUpdated,
  refresh,
} = useScreenData<FactoryOverview>(
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

const nf = new Intl.NumberFormat('en-US')
const freshness = computed(() => formatScreenFreshness(isStale.value, lastUpdated.value))

// —— 顶部 KPI 带（达成率环 + 7 格；图标块承接语义色，异常数字同色）——
interface BandCell {
  label: string
  value: string
  icon: Component
  sub?: string
  tone?: 'bad' | 'warn'
}
const bandCells = computed<BandCell[]>(() => {
  const k = ov.value?.kpis
  if (!k) return []
  return [
    { label: '今日产量（件）', value: nf.format(k.todayOutput), icon: PackageCheck },
    { label: '计划产量（件）', value: nf.format(k.todayPlan), icon: Target },
    { label: '在产工单', value: String(k.wipOrders), icon: ClipboardList },
    {
      label: '超期 / 风险工单',
      value: String(k.riskOrders),
      icon: CalendarClock,
      tone: k.riskOrders > 0 ? 'bad' : undefined,
    },
    {
      label: '未恢复告警',
      value: String(k.openAlarms),
      icon: AlertTriangle,
      sub: `严重 ${k.criticalAlarms}`,
      tone: k.criticalAlarms > 0 ? 'bad' : k.openAlarms > 0 ? 'warn' : undefined,
    },
    {
      label: '未结停机',
      value: String(k.openDowntime),
      icon: PowerOff,
      tone: k.openDowntime > 0 ? 'warn' : undefined,
    },
    {
      label: '未结不良单',
      value: String(k.openNcr),
      icon: FileWarning,
      tone: k.openNcr > 0 ? 'warn' : undefined,
    },
  ]
})
</script>

<template>
  <ScreenLayout title="Nerv-IIP 工厂运营大屏" :line="factoryName" screen="指挥中心大屏 01">
    <div v-if="ov" class="fx">
      <!-- 全厂 KPI 带：达成率大环（与 OEE 三环同款圆角弧）+ 七格语义数字 -->
      <NvScreenPanel class="band">
        <div class="band-in">
          <NvRingGauge
            class="band-hero"
            :value="ov.kpis.achievement"
            label="今日全厂达成率"
            :size="150"
            :value-size="38"
          />
          <div class="band-cells">
            <div v-for="c in bandCells" :key="c.label" class="band-cell">
              <span class="band-ic" :class="c.tone">
                <component :is="c.icon" :size="19" :stroke-width="1.8" />
              </span>
              <div class="band-txt">
                <div class="band-v" :class="c.tone">{{ c.value }}</div>
                <div class="band-l">
                  {{ c.label
                  }}<span v-if="c.sub" class="band-sub" :class="c.tone">· {{ c.sub }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </NvScreenPanel>

      <div class="main">
        <!-- 车间状态矩阵：无外壳（卡片直接浮在舱底上，不做卡套卡），红卡置顶 -->
        <section class="matrix-wrap">
          <div class="sec-h">
            <i class="sec-glyph" aria-hidden="true" />
            <span class="sec-t">车间运行状态</span>
            <span class="sec-rule" aria-hidden="true" />
            <span class="legend">
              <i class="lg green" />正常 <i class="lg yellow" />关注 · 停机/达成率低
              <i class="lg red" />异常 · 严重告警/超期
            </span>
          </div>
          <div class="matrix-grid">
            <!-- 可看车间总览屏时，车间卡可点击下钻（M2 · /workshop/[id]） -->
            <component
              :is="scope.canSeeScreen('workshop') ? RouterLink : 'div'"
              v-for="w in ov.workshops"
              :key="w.id"
              :to="scope.canSeeScreen('workshop') ? `/workshop/${w.id}` : undefined"
              class="matrix-cell-link"
            >
              <WorkshopHealthCard :cell="w" />
            </component>
            <div class="more-card">
              <span class="more-t">更多指标接入中</span>
              <span class="more-d">不良率 · 设备在线率 · 产出良率趋势</span>
              <NvScreenStatusTag tone="amber" label="待 #570" />
            </div>
          </div>
        </section>

        <div class="side">
          <NvScreenPanel title="综合效率 OEE">
            <template #extra>
              <NvScreenStatusTag tone="amber" :label="FACTORY_OEE_PLACEHOLDER_BADGE" />
            </template>
            <p class="oee-note">请在设备详情查看由报工、运行时长和理论速率计算的真实单机 OEE。</p>
            <p class="oee-note">
              全厂聚合仍需批量 OEE 读面，当前不以可用率或演示数据冒充综合 OEE。
            </p>
          </NvScreenPanel>

          <NvScreenPanel title="实时告警" class="feed">
            <template #extra>
              <span :class="['live', { stale: isStale }]">{{ isStale ? '数据滞留' : '实时' }}</span>
            </template>
            <div class="feed-scroll">
              <NvScrollBoard :items="ov.alarms" :row-key="(a) => a.id" :speed="22">
                <template #row="{ item }">
                  <div class="row" :class="item.level">
                    <span class="dot" />
                    <span class="txt">{{ item.text }}</span>
                    <span class="time">{{ item.time }}</span>
                  </div>
                </template>
              </NvScrollBoard>
            </div>
          </NvScreenPanel>

          <NvScreenPanel title="停机事件" class="feed">
            <div class="feed-scroll">
              <NvScrollBoard :items="ov.downtimes" :row-key="(a) => a.id" :speed="18">
                <template #row="{ item }">
                  <div class="row" :class="item.level">
                    <span class="dot" />
                    <span class="txt">{{ item.text }}</span>
                    <span class="time">{{ item.time }}</span>
                  </div>
                </template>
              </NvScrollBoard>
            </div>
          </NvScreenPanel>
        </div>
      </div>

      <footer class="scr-foot">
        <RouterLink :to="backLink.to" class="scr-back">‹ {{ backLink.label }}</RouterLink>
        <span>车间归并 / 达成为前端聚合推算 · 待 #570；点车间卡进入车间总览</span>
        <NvScreenFreshness :tone="freshness.tone" :label="freshness.text" />
      </footer>
    </div>
    <div v-else class="fx-loading">连接数据…</div>
  </ScreenLayout>
</template>

<style scoped>
@layer app {
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

  /* —— KPI 带 —— */
  .band-in {
    display: flex;
    align-items: center;
    gap: 30px;
  }
  .band-hero {
    flex: none;
  }
  .band-cells {
    flex: 1;
    display: flex;
    align-items: center;
  }
  .band-cell {
    flex: 1;
    padding: 6px 18px;
    position: relative;
    display: flex;
    align-items: center;
    gap: 13px;
  }
  .band-cell + .band-cell::before {
    content: '';
    position: absolute;
    left: 0;
    top: 8px;
    bottom: 8px;
    width: 1px;
    background: var(--nv-scr-divider);
  }
  /* 指标图标块：语义色随异常态（中性 / 青 / 黄 / 红） */
  .band-ic {
    width: 38px;
    height: 38px;
    border-radius: 9px;
    display: grid;
    place-items: center;
    flex: none;
    color: var(--nv-scr-text-2);
    background: rgba(255, 255, 255, 0.045);
    border: 1px solid rgba(255, 255, 255, 0.09);
  }
  .band-ic.warn {
    color: var(--nv-scr-amber);
    background: rgba(242, 193, 78, 0.09);
    border-color: rgba(242, 193, 78, 0.24);
  }
  .band-ic.bad {
    color: var(--nv-scr-red);
    background: rgba(239, 90, 99, 0.1);
    border-color: rgba(239, 90, 99, 0.26);
  }
  .band-txt {
    min-width: 0;
  }
  .band-v {
    font-size: 31px;
    font-weight: 700;
    line-height: 1;
    color: var(--nv-scr-text);
    font-variant-numeric: tabular-nums;
  }
  .band-v.bad {
    color: var(--nv-scr-red);
  }
  .band-v.warn {
    color: var(--nv-scr-amber);
  }
  .band-l {
    margin-top: 8px;
    font-size: 13px;
    color: var(--nv-scr-muted);
    white-space: nowrap;
  }
  .band-sub {
    margin-left: 4px;
  }
  .band-sub.bad {
    color: var(--nv-scr-red);
  }
  .band-sub.warn {
    color: var(--nv-scr-amber);
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
  /* 车间卡下钻包装：填满格子，交互态交给卡片自身的 hover 语言 */
  .matrix-cell-link {
    display: block;
    min-height: 0;
    text-decoration: none;
    color: inherit;
    border-radius: var(--nv-scr-radius);
  }
  .matrix-cell-link:is(a) {
    cursor: pointer;
  }
  .matrix-cell-link:is(a):hover :deep(.whc) {
    border-color: rgba(135, 208, 255, 0.26);
    border-top-color: rgba(135, 208, 255, 0.34);
  }
  .matrix-cell-link:focus-visible {
    outline: none;
    box-shadow:
      0 0 0 2px var(--nv-scr-bg),
      0 0 0 4px var(--nv-scr-cyan-dim);
  }
  .matrix-cell-link > :deep(.whc) {
    height: 100%;
  }

  /* 区块标题（无外壳区域用）：与 ScreenPanel 升级后的标题同款语言 */
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
  .legend {
    display: inline-flex;
    align-items: center;
    gap: 7px;
    font-size: 12.5px;
    color: var(--nv-scr-muted);
    white-space: nowrap;
  }
  .legend .lg {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    margin-left: 10px;
  }
  .legend .lg.green {
    background: var(--nv-scr-green);
  }
  .legend .lg.yellow {
    background: var(--nv-scr-amber);
  }
  .legend .lg.red {
    background: var(--nv-scr-red);
  }

  /* 🟠 占位卡：接入中的指标，虚线边示意非实数据 */
  .more-card {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    justify-content: center;
    gap: 9px;
    padding: 18px 20px;
    border-radius: var(--nv-scr-radius);
    border: 1px dashed var(--nv-scr-line-2);
    color: var(--nv-scr-muted);
  }
  .more-t {
    font-size: 15px;
    color: var(--nv-scr-text-2);
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
    padding: 4px 0 0;
  }
  .oee-note {
    margin: 9px 0 0;
    font-size: 12px;
    color: var(--nv-scr-muted);
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
    color: var(--nv-scr-green);
  }
  .live.stale {
    color: var(--nv-scr-amber);
  }
  .row {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 9px 2px;
    border-bottom: 1px solid var(--nv-scr-divider);
    font-size: 15px;
  }
  .row .dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    flex: none;
    background: var(--nv-scr-faint);
  }
  .row.critical .dot {
    background: var(--nv-scr-red);
    box-shadow: 0 0 8px var(--nv-scr-red);
  }
  .row.warning .dot {
    background: var(--nv-scr-amber);
    box-shadow: 0 0 8px var(--nv-scr-amber);
  }
  .row .txt {
    flex: 1;
    color: var(--nv-scr-text-2);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .row.critical .txt {
    color: var(--nv-scr-text);
  }
  .row .time {
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
  }
}
</style>
