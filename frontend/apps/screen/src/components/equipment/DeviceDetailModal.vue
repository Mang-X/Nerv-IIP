<script setup lang="ts">
import { Sparkline, StatusLight, StatusTag } from '@nerv-iip/ui'
import {
  Activity,
  BatteryCharging,
  Droplets,
  Gauge,
  Thermometer,
  Timer,
  Waves,
  Wind,
  Wrench,
  X,
  Zap,
} from 'lucide-vue-next'
import { type Component, computed, onBeforeUnmount, onMounted } from 'vue'
import type { DeviceDetail, ParamKind } from '@/data/contracts/equipment'
import { fetchDeviceDetail } from '@/data/fetchers/equipment'
import { useScreenData } from '@/screen-kit'

/**
 * 设备详情弹窗（点击全景墙设备格触发）：自取数并 3s 轮询（参数/趋势实时跳动）。
 * 基础信息 / 关键参数可视化（类型配色 + 图标 + 正常范围，断线虚线占位）/
 * 保养维修档案。Teleport body，ESC / 遮罩关闭；淡入无回弹。
 */
const props = defineProps<{
  deviceId: string
  factoryId: string
  workshopIds: string[] | 'all'
}>()

const emit = defineEmits<{ close: [] }>()

// 详情刷新频率：3s（比全景 5s 快，比格上参数 2s 稳）
const { data: detail } = useScreenData<DeviceDetail | null>(
  () => fetchDeviceDetail(props.deviceId, props.factoryId, props.workshopIds),
  { intervalMs: 3000 },
)

function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape') emit('close')
}
onMounted(() => window.addEventListener('keydown', onKey))
onBeforeUnmount(() => window.removeEventListener('keydown', onKey))

const tone = computed(() => {
  const s = detail.value?.device.state
  return s === 'alarm' ? 'alarm' : s === 'idle' || s === 'down' ? 'idle' : 'run'
})

const infoRows = computed(() => {
  const d = detail.value
  if (!d) return []
  return [
    { label: '资产编码', value: d.device.code },
    { label: '所属车间', value: d.device.workshopName },
    { label: '所属产线', value: d.device.lineName },
    { label: '工作中心', value: d.workCenterName },
    { label: '负责人', value: d.managerName },
    { label: '数据源', value: d.device.sourceFresh ? '在线 · 新鲜' : '断线', bad: !d.device.sourceFresh },
    { label: '当前状态', value: d.device.stateLabel },
    ...(d.device.block ? [{ label: '阻塞原因', value: d.device.block, bad: true }] : []),
  ]
})

// 参数类型 → 颜色 / 图标（图表按类型着色，2026-07 生产走查）
const KIND_COLOR: Record<ParamKind, string> = {
  temp: 'var(--sb-amber)',
  energy: 'var(--sb-amber)',
  pressure: 'var(--sb-cyan)',
  flow: 'var(--sb-cyan)',
  torque: 'var(--sb-cyan)',
  speed: 'var(--sb-green)',
  level: 'var(--sb-green)',
  cycle: 'var(--sb-green)',
  current: 'var(--sb-indigo)',
  vibration: 'var(--sb-indigo)',
}
const KIND_ICON: Record<ParamKind, Component> = {
  temp: Thermometer,
  pressure: Gauge,
  speed: Wind,
  current: Zap,
  vibration: Activity,
  flow: Droplets,
  level: Waves,
  cycle: Timer,
  energy: BatteryCharging,
  torque: Wrench,
}
function paramColor(kind: ParamKind, toneVal?: 'warn' | 'bad'): string {
  if (toneVal === 'bad') return 'var(--sb-red)'
  if (toneVal === 'warn') return 'var(--sb-amber)'
  return KIND_COLOR[kind]
}
</script>

