<script setup lang="ts">
import { ref } from 'vue'
import type { DeviceCell, StateCounts } from '@/data/contracts/equipment'

/**
 * 设备状态全景墙（spec §三 + 生产走查交互深化）：
 * 三种排列/展现形式 —— 平铺（大格带关键参数，纵向滚动承载大规模）/
 * 按车间（分组紧凑格，滚动）/ 按产线（每线一组 chip 流式换行，产线设备数不定）。
 * 五态视觉：运行绿/待机黄/停机灰/报警红缓闪/断线灰斜纹（IsSourceFresh 防假绿）。
 * 每台可点（emit select）；悬浮显示设备摘要 tooltip（单例浮层，Teleport body）。
 */
const props = defineProps<{
  devices: DeviceCell[]
  counts: StateCounts
  view: 'flat' | 'workshop' | 'line'
}>()

const emit = defineEmits<{ select: [device: DeviceCell] }>()

function countItems() {
  return [
    { k: 'run', label: '运行', v: props.counts.run },
    { k: 'idle', label: '待机', v: props.counts.idle },
    { k: 'down', label: '停机', v: props.counts.down },
    { k: 'alarm', label: '报警', v: props.counts.alarm },
    { k: 'offline', label: '断线', v: props.counts.offline },
  ]
}

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

// —— 单例 tooltip（Teleport body，滚动/点击即隐）——
const tip = ref<{ device: DeviceCell; x: number; y: number; below: boolean } | null>(null)
function showTip(d: DeviceCell, e: MouseEvent) {
  const r = (e.currentTarget as HTMLElement).getBoundingClientRect()
  const below = r.top < 190
  tip.value = {
    device: d,
    x: Math.min(Math.max(r.left + r.width / 2, 140), window.innerWidth - 140),
    y: below ? r.bottom + 10 : r.top - 10,
    below,
  }
}
function hideTip() {
  tip.value = null
}
function pick(d: DeviceCell) {
  hideTip()
  emit('select', d)
}
</script>

