<script setup lang="ts">
import { NvScreenStatusLight } from '@nerv-iip/ui'
import { ArrowRight } from '@lucide/vue'
import type { Component } from 'vue'
import type { ScreenGlance } from '@/data/contracts/launcher'

/**
 * 门厅屏入口卡：图标 + 屏名 + 领域实时一瞥（3 指标）+ 进入指引。
 * 边框刻意收暗（整圈 --nv-scr-line 级，顶边仅比底边亮一丝），单主色 cyan；
 * 状态只经 StatusLight 与数字语气表达 —— 不做彩色边框 / 亮白高光。
 */
defineProps<{
  title: string
  desc: string
  icon: Component
  glance?: ScreenGlance
}>()
</script>

<template>
  <article class="lc">
    <header class="lc-top">
      <span class="lc-ic"><component :is="icon" :size="30" :stroke-width="1.5" /></span>
      <div class="lc-head">
        <h3 class="lc-title">{{ title }}</h3>
        <p class="lc-desc">{{ desc }}</p>
      </div>
      <NvScreenStatusLight
        v-if="glance"
        :tone="glance.state"
        :label="glance.stateLabel"
        class="lc-state"
      />
    </header>

    <dl v-if="glance" class="lc-stats">
      <div v-for="s in glance.stats" :key="s.label" class="lc-stat">
        <dt>{{ s.label }}</dt>
        <dd :class="s.tone">{{ s.value }}</dd>
      </div>
    </dl>
    <div v-else class="lc-stats" aria-hidden="true">
      <div v-for="i in 3" :key="i" class="lc-stat"><span class="lc-skl" /></div>
    </div>

    <div class="lc-zone">
      <template v-if="glance">
        <span class="lc-zone-t">{{ glance.chipsLabel }}</span>
        <div class="lc-chips">
          <span v-for="c in glance.chips" :key="c.label" class="lc-chip" :class="c.tone">
            <i class="lc-chip-dot" />{{ c.label }}
          </span>
        </div>
      </template>
    </div>

    <footer class="lc-foot">
      <span>进入大屏</span>
      <ArrowRight :size="17" class="lc-arrow" aria-hidden="true" />
    </footer>
  </article>
</template>

<style scoped>
@layer app {
  .lc {
    height: 100%;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    padding: 30px 32px 24px;
    border-radius: var(--nv-scr-radius);
    background: linear-gradient(180deg, var(--nv-scr-panel-a), var(--nv-scr-panel-b));
    /* 评审定调：整圈暗边，顶边只比底边亮一丝 */
    border: 1px solid var(--nv-scr-line);
    border-top-color: rgba(255, 255, 255, 0.1);
    transition:
      border-color 0.2s var(--nv-scr-ease),
      transform 0.12s var(--nv-scr-ease);
  }
  .lc:hover {
    border-color: rgba(135, 208, 255, 0.26);
    border-top-color: rgba(135, 208, 255, 0.34);
  }
  .lc:active {
    transform: scale(0.985);
  }

  .lc-top {
    display: flex;
    align-items: flex-start;
    gap: 16px;
  }
  .lc-ic {
    width: 54px;
    height: 54px;
    border-radius: 10px;
    display: grid;
    place-items: center;
    flex: none;
    color: var(--nv-scr-cyan);
    background: rgba(74, 166, 238, 0.08);
    border: 1px solid rgba(74, 166, 238, 0.2);
  }
  .lc-title {
    margin: 0;
    font-size: 26px;
    font-weight: 600;
    color: #fff;
    letter-spacing: 0.02em;
  }
  .lc-desc {
    margin: 5px 0 0;
    font-size: 14px;
    color: var(--nv-scr-muted);
  }
  .lc-state {
    margin-left: auto;
    padding-top: 4px;
  }

  .lc-stats {
    display: flex;
    flex-direction: column;
    margin: 22px 0 0;
  }
  .lc-stat {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    padding: 18px 2px;
  }
  .lc-stat + .lc-stat {
    border-top: 1px solid var(--nv-scr-divider);
  }
  .lc-stat dt {
    font-size: 16px;
    color: var(--nv-scr-muted);
  }
  .lc-stat dd {
    margin: 0;
    font-size: 32px;
    font-weight: 700;
    font-variant-numeric: tabular-nums;
    color: var(--nv-scr-text);
  }
  .lc-stat dd.ok {
    color: var(--nv-scr-green);
  }
  .lc-stat dd.warn {
    color: var(--nv-scr-amber);
  }
  .lc-stat dd.bad {
    color: var(--nv-scr-red);
  }
  .lc-skl {
    height: 12px;
    width: 100%;
    border-radius: 3px;
    background: rgba(255, 255, 255, 0.05);
  }

  /* 成员导航区：吃掉卡片剩余高度，chips 顶部对齐 */
  .lc-zone {
    flex: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 12px;
    margin: 24px 0 18px;
    padding-top: 18px;
    border-top: 1px solid var(--nv-scr-divider);
  }
  .lc-zone-t {
    font-size: 13px;
    color: var(--nv-scr-faint);
    letter-spacing: 0.06em;
  }
  .lc-chips {
    display: flex;
    flex-wrap: wrap;
    align-content: flex-start;
    gap: 9px;
    overflow: hidden;
  }
  .lc-chip {
    display: inline-flex;
    align-items: center;
    gap: 7px;
    height: 27px;
    padding: 0 12px;
    border-radius: 999px;
    font-size: 13px;
    color: var(--nv-scr-text-2);
    border: 1px solid rgba(255, 255, 255, 0.08);
    background: rgba(255, 255, 255, 0.02);
  }
  .lc-chip-dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    flex: none;
  }
  .lc-chip.run .lc-chip-dot {
    background: var(--nv-scr-green);
    box-shadow: 0 0 5px var(--nv-scr-green);
  }
  .lc-chip.idle .lc-chip-dot {
    background: var(--nv-scr-amber);
    box-shadow: 0 0 5px var(--nv-scr-amber);
  }
  .lc-chip.alarm {
    border-color: rgba(239, 90, 99, 0.35);
    color: var(--nv-scr-text);
  }
  .lc-chip.alarm .lc-chip-dot {
    background: var(--nv-scr-red);
    box-shadow: 0 0 5px var(--nv-scr-red);
  }
  .lc-chip.off {
    color: var(--nv-scr-faint);
  }
  .lc-chip.off .lc-chip-dot {
    background: var(--nv-scr-faint);
  }

  .lc-foot {
    display: flex;
    align-items: center;
    justify-content: space-between;
    border-top: 1px solid var(--nv-scr-divider);
    padding-top: 16px;
    font-size: 14px;
    color: var(--nv-scr-faint);
    transition: color 0.2s var(--nv-scr-ease);
  }
  .lc-arrow {
    transition: transform 0.2s var(--nv-scr-ease);
  }
  .lc:hover .lc-foot {
    color: var(--nv-scr-cyan);
  }
  .lc:hover .lc-arrow {
    transform: translateX(4px);
  }

  @media (prefers-reduced-motion: reduce) {
    .lc,
    .lc-foot,
    .lc-arrow {
      transition: none;
    }
    .lc:hover .lc-arrow {
      transform: none;
    }
  }
}
</style>