<template>
  <Teleport to="body">
    <div class="ddm-mask" @click.self="emit('close')">
      <section class="ddm" role="dialog" aria-modal="true" :aria-label="detail?.device.name ?? '设备详情'">
        <header class="ddm-h">
          <StatusLight v-if="detail" :tone="tone" :label="detail.device.stateLabel" />
          <h3 class="ddm-t">{{ detail?.device.name ?? '设备详情' }}</h3>
          <span v-if="detail" class="ddm-code">{{ detail.device.code }}</span>
          <span v-if="detail" class="ddm-path">{{ detail.device.workshopName }} · {{ detail.device.lineName }}</span>
          <button type="button" class="ddm-x" aria-label="关闭" @click="emit('close')">
            <X :size="20" />
          </button>
        </header>

        <div v-if="!detail" class="ddm-loading">读取设备档案…</div>

        <div v-else class="ddm-body">
          <!-- 基础信息 -->
          <section class="ddm-col">
            <h4 class="ddm-st">基础信息</h4>
            <dl class="ddm-info">
              <div v-for="r in infoRows" :key="r.label">
                <dt>{{ r.label }}</dt>
                <dd :class="{ bad: r.bad }">{{ r.value }}</dd>
              </div>
            </dl>
          </section>

          <!-- 参数可视化：类型配色 + 图标 + 正常范围 + 趋势 -->
          <section class="ddm-col ddm-col--params">
            <h4 class="ddm-st">关键参数 · 近 12 点 · 3s 刷新</h4>
            <div class="ddm-params">
              <div v-for="p in detail.params" :key="p.label" class="ddm-param" :class="p.tone">
                <div class="ddm-param-h">
                  <component
                    :is="KIND_ICON[p.kind]"
                    :size="15"
                    class="ddm-param-ic"
                    :style="{ color: paramColor(p.kind, p.tone) }"
                  />
                  <span class="ddm-param-l">{{ p.label }}</span>
                  <span v-if="p.range !== '—'" class="ddm-param-r">正常 {{ p.range }}</span>
                  <b class="ddm-param-v" :style="{ color: paramColor(p.kind, p.tone) }">
                    {{ p.value === null ? '—' : p.value }}<small v-if="p.value !== null">{{ p.unit }}</small>
                  </b>
                </div>
                <div class="ddm-spark">
                  <Sparkline :data="p.spark" area :color="paramColor(p.kind, p.tone)" />
                </div>
              </div>
            </div>
            <p class="ddm-note">参数为演示数据流 · historian / 实时采集接入待 #570</p>
          </section>

          <!-- 保养维修 -->
          <section class="ddm-col">
            <h4 class="ddm-st">保养维修</h4>
            <div class="ddm-rel">
              <div>
                <dt>单机 MTBF</dt>
                <dd>{{ detail.mtbfHours === null ? '—' : `${detail.mtbfHours} h` }}</dd>
              </div>
              <div>
                <dt>单机 MTTR</dt>
                <dd>{{ detail.mttrMinutes === null ? '—' : `${detail.mttrMinutes} min` }}</dd>
              </div>
            </div>

            <div v-if="detail.repairs.length" class="ddm-repairs">
              <div v-for="r in detail.repairs" :key="r.wo" class="ddm-repair">
                <div class="ddm-repair-top">
                  <span class="ddm-wo">{{ r.wo }}</span>
                  <span class="ddm-issue">{{ r.issue }}</span>
                  <StatusTag v-if="r.overdue" tone="red" label="超时" />
                  <StatusTag v-else-if="r.awaitingConfirm" tone="cyan" label="待确认" />
                  <span v-else class="ddm-stage">{{ r.stage }}</span>
                </div>
                <div class="ddm-bar">
                  <i :class="{ overdue: r.overdue, done: r.progress >= 100 }" :style="{ width: `${r.progress}%` }" />
                </div>
              </div>
            </div>
            <p v-else class="ddm-empty">暂无进行中的维修单</p>

            <div class="ddm-list">
              <div v-for="t in detail.pmTasks" :key="t.task" class="ddm-row">
                <span class="ddm-row-txt">PM · {{ t.task }}</span>
                <span class="ddm-due" :class="t.state">{{ t.due }}</span>
              </div>
              <div v-for="i in detail.inspections" :key="i.time" class="ddm-row">
                <span class="ddm-row-txt">点检 · {{ i.item }} · {{ i.by }}</span>
                <span class="ddm-res" :class="{ bad: i.result === '异常' }">{{ i.result }}</span>
              </div>
              <p v-if="!detail.pmTasks.length && !detail.inspections.length" class="ddm-empty">
                今日暂无保养 / 点检记录
              </p>
            </div>
          </section>
        </div>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.ddm-mask {
  position: fixed;
  inset: 0;
  z-index: 60;
  background: rgba(2, 4, 10, 0.64);
  display: grid;
  place-items: center;
  animation: ddm-fade 0.2s var(--sb-ease);
}
.ddm {
  width: min(1120px, 94vw);
  max-height: min(700px, 92vh);
  display: flex;
  flex-direction: column;
  border-radius: 10px;
  background: linear-gradient(180deg, rgba(17, 26, 46, 0.97), rgba(8, 13, 25, 0.97));
  border: 1px solid rgba(148, 190, 255, 0.16);
  border-top-color: rgba(255, 255, 255, 0.14);
  box-shadow: 0 30px 80px -30px rgba(0, 0, 0, 0.9);
  padding: 20px 24px 18px;
  animation: ddm-in 0.22s var(--sb-ease);
}
@keyframes ddm-fade {
  from {
    opacity: 0;
  }
}
@keyframes ddm-in {
  from {
    opacity: 0;
    transform: scale(0.98);
  }
}

