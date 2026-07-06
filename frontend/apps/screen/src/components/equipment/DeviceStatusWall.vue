<script setup lang="ts">
import { computed } from 'vue'
import type { DeviceCell, StateCounts } from '@/data/contracts/equipment'

/**
 * 设备状态全景墙（spec §三 + 生产走查交互深化）：
 * 三种排列/展现形式 —— 平铺（大格带关键参数）/ 按车间（分组紧凑格）/
 * 按产线（行式，每线一行两机）。五态视觉：运行绿/待机黄/停机灰/报警红缓闪/
 * 断线灰斜纹（IsSourceFresh 防假绿）。每台可点击（emit select）查看详情。
 */
const props = defineProps<{
  devices: DeviceCell[]
  counts: StateCounts
  view: 'flat' | 'workshop' | 'line'
}>()

const emit = defineEmits<{ select: [device: DeviceCell] }>()

const countItems = computed(() => [
  { k: 'run', label: '运行', v: props.counts.run },
  { k: 'idle', label: '待机', v: props.counts.idle },
  { k: 'down', label: '停机', v: props.counts.down },
  { k: 'alarm', label: '报警', v: props.counts.alarm },
  { k: 'offline', label: '断线', v: props.counts.offline },
])

function groupBy(key: (d: DeviceCell) => string): [string, DeviceCell[]][] {
  const m = new Map<string, DeviceCell[]>()
  for (const d of props.devices) {
    const k = key(d)
    const arr = m.get(k) ?? []
    arr.push(d)
    m.set(k, arr)
  }
  return [...m.entries()]
}
const byWorkshop = computed(() => groupBy((d) => d.workshopName))
const byLine = computed(() => groupBy((d) => d.lineName))

/** 行式 chip 上的一句话：阻塞原因优先，否则第一个关键参数。 */
function chipText(d: DeviceCell): string {
  if (d.block) return d.block
  const p = d.params[0]
  return p ? `${p.label} ${p.value}` : d.stateLabel
}
function kindOf(d: DeviceCell): string {
  return d.name.includes('主机') ? '主机' : '辅机'
}
</script>

<template>
  <div class="dsw-root">
    <!-- 顶部五态计数（与墙体逐台归并一致，单测对账） -->
    <div class="dsw-counts">
      <span v-for="c in countItems" :key="c.k" class="count" :class="c.k">
        <i />{{ c.label }} <b>{{ c.v }}</b>
      </span>
    </div>

    <!-- 平铺：大格带关键参数 -->
    <div v-if="view === 'flat'" class="dsw dsw--flat">
      <button
        v-for="d in devices"
        :key="d.id"
        type="button"
        class="dsw-cell"
        :class="d.state"
        @click="emit('select', d)"
      >
        <header class="dsw-top">
          <h5 class="dsw-name">{{ d.name }}</h5>
          <span class="dsw-state" :class="d.state"><i />{{ d.stateLabel }}</span>
        </header>
        <p class="dsw-code">{{ d.code }}</p>
        <dl class="dsw-params">
          <div v-for="p in d.params" :key="p.label">
            <dt>{{ p.label }}</dt>
            <dd :class="p.tone">{{ p.value }}</dd>
          </div>
        </dl>
        <p v-if="d.block" class="dsw-block" :class="d.state">{{ d.block }}</p>
      </button>
    </div>

    <!-- 按车间：分组紧凑格 -->
    <div v-else-if="view === 'workshop'" class="dsw dsw--groups">
      <section v-for="[name, list] in byWorkshop" :key="name" class="dsw-group">
        <h6 class="dsw-group-t">
          {{ name }} <small>{{ list.length }} 台</small>
        </h6>
        <div class="dsw-group-grid">
          <button
            v-for="d in list"
            :key="d.id"
            type="button"
            class="dsw-mini"
            :class="d.state"
            @click="emit('select', d)"
          >
            <span class="dsw-mini-top"><i class="dot" :class="d.state" />{{ d.name }}</span>
            <span class="dsw-mini-sub" :class="d.block ? d.state : ''">{{ chipText(d) }}</span>
          </button>
        </div>
      </section>
    </div>

    <!-- 按产线：行式，每线一行两机 -->
    <div v-else class="dsw dsw--lines">
      <div v-for="[name, list] in byLine" :key="name" class="dsw-line">
        <span class="dsw-line-name">{{ name }}</span>
        <button
          v-for="d in list"
          :key="d.id"
          type="button"
          class="dsw-chip"
          :class="d.state"
          @click="emit('select', d)"
        >
          <i class="dot" :class="d.state" />
          <b>{{ kindOf(d) }}</b>
          <span class="dsw-chip-txt" :class="d.block ? d.state : ''">{{ chipText(d) }}</span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.dsw-root {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

/* —— 五态计数 —— */
.dsw-counts {
  display: flex;
  align-items: center;
  gap: 18px;
  flex: none;
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
.count i,
.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--sb-faint);
  flex: none;
}
.count.run i,
.dot.run {
  background: var(--sb-green);
  box-shadow: 0 0 7px var(--sb-green);
}
.count.idle i,
.dot.idle {
  background: var(--sb-amber);
  box-shadow: 0 0 7px var(--sb-amber);
}
.count.alarm i,
.dot.alarm {
  background: var(--sb-red);
  box-shadow: 0 0 7px var(--sb-red);
}
.count.alarm b {
  color: var(--sb-red);
}
.count.down i,
.dot.down {
  background: var(--sb-muted);
}
.count.offline i,
.dot.offline {
  background: var(--sb-faint);
}

