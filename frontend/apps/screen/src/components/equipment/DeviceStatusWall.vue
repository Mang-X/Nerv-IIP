<script setup lang="ts">
import { Sparkline } from '@nerv-iip/ui'
import { useVirtualList } from '@vueuse/core'
import { computed, ref, watch } from 'vue'
import type { DeviceCell, DeviceDetail, StateCounts } from '@/data/contracts/equipment'
import { fetchDeviceDetail } from '@/data/fetchers/equipment'
import { paramColor } from './paramColors'

/**
 * 设备状态全景墙（spec §三 + 生产走查性能/交互深化）：
 * 平铺 = 行虚拟滚动（useVirtualList，规模化就绪）；按车间/按产线 = 分组滚动。
 * 视野内设备集经 emit('visible') 上报 —— 页面据此只对可见设备做参数快刷。
 * 悬浮 tooltip = 浓缩详情卡（防抖 250ms 取单设备档案 + 30s 缓存）：全参数
 * 迷你趋势 + 维修/保养摘要 + 单机 MTBF/MTTR，减少必须点开弹窗的场景。
 */
const props = defineProps<{
  devices: DeviceCell[]
  counts: StateCounts
  view: 'flat' | 'workshop' | 'line'
  factoryId: string
  workshopIds: string[] | 'all'
}>()

const emit = defineEmits<{
  select: [device: DeviceCell]
  visible: [ids: string[]]
}>()

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

// —— 平铺：行虚拟滚动（每行 6 台，行高 122 + 12 间距）——
const COLS = 6
const rowsSrc = computed(() => {
  const out: DeviceCell[][] = []
  for (let i = 0; i < props.devices.length; i += COLS) out.push(props.devices.slice(i, i + COLS))
  return out
})
const { list: vRows, containerProps, wrapperProps } = useVirtualList(rowsSrc, {
  itemHeight: 134,
  overscan: 1,
})

// 视野内设备集：平铺 = 虚拟列表渲染行；分组视图格子总量小，视为全可见
const visibleIds = computed(() =>
  props.view === 'flat'
    ? vRows.value.flatMap((r) => r.data.map((d) => d.id))
    : props.devices.map((d) => d.id),
)
watch(visibleIds, (ids) => emit('visible', ids), { immediate: true })

function onFlatScroll(e: Event) {
  hideTip()
  ;(containerProps.onScroll as (e: Event) => void)(e)
}

// —— 浓缩详情 tooltip（单例浮层；防抖取档案 + 30s 缓存）——
const tip = ref<{ device: DeviceCell; x: number; y: number; below: boolean } | null>(null)
const tipDetail = ref<DeviceDetail | null>(null)
const detailCache = new Map<string, { at: number; d: DeviceDetail }>()
let hoverTimer: ReturnType<typeof setTimeout> | undefined
let hoverToken = 0