.ddm-h {
  display: flex;
  align-items: center;
  gap: 12px;
  padding-bottom: 14px;
  border-bottom: 1px solid var(--sb-divider);
}
.ddm-t {
  margin: 0;
  font-size: 21px;
  font-weight: 700;
  color: #fff;
  letter-spacing: 0.04em;
}
.ddm-code {
  font-family: ui-monospace, monospace;
  font-size: 13px;
  color: var(--sb-cyan);
}
.ddm-path {
  font-size: 13px;
  color: var(--sb-muted);
}
.ddm-x {
  appearance: none;
  margin-left: auto;
  width: 32px;
  height: 32px;
  display: grid;
  place-items: center;
  border-radius: 8px;
  border: 1px solid var(--sb-line-2);
  background: transparent;
  color: var(--sb-muted);
  cursor: pointer;
  transition:
    color 0.15s var(--sb-ease),
    border-color 0.15s var(--sb-ease);
}
.ddm-x:hover {
  color: var(--sb-text);
  border-color: rgba(135, 208, 255, 0.4);
}
.ddm-x:active {
  transform: scale(0.94);
}

.ddm-loading {
  padding: 80px 0;
  text-align: center;
  color: var(--sb-muted);
  font-size: 15px;
}

.ddm-body {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: 250px 1fr 300px;
  gap: 26px;
  padding-top: 16px;
  overflow: auto;
}
.ddm-st {
  margin: 0 0 12px;
  font-size: 14px;
  font-weight: 700;
  letter-spacing: 0.08em;
  color: var(--sb-text-2);
}

/* 基础信息 */
.ddm-info {
  margin: 0;
  display: flex;
  flex-direction: column;
}
.ddm-info div {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 0;
  border-bottom: 1px solid var(--sb-divider);
  font-size: 13.5px;
}
.ddm-info dt {
  color: var(--sb-muted);
  flex: none;
}
.ddm-info dd {
  margin: 0;
  color: var(--sb-text);
  text-align: right;
}
.ddm-info dd.bad {
  color: var(--sb-red);
}

/* 参数可视化 */
.ddm-params {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
.ddm-param {
  border: 1px solid var(--sb-line);
  border-top-color: rgba(255, 255, 255, 0.09);
  border-radius: var(--sb-radius);
  background: rgba(255, 255, 255, 0.02);
  padding: 12px 14px 8px;
}
.ddm-param.bad {
  border-color: rgba(239, 90, 99, 0.35);
}
.ddm-param-h {
  display: flex;
  align-items: baseline;
  gap: 7px;
  margin-bottom: 6px;
}
.ddm-param-ic {
  align-self: center;
  flex: none;
}
.ddm-param-l {
  font-size: 13px;
  color: var(--sb-muted);
  white-space: nowrap;
}
.ddm-param-r {
  font-size: 11px;
  color: var(--sb-faint);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.ddm-param-v {
  margin-left: auto;
  font-size: 22px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.ddm-param-v small {
  font-size: 12px;
  font-weight: 500;
  color: var(--sb-muted);
  margin-left: 3px;
}
.ddm-spark {
  height: 52px;
}
.ddm-note {
  margin: 12px 0 0;
  font-size: 12px;
  color: var(--sb-faint);
}

/* 保养维修 */
.ddm-rel {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  margin-bottom: 14px;
}
.ddm-rel div {
  border: 1px solid var(--sb-line);
  border-radius: var(--sb-radius);
  padding: 10px 12px;
  background: rgba(255, 255, 255, 0.02);
}
.ddm-rel dt {
  font-size: 12px;
  color: var(--sb-muted);
}
.ddm-rel dd {
  margin: 4px 0 0;
  font-size: 20px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text);
}
.ddm-repair {
  margin-bottom: 12px;
}
.ddm-repair-top {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.ddm-wo {
  font-family: ui-monospace, monospace;
  font-size: 12.5px;
  color: var(--sb-cyan);
  flex: none;
}
.ddm-issue {
  flex: 1;
  min-width: 0;
  font-size: 13px;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.ddm-stage {
  font-size: 12px;
  color: var(--sb-muted);
}
.ddm-bar {
  height: 5px;
  border-radius: 3px;
  background: rgba(255, 255, 255, 0.07);
  overflow: hidden;
  margin-top: 7px;
}
.ddm-bar i {
  display: block;
  height: 100%;
  border-radius: 3px;
  background: var(--sb-cyan);
}
.ddm-bar i.overdue {
  background: var(--sb-red);
}
.ddm-bar i.done {
  background: var(--sb-green);
}
.ddm-list {
  margin-top: 4px;
}
.ddm-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 7px 0;
  border-bottom: 1px solid var(--sb-divider);
  font-size: 13px;
}
.ddm-row-txt {
  flex: 1;
  min-width: 0;
  color: var(--sb-text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.ddm-due {
  color: var(--sb-amber);
  flex: none;
  font-variant-numeric: tabular-nums;
}
.ddm-due.overdue {
  color: var(--sb-red);
}
.ddm-due.done {
  color: var(--sb-green);
}
.ddm-res {
  color: var(--sb-green);
  flex: none;
}
.ddm-res.bad {
  color: var(--sb-red);
}
.ddm-empty {
  margin: 6px 0;
  font-size: 12.5px;
  color: var(--sb-faint);
}

@media (prefers-reduced-motion: reduce) {
  .ddm-mask,
  .ddm {
    animation: none;
  }
  .ddm-x {
    transition: none;
  }
}
</style>