/* —— 通用：可点击格（button 语义，键盘可达） —— */
.dsw-cell,
.dsw-mini,
.dsw-chip {
  appearance: none;
  font: inherit;
  text-align: left;
  color: var(--sb-text);
  cursor: pointer;
  border-radius: var(--sb-radius);
  background: linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
  border: 1px solid var(--sb-line);
  border-top-color: rgba(255, 255, 255, 0.09);
  transition:
    border-color 0.18s var(--sb-ease),
    transform 0.12s var(--sb-ease);
}
.dsw-cell:hover,
.dsw-mini:hover,
.dsw-chip:hover {
  border-color: rgba(135, 208, 255, 0.3);
}
.dsw-cell:active,
.dsw-mini:active,
.dsw-chip:active {
  transform: scale(0.985);
}
.dsw-cell:focus-visible,
.dsw-mini:focus-visible,
.dsw-chip:focus-visible {
  outline: none;
  box-shadow:
    0 0 0 2px var(--sb-bg),
    0 0 0 4px var(--sb-cyan-dim);
}
/* 报警：边框红染 + 外发光缓闪 */
.dsw-cell.alarm,
.dsw-mini.alarm,
.dsw-chip.alarm {
  border-color: rgba(239, 90, 99, 0.4);
  position: relative;
}
.dsw-cell.alarm::after,
.dsw-mini.alarm::after,
.dsw-chip.alarm::after {
  content: '';
  position: absolute;
  inset: -1px;
  border-radius: inherit;
  pointer-events: none;
  box-shadow: 0 0 14px -4px rgba(239, 90, 99, 0.6);
  animation: dsw-alarm 1.8s ease-in-out infinite;
}
@keyframes dsw-alarm {
  50% {
    opacity: 0.25;
  }
}
/* 断线：灰斜纹 + 虚边 + 降饱和（防假绿） */
.dsw-cell.offline,
.dsw-mini.offline,
.dsw-chip.offline {
  opacity: 0.72;
  border-style: dashed;
  background-image:
    repeating-linear-gradient(-45deg, rgba(255, 255, 255, 0.028) 0 8px, transparent 8px 16px),
    linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
}

