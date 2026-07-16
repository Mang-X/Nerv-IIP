<script setup lang="ts">
import { NvKpiBar, NvSparkline, NvScreenStatusLight, useScreenData } from '@nerv-iip/ui'
import { useVirtualList } from '@vueuse/core'
import { Activity, AlertTriangle, Factory, PackageCheck, Workflow } from '@lucide/vue'
import { type Component, computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import { useBackLink } from '@/composables/useBackLink'
import type { LineSummaryCard } from '@/data/contracts/line'
import { fetchLineCards } from '@/data/fetchers/line'
import ScreenLayout from '@/layouts/ScreenLayout.vue'

// 产线选择器 = 迷你监控板（spec §四）：红线置顶（数据层排序）、scope 收窄、
// 点卡进入单线大屏。行虚拟滚动 + 视野外停止请求趋势序列。4s 轮询。
const scope = useAccessScope()
const backLink = useBackLink(() => ({ to: '/', label: '返回大屏门厅' }))
const visibleIds = ref<string[]>([])
const { data: cards, refresh } = useScreenData<LineSummaryCard[]>(
  () =>
    fetchLineCards(
      scope.currentFactoryId,
      scope.persona.workshopIds,
      visibleIds.value.length ? visibleIds.value : undefined,
    ),
  { intervalMs: 4000 },
)
watch(
  () => [scope.currentFactoryId, scope.personaId],
  () => {
    void refresh()
  },
)

// —— 行虚拟滚动（每行 3 卡）——
const COLS = 3
const rowsSrc = computed(() => {
  const list = cards.value ?? []
  const out: LineSummaryCard[][] = []
  for (let i = 0; i < list.length; i += COLS) out.push(list.slice(i, i + COLS))
  return out
})
// 行高：卡片自然高 ~258 + 行间距 16 → itemHeight 必须精确匹配，否则虚拟定位错位重叠
const CARD_H = 258
const ROW_GAP = 16
const {
  list: vRows,
  containerProps,
  wrapperProps,
} = useVirtualList(rowsSrc, {
  itemHeight: CARD_H + ROW_GAP,
  overscan: 1,
})
// 视野内产线集：滚动改变可见行 → 立即补取（避免滚入卡趋势短暂空白）
watch(
  () => vRows.value.map((r) => r.data.map((c) => c.id).join(',')).join('|'),
  () => {
    const ids = vRows.value.flatMap((r) => r.data.map((c) => c.id))
    const changed = ids.some((id) => !visibleIds.value.includes(id))
    visibleIds.value = ids
    if (changed) void refresh()
  },
)

const factoryName = computed(
  () => scope.factories.find((f) => f.id === scope.currentFactoryId)?.name ?? '全部车间',
)

const toneOf = (s: LineSummaryCard['state']) =>
  s === 'alarm' ? ('alarm' as const) : s === 'attention' ? ('idle' as const) : ('run' as const)

const stateColor = (s: LineSummaryCard['state']) =>
  s === 'alarm'
    ? 'var(--nv-scr-red)'
    : s === 'attention'
      ? 'var(--nv-scr-amber)'
      : 'var(--nv-scr-cyan)'

// —— 顶部产线汇总带（卡片数据 rollup）——
interface KpiCell {
  icon?: Component
  value: string
  label: string
  tone?: 'cyan' | 'amber' | 'green'
  ring?: number
}
const nf = new Intl.NumberFormat('en-US')
const kpiItems = computed<KpiCell[]>(() => {
  const list = cards.value
  if (!list?.length) return []
  const alarm = list.filter((c) => c.state === 'alarm').length
  const attention = list.filter((c) => c.state === 'attention').length
  const good = list.reduce((n, c) => n + c.output.good, 0)
  const plan = list.reduce((n, c) => n + c.output.plan, 0)
  const avg = plan > 0 ? Math.round((good / plan) * 100) : 0
  return [
    {
      icon: Workflow,
      value: `${list.length - alarm - attention}/${list.length}`,
      label: '正常作业产线',
    },
    {
      icon: AlertTriangle,
      value: String(alarm),
      label: '报警产线',
      tone: alarm > 0 ? 'amber' : undefined,
    },
    {
      icon: Activity,
      value: String(attention),
      label: '需关注产线',
      tone: attention > 0 ? 'amber' : undefined,
    },
    { icon: PackageCheck, value: nf.format(good), label: '当班总产量（件）' },
    { value: `${avg}%`, label: '当班平均达成', tone: 'cyan', ring: avg },
  ]
})
</script>

<template>
  <ScreenLayout title="Nerv-IIP 产线监控大屏" :line="factoryName" screen="指挥中心大屏 03">
    <div v-if="cards" class="ls">
      <div class="ls-kpi">
        <NvKpiBar v-if="kpiItems.length" :items="kpiItems" />
      </div>

      <div class="sec-h">
        <i class="sec-glyph" aria-hidden="true" />
        <span class="sec-t">产线状态总览</span>
        <span class="sec-rule" aria-hidden="true" />
        <span class="ls-meta">{{ cards.length }} 条产线 · 点击进入单线大屏</span>
      </div>

      <div v-bind="containerProps" class="ls-scroll nv-scr-scroll">
        <div v-bind="wrapperProps">
          <div v-for="row in vRows" :key="row.index" class="ls-row">
            <RouterLink v-for="c in row.data" :key="c.id" :to="`/line/${c.id}`" class="ls-link">
              <article class="ls-card" :class="c.state">
                <header class="ls-top">
                  <NvScreenStatusLight :tone="toneOf(c.state)" :label="c.stateLabel" />
                  <span v-if="c.offlineDevices > 0" class="ls-off"
                    >{{ c.offlineDevices }} 台失联</span
                  >
                </header>
                <h3 class="ls-name">
                  <span class="ls-ic"><Factory :size="19" :stroke-width="1.6" /></span>{{ c.name }}
                </h3>
                <p class="ls-ws">
                  {{ c.workshopName }}<template v-if="c.currentWo"> · {{ c.currentWo }}</template>
                </p>
                <div class="ls-nums">
                  <div>
                    <dt>当班达成</dt>
                    <dd :class="{ bad: c.achievement < 85 }">
                      {{ c.achievement }}<small>%</small>
                    </dd>
                  </div>
                  <div>
                    <dt>节拍偏差</dt>
                    <dd
                      :class="{
                        bad: c.taktDeviationPct > 8,
                        warn: c.taktDeviationPct > 0 && c.taktDeviationPct <= 8,
                      }"
                    >
                      {{ c.taktDeviationPct > 0 ? '+' : '' }}{{ c.taktDeviationPct
                      }}<small>%</small>
                    </dd>
                  </div>
                  <div>
                    <dt>当班产量</dt>
                    <dd class="ls-out">
                      {{ nf.format(c.output.good) }}<small>/ {{ nf.format(c.output.plan) }}</small>
                    </dd>
                  </div>
                </div>
                <div class="ls-spark">
                  <NvSparkline :data="c.hourly" area :color="stateColor(c.state)" />
                </div>
                <div class="ls-dots" :aria-label="`设备 ${c.deviceDots.length} 台`">
                  <i v-for="(d, i) in c.deviceDots" :key="i" class="ls-dot" :class="d" />
                  <span class="ls-dots-n">{{ c.deviceDots.length }} 台</span>
                </div>
                <p class="ls-alert" :class="c.state">{{ c.alert ?? '作业平稳' }}</p>
              </article>
            </RouterLink>
          </div>
        </div>
      </div>

      <footer class="scr-foot">
        <RouterLink :to="backLink.to" class="scr-back">‹ {{ backLink.label }}</RouterLink>
        <span>产线状态与设备屏同源 · 产量 / 节拍为演示推算 · 待 #570；点卡进入单线屏</span>
      </footer>
    </div>
    <div v-else class="ls-loading">连接数据…</div>
  </ScreenLayout>