function showTip(d: DeviceCell, e: MouseEvent) {
  const r = (e.currentTarget as HTMLElement).getBoundingClientRect()
  const below = r.top < 380
  tip.value = {
    device: d,
    x: Math.min(Math.max(r.left + r.width / 2, 200), window.innerWidth - 200),
    y: below ? r.bottom + 10 : r.top - 10,
    below,
  }
  const cached = detailCache.get(d.id)
  if (cached && Date.now() - cached.at < 30_000) {
    tipDetail.value = cached.d
    return
  }
  tipDetail.value = null
  const token = ++hoverToken
  clearTimeout(hoverTimer)
  hoverTimer = setTimeout(async () => {
    const det = await fetchDeviceDetail(d.id, props.factoryId, props.workshopIds)
    if (det) {
      detailCache.set(d.id, { at: Date.now(), d: det })
      if (token === hoverToken && tip.value?.device.id === d.id) tipDetail.value = det
    }
  }, 250)
}
function hideTip() {
  tip.value = null
  tipDetail.value = null
  clearTimeout(hoverTimer)
  hoverToken++
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

    <!-- 平铺：行虚拟滚动的大格参数墙 -->
    <div
      v-if="view === 'flat'"
      v-bind="containerProps"
      class="dsw dsw--flat sb-scroll"
      @scroll="onFlatScroll"
    >
      <div v-bind="wrapperProps">
        <div v-for="row in vRows" :key="row.index" class="dsw-vrow">
          <button
            v-for="d in row.data"
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
      </div>
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

    <!-- 按产线：每线一组，chip 流式换行 -->
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

    <!-- 浓缩详情 tooltip：全参数迷你趋势 + 维修/保养摘要 -->
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
          <span class="dsw-tip-code">{{ tip.device.code }}</span>
          <span :class="['dsw-tip-state', tip.device.state]">{{ tip.device.stateLabel }}</span>
        </div>
        <div class="dsw-tip-sub">
          {{ tip.device.workshopName }} · {{ tip.device.lineName
          }}<template v-if="tipDetail"> · {{ tipDetail.workCenterName }} · 负责人 {{ tipDetail.managerName }}</template>
        </div>
        <div v-if="tip.device.block" class="dsw-tip-block" :class="tip.device.state">{{ tip.device.block }}</div>

        <!-- 满血：4 参数 + 迷你趋势；档案未到时先显格上 2 参数 -->
        <template v-if="tipDetail">
          <div v-for="p in tipDetail.params" :key="p.label" class="dsw-tip-prow">
            <span class="l">{{ p.label }}</span>
            <span class="spark"><Sparkline :data="p.spark" :color="paramColor(p.kind, p.tone)" /></span>
            <b :style="{ color: paramColor(p.kind, p.tone) }">{{ p.value === null ? '—' : `${p.value}${p.unit}` }}</b>
          </div>
          <div class="dsw-tip-meta">
            <span>MTBF {{ tipDetail.mtbfHours === null ? '—' : `${tipDetail.mtbfHours}h` }}</span>
            <span>MTTR {{ tipDetail.mttrMinutes === null ? '—' : `${tipDetail.mttrMinutes}min` }}</span>
          </div>
          <div v-if="tipDetail.repairs[0]" class="dsw-tip-repair">
            <span class="wo">{{ tipDetail.repairs[0].wo }}</span>
            <span class="txt">{{ tipDetail.repairs[0].issue }}</span>
            <b>{{ tipDetail.repairs[0].progress }}%</b>
          </div>
          <div v-if="tipDetail.pmTasks[0]" class="dsw-tip-pm">
            PM · {{ tipDetail.pmTasks[0].task }}
            <span :class="tipDetail.pmTasks[0].state">{{ tipDetail.pmTasks[0].due }}</span>
          </div>
        </template>
        <template v-else>
          <div v-for="p in tip.device.params" :key="p.label" class="dsw-tip-row">
            <span>{{ p.label }}</span>
            <b :class="p.tone">{{ p.value }}</b>
          </div>
        </template>

        <div class="dsw-tip-hint">点击查看完整档案与实时趋势</div>
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

/* —— 通用可点击格 —— */
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

/* —— 平铺（虚拟滚动） —— */
.dsw--flat {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding-right: 4px;
}
.dsw-vrow {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 12px;
  height: 122px;
  margin-bottom: 12px;
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

/* —— 按车间 —— */
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

/* —— 按产线 —— */
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

/* —— 浓缩详情 tooltip —— */
.dsw-tip {
  position: fixed;
  z-index: 70;
  transform: translate(-50%, -100%);
  width: 372px;
  padding: 13px 15px 11px;
  border-radius: 9px;
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
  font-size: 14px;
  color: var(--sb-text);
}
.dsw-tip-h b {
  font-weight: 600;
}
.dsw-tip-code {
  font-size: 11.5px;
  font-family: ui-monospace, monospace;
  color: var(--sb-cyan);
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
  margin: 4px 0 6px;
  font-size: 12px;
  color: var(--sb-muted);
}
.dsw-tip-block {
  margin: 0 0 6px;
  font-size: 12px;
  color: var(--sb-amber);
}
.dsw-tip-block.alarm {
  color: var(--sb-red);
}
/* 满血参数行：label + 迷你趋势 + 类型色数值 */
.dsw-tip-prow {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 4px 0;
}
.dsw-tip-prow .l {
  width: 64px;
  flex: none;
  font-size: 12px;
  color: var(--sb-muted);
}
.dsw-tip-prow .spark {
  flex: 1;
  height: 20px;
  min-width: 0;
}
.dsw-tip-prow b {
  flex: none;
  min-width: 74px;
  text-align: right;
  font-size: 13px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
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
.dsw-tip-meta {
  display: flex;
  gap: 16px;
  margin-top: 7px;
  padding-top: 7px;
  border-top: 1px solid var(--sb-divider);
  font-size: 12px;
  color: var(--sb-text-2);
  font-variant-numeric: tabular-nums;
}
.dsw-tip-repair {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 6px;
  font-size: 12px;
}
.dsw-tip-repair .wo {
  font-family: ui-monospace, monospace;
  color: var(--sb-cyan);
  flex: none;
}
.dsw-tip-repair .txt {
  flex: 1;
  min-width: 0;
  color: var(--sb-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dsw-tip-repair b {
  color: var(--sb-text-2);
  font-variant-numeric: tabular-nums;
}
.dsw-tip-pm {
  margin-top: 4px;
  font-size: 12px;
  color: var(--sb-muted);
  display: flex;
  justify-content: space-between;
  gap: 10px;
}
.dsw-tip-pm .due {
  color: var(--sb-amber);
}
.dsw-tip-pm .overdue {
  color: var(--sb-red);
}
.dsw-tip-pm .done {
  color: var(--sb-green);
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