/* —— 平铺大格 —— */
.dsw--flat {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  grid-auto-rows: 1fr;
  gap: 12px;
}
.dsw-cell {
  display: flex;
  flex-direction: column;
  padding: 12px 14px 10px;
  min-width: 0;
}
.dsw-top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
}
.dsw-name {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--sb-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dsw-state {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex: none;
  font-size: 12.5px;
  color: var(--sb-text-2);
}
.dsw-state i {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--sb-faint);
}
.dsw-state.run i {
  background: var(--sb-green);
  box-shadow: 0 0 7px var(--sb-green);
}
.dsw-state.idle i {
  background: var(--sb-amber);
  box-shadow: 0 0 7px var(--sb-amber);
}
.dsw-state.alarm i {
  background: var(--sb-red);
  box-shadow: 0 0 7px var(--sb-red);
}
.dsw-state.alarm {
  color: var(--sb-red);
}
.dsw-state.down i {
  background: var(--sb-muted);
}
.dsw-state.offline i {
  background: var(--sb-faint);
}
.dsw-code {
  margin: 3px 0 6px;
  font-size: 12px;
  font-family: ui-monospace, monospace;
  color: var(--sb-muted);
}
.dsw-params {
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
}
.dsw-params div {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
}
.dsw-params dt {
  font-size: 12px;
  color: var(--sb-muted);
}
.dsw-params dd {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text-2);
}
.dsw-params dd.warn {
  color: var(--sb-amber);
}
.dsw-params dd.bad {
  color: var(--sb-red);
}
.dsw-block {
  margin: auto 0 0;
  padding-top: 6px;
  font-size: 12.5px;
  color: var(--sb-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dsw-block.alarm {
  color: var(--sb-red);
}
.dsw-block.down,
.dsw-block.idle {
  color: var(--sb-amber);
}

/* —— 按车间：分组紧凑格 —— */
.dsw--groups {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  gap: 8px;
  overflow: hidden;
}
.dsw-group-t {
  margin: 0 0 6px;
  font-size: 13.5px;
  font-weight: 600;
  color: var(--sb-text-2);
  letter-spacing: 0.04em;
}
.dsw-group-t small {
  margin-left: 6px;
  font-size: 12px;
  font-weight: 400;
  color: var(--sb-muted);
}
.dsw-group-grid {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 8px;
}
.dsw-mini {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 8px 10px;
  min-width: 0;
}
.dsw-mini-top {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: 13px;
  font-weight: 600;
  color: var(--sb-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dsw-mini-sub {
  font-size: 11.5px;
  color: var(--sb-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  font-variant-numeric: tabular-nums;
}
.dsw-mini-sub.alarm {
  color: var(--sb-red);
}
.dsw-mini-sub.down,
.dsw-mini-sub.idle {
  color: var(--sb-amber);
}

/* —— 按产线：行式 —— */
.dsw--lines {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  gap: 6px;
  overflow: hidden;
}
.dsw-line {
  display: grid;
  grid-template-columns: 110px 1fr 1fr;
  gap: 10px;
  align-items: center;
}
.dsw-line-name {
  font-size: 13.5px;
  font-weight: 600;
  color: var(--sb-text-2);
  white-space: nowrap;
}
.dsw-chip {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  min-width: 0;
}
.dsw-chip b {
  font-size: 13px;
  font-weight: 600;
  color: var(--sb-text);
  flex: none;
}
.dsw-chip-txt {
  flex: 1;
  min-width: 0;
  font-size: 12.5px;
  color: var(--sb-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  font-variant-numeric: tabular-nums;
}
.dsw-chip-txt.alarm {
  color: var(--sb-red);
}
.dsw-chip-txt.down,
.dsw-chip-txt.idle {
  color: var(--sb-amber);
}

@media (prefers-reduced-motion: reduce) {
  .dsw-cell,
  .dsw-mini,
  .dsw-chip {
    transition: none;
  }
  .dsw-cell.alarm::after,
  .dsw-mini.alarm::after,
  .dsw-chip.alarm::after {
    animation: none;
  }
}
</style>