</template>

<style scoped>
@layer app {
  .ls {
    height: 100%;
    min-height: 0;
    display: flex;
    flex-direction: column;
  }
  .ls-loading {
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
    margin-top: 12px;
    font-size: 12.5px;
    color: var(--nv-scr-faint);
  }
  .scr-back {
    color: var(--nv-scr-cyan);
    text-decoration: none;
    font-size: 13.5px;
    flex: none;
  }
  .sec-h {
    display: flex;
    align-items: center;
    gap: 11px;
    margin-bottom: 14px;
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
  .ls-meta {
    font-size: 13px;
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
  }

  /* 虚拟滚动容器：flex:1 + min-height:0 收进画布，仅此处滚动（修幽灵滚动条）；
   overflow-x hidden + scrollbar-gutter stable 消除横/竖条抖动闪烁 */
  .ls-scroll {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    overflow-x: hidden;
    scrollbar-gutter: stable;
  }
  /* 行高 = CARD_H(258) + ROW_GAP(16)，与 itemHeight 精确一致 → 不重叠 */
  .ls-row {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 14px;
    height: 258px;
    margin-bottom: 16px;
  }
  .ls-link {
    display: block;
    min-width: 0;
    text-decoration: none;
    border-radius: var(--nv-scr-radius);
  }
  .ls-link:focus-visible {
    outline: none;
    box-shadow:
      0 0 0 2px var(--nv-scr-bg),
      0 0 0 4px var(--nv-scr-cyan-dim);
  }
  .ls-card {
    height: 100%;
    box-sizing: border-box;
    overflow: hidden;
    display: flex;
    flex-direction: column;
    padding: 14px 17px 12px;
    border-radius: var(--nv-scr-radius);
    background: linear-gradient(180deg, var(--nv-scr-panel-a), var(--nv-scr-panel-b));
    border: 1px solid var(--nv-scr-line);
    border-top-color: rgba(255, 255, 255, 0.09);
    transition:
      border-color 0.18s var(--nv-scr-ease),
      transform 0.12s var(--nv-scr-ease);
  }
  .ls-link:hover .ls-card {
    border-color: rgba(135, 208, 255, 0.3);
  }
  .ls-link:active .ls-card {
    transform: scale(0.985);
  }
  .ls-card.alarm {
    border-color: rgba(239, 90, 99, 0.4);
    position: relative;
  }
  .ls-card.alarm::after {
    content: '';
    position: absolute;
    inset: -1px;
    border-radius: inherit;
    pointer-events: none;
    box-shadow: 0 0 16px -4px rgba(239, 90, 99, 0.6);
    animation: ls-alarm 1.8s ease-in-out infinite;
  }
  @keyframes ls-alarm {
    50% {
      opacity: 0.25;
    }
  }

  .ls-top {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
  }
  .ls-off {
    padding: 2px 8px;
    border-radius: 5px;
    border: 1px dashed rgba(255, 255, 255, 0.24);
    background: repeating-linear-gradient(
      -45deg,
      rgba(255, 255, 255, 0.04) 0 6px,
      transparent 6px 12px
    );
    font-size: 11.5px;
    color: var(--nv-scr-muted);
  }
  .ls-name {
    margin: 8px 0 0;
    font-size: 22px;
    font-weight: 700;
    color: #fff;
    letter-spacing: 0.04em;
    display: inline-flex;
    align-items: center;
    gap: 9px;
  }
  /* 产线名 leading 图标：发丝级去饱和线性符号（无填充块/边框/发光）。
   状态由状态灯 + 卡片边框 + 异常文案表达，图标只作类型标识、克制陪衬。 */
  .ls-ic {
    flex: none;
    display: inline-flex;
    color: var(--nv-scr-muted);
  }
  .ls-ws {
    margin: 4px 0 0;
    font-size: 12.5px;
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
  }
  .ls-kpi {
    flex: none;
    margin-bottom: 14px;
  }
  .ls-nums {
    display: grid;
    grid-template-columns: 1fr 1fr 1.3fr;
    gap: 10px;
    margin: 9px 0 0;
  }
  .ls-out small {
    color: var(--nv-scr-muted);
  }
  .ls-spark {
    height: 26px;
    margin: 8px 0 5px;
  }
  .ls-dots {
    display: flex;
    align-items: center;
    gap: 5px;
    flex-wrap: wrap;
  }
  .ls-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--nv-scr-faint);
  }
  .ls-dot.run {
    background: var(--nv-scr-green);
    box-shadow: 0 0 6px var(--nv-scr-green);
  }
  .ls-dot.idle {
    background: var(--nv-scr-amber);
    box-shadow: 0 0 6px var(--nv-scr-amber);
  }
  .ls-dot.alarm {
    background: var(--nv-scr-red);
    box-shadow: 0 0 6px var(--nv-scr-red);
  }
  .ls-dot.down {
    background: var(--nv-scr-muted);
  }
  .ls-dots-n {
    margin-left: 4px;
    font-size: 11.5px;
    color: var(--nv-scr-faint);
    font-variant-numeric: tabular-nums;
  }
  .ls-nums dt {
    font-size: 12px;
    color: var(--nv-scr-muted);
  }
  .ls-nums dd {
    margin: 3px 0 0;
    font-size: 23px;
    font-weight: 700;
    font-variant-numeric: tabular-nums;
    color: var(--nv-scr-text);
  }
  .ls-nums dd small {
    font-size: 13px;
    font-weight: 600;
    margin-left: 1px;
  }
  .ls-nums dd.warn {
    color: var(--nv-scr-amber);
  }
  .ls-nums dd.bad {
    color: var(--nv-scr-red);
  }
  .ls-alert {
    margin: auto 0 0;
    padding-top: 6px;
    font-size: 12.5px;
    color: var(--nv-scr-faint);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .ls-alert.alarm {
    color: var(--nv-scr-red);
  }
  .ls-alert.attention {
    color: var(--nv-scr-amber);
  }

  @media (prefers-reduced-motion: reduce) {
    .ls-card {
      transition: none;
    }
    .ls-card.alarm::after {
      animation: none;
    }
  }
}
</style>