<template>
  <div class="dsw-root">
    <!-- 顶部五态计数（与墙体逐台归并一致，单测对账） -->
    <div class="dsw-counts">
      <span v-for="c in countItems()" :key="c.k" class="count" :class="c.k">
        <i />{{ c.label }} <b>{{ c.v }}</b>
      </span>
      <span class="dsw-total">共 {{ devices.length }} 台</span>
    </div>

    <!-- 平铺：大格带关键参数，纵向滚动 -->
    <div v-if="view === 'flat'" class="dsw dsw--flat sb-scroll" @scroll="hideTip">
      <button
        v-for="d in devices"
        :key="d.id"
        type="button"
        class="dsw-cell"
        :class="d.state"
        @click="pick(d)"
        @mouseenter="showTip(d, $event)"
        @mouseleave="hideTip"
      >
        <header class="dsw-top">
          <h5 class="dsw-name">{{ d.name }}</h5>
          <span class="dsw-state" :class="d.state"><i />{{ d.stateLabel }}</span>
        </header>
        <p class="dsw-code">{{ d.code }} · {{ d.lineName }}</p>
        <dl class="dsw-params">
          <div v-for="p in d.params" :key="p.label">
            <dt>{{ p.label }}</dt>
            <dd :class="p.tone">{{ p.value }}</dd>
          </div>
        </dl>
        <p v-if="d.block" class="dsw-block" :class="d.state">{{ d.block }}</p>
      </button>
    </div>

    <!-- 按车间：分组紧凑格，滚动 -->
    <div v-else-if="view === 'workshop'" class="dsw dsw--groups sb-scroll" @scroll="hideTip">
      <section v-for="[name, list] in groupBy((d) => d.workshopName)" :key="name" class="dsw-group">
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
            @click="pick(d)"
            @mouseenter="showTip(d, $event)"
            @mouseleave="hideTip"
          >
            <span class="dsw-mini-top"><i class="dot" :class="d.state" />{{ d.name }}</span>
            <span class="dsw-mini-sub" :class="d.block ? d.state : ''">
              {{ d.block ?? `${d.params[0]?.label ?? ''} ${d.params[0]?.value ?? ''}` }}
            </span>
          </button>
        </div>
      </section>
    </div>

    <!-- 按产线：每线一组，chip 流式换行（产线设备数不定） -->
    <div v-else class="dsw dsw--lines sb-scroll" @scroll="hideTip">
      <section v-for="[name, list] in groupBy((d) => d.lineName)" :key="name" class="dsw-linegroup">
        <h6 class="dsw-group-t">
          {{ name }} <small>{{ list.length }} 台</small>
        </h6>
        <div class="dsw-chips">
          <button
            v-for="d in list"
            :key="d.id"
            type="button"
            class="dsw-chip"
            :class="d.state"
            @click="pick(d)"
            @mouseenter="showTip(d, $event)"
            @mouseleave="hideTip"
          >
            <i class="dot" :class="d.state" />
            <b>{{ d.name }}</b>
            <span class="dsw-chip-txt" :class="d.block ? d.state : ''">
              {{ d.block ?? `${d.params[0]?.label ?? ''} ${d.params[0]?.value ?? ''}` }}
            </span>
          </button>
        </div>
      </section>
    </div>

    <!-- 悬浮设备摘要 tooltip -->
    <Teleport to="body">
      <div
        v-if="tip"
        class="dsw-tip"
        :class="{ below: tip.below }"
        :style="{ left: `${tip.x}px`, top: `${tip.y}px` }"
      >
        <div class="dsw-tip-h">
          <i class="dot" :class="tip.device.state" />
          <b>{{ tip.device.name }}</b>
          <span :class="['dsw-tip-state', tip.device.state]">{{ tip.device.stateLabel }}</span>
        </div>
        <div class="dsw-tip-sub">{{ tip.device.code }} · {{ tip.device.workshopName }} · {{ tip.device.lineName }}</div>
        <div v-for="p in tip.device.params" :key="p.label" class="dsw-tip-row">
          <span>{{ p.label }}</span>
          <b :class="p.tone">{{ p.value }}</b>
        </div>
        <div v-if="tip.device.block" class="dsw-tip-block" :class="tip.device.state">{{ tip.device.block }}</div>
        <div class="dsw-tip-hint">点击查看设备详情</div>
      </div>
    </Teleport>
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
.dsw-total {
  margin-left: auto;
  font-size: 13px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
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
.dsw-cell.offline,
.dsw-mini.offline,
.dsw-chip.offline {
  opacity: 0.72;
  border-style: dashed;
  background-image:
    repeating-linear-gradient(-45deg, rgba(255, 255, 255, 0.028) 0 8px, transparent 8px 16px),
    linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
}

/* —— 平铺大格（滚动墙） —— */
.dsw--flat {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  grid-auto-rows: 122px;
  gap: 12px;
  padding-right: 4px;
}
.dsw-cell {
  display: flex;
  flex-direction: column;
  padding: 11px 13px 9px;
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
  font-size: 14.5px;
  font-weight: 600;
  color: var(--sb-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dsw-state {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  flex: none;
  font-size: 12px;
  color: var(--sb-text-2);
}
.dsw-state i {
  width: 7px;
  height: 7px;
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
  margin: 2px 0 5px;
  font-size: 11.5px;
  font-family: ui-monospace, monospace;
  color: var(--sb-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dsw-params {
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.dsw-params div {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
}
.dsw-params dt {
  font-size: 11.5px;
  color: var(--sb-muted);
}
.dsw-params dd {
  margin: 0;
  font-size: 12.5px;
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
  padding-top: 4px;
  font-size: 11.5px;
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

/* —— 按车间：分组紧凑格（滚动） —— */
.dsw--groups {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-right: 4px;
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
  grid-template-columns: repeat(auto-fill, minmax(168px, 1fr));
  gap: 8px;
}
.dsw-mini {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 8px 10px;
  min-width: 0;
}
.dsw-mini-top {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: 12.5px;
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

/* —— 按产线：每线一组 chip 流式 —— */
.dsw--lines {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 11px;
  padding-right: 4px;
}
.dsw-chips {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(215px, 1fr));
  gap: 8px;
}
.dsw-chip {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 11px;
  min-width: 0;
}
.dsw-chip b {
  font-size: 12.5px;
  font-weight: 600;
  color: var(--sb-text);
  flex: none;
  white-space: nowrap;
}
.dsw-chip-txt {
  flex: 1;
  min-width: 0;
  font-size: 11.5px;
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

/* —— tooltip（Teleport body，视口坐标） —— */
.dsw-tip {
  position: fixed;
  z-index: 70;
  transform: translate(-50%, -100%);
  min-width: 230px;
  max-width: 300px;
  padding: 11px 13px;
  border-radius: 8px;
  background: rgba(10, 16, 30, 0.97);
  border: 1px solid rgba(148, 190, 255, 0.2);
  border-top-color: rgba(255, 255, 255, 0.14);
  box-shadow: 0 14px 40px -16px rgba(0, 0, 0, 0.9);
  pointer-events: none;
  animation: dsw-tip-in 0.15s var(--sb-ease);
}
.dsw-tip.below {
  transform: translate(-50%, 0);
}
@keyframes dsw-tip-in {
  from {
    opacity: 0;
  }
}
.dsw-tip-h {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13.5px;
  color: var(--sb-text);
}
.dsw-tip-h b {
  font-weight: 600;
}
.dsw-tip-state {
  margin-left: auto;
  font-size: 12px;
  color: var(--sb-text-2);
}
.dsw-tip-state.alarm {
  color: var(--sb-red);
}
.dsw-tip-state.down,
.dsw-tip-state.idle {
  color: var(--sb-amber);
}
.dsw-tip-state.offline {
  color: var(--sb-faint);
}
.dsw-tip-sub {
  margin: 4px 0 7px;
  font-size: 11.5px;
  font-family: ui-monospace, monospace;
  color: var(--sb-muted);
}
.dsw-tip-row {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  padding: 3px 0;
  font-size: 12.5px;
  color: var(--sb-muted);
}
.dsw-tip-row b {
  font-weight: 600;
  color: var(--sb-text-2);
  font-variant-numeric: tabular-nums;
}
.dsw-tip-row b.warn {
  color: var(--sb-amber);
}
.dsw-tip-row b.bad {
  color: var(--sb-red);
}
.dsw-tip-block {
  margin-top: 6px;
  font-size: 12px;
  color: var(--sb-amber);
}
.dsw-tip-block.alarm {
  color: var(--sb-red);
}
.dsw-tip-hint {
  margin-top: 8px;
  padding-top: 7px;
  border-top: 1px solid var(--sb-divider);
  font-size: 11.5px;
  color: var(--sb-faint);
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
  .dsw-tip {
    animation: none;
  }
}
</style>
