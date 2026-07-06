<script setup lang="ts">
import type { DeviceCell } from '@/data/contracts/equipment'

/**
 * 设备状态全景墙（spec §三）：每台设备一格 —— 设备名 + 资产编码 + 状态灯/词 +
 * 活动阻塞原因。五态视觉：运行绿 / 待机黄 / 停机灰 / 报警红（边框缓闪）/
 * 断线灰斜纹 + 降饱和（IsSourceFresh 防假绿）。reduced-motion 红闪静止。
 */
defineProps<{ devices: DeviceCell[] }>()
</script>

<template>
  <div class="dsw">
    <article v-for="d in devices" :key="d.id" class="dsw-cell" :class="d.state">
      <header class="dsw-top">
        <h5 class="dsw-name">{{ d.name }}</h5>
        <span class="dsw-state" :class="d.state"><i />{{ d.stateLabel }}</span>
      </header>
      <p class="dsw-code">{{ d.code }}</p>
      <p v-if="d.block" class="dsw-block" :class="d.state">{{ d.block }}</p>
      <p v-else class="dsw-block ok">状态正常</p>
    </article>
  </div>
</template>

<style scoped>
.dsw {
  height: 100%;
  min-height: 0;
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  grid-auto-rows: 1fr;
  gap: 12px;
}
.dsw-cell {
  display: flex;
  flex-direction: column;
  padding: 13px 14px 11px;
  border-radius: var(--sb-radius);
  background: linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
  border: 1px solid var(--sb-line);
  border-top-color: rgba(255, 255, 255, 0.09);
  position: relative;
  min-width: 0;
}
/* 报警：边框红染 + 外发光缓闪 */
.dsw-cell.alarm {
  border-color: rgba(239, 90, 99, 0.4);
}
.dsw-cell.alarm::after {
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
/* 停机：整格降饱和一点 */
.dsw-cell.down {
  border-color: rgba(255, 255, 255, 0.09);
}
/* 断线：灰斜纹条 + 降饱和（数据源不新鲜，防假绿） */
.dsw-cell.offline {
  opacity: 0.72;
  border-style: dashed;
  background-image:
    repeating-linear-gradient(
      -45deg,
      rgba(255, 255, 255, 0.028) 0 8px,
      transparent 8px 16px
    ),
    linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
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
  margin: 4px 0 0;
  font-size: 12px;
  font-family: ui-monospace, monospace;
  color: var(--sb-muted);
}
.dsw-block {
  margin: auto 0 0;
  padding-top: 8px;
  font-size: 12.5px;
  color: var(--sb-muted);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dsw-block.alarm {
  color: var(--sb-red);
}
.dsw-block.down {
  color: var(--sb-amber);
}
.dsw-block.idle {
  color: var(--sb-amber);
}
.dsw-block.ok {
  color: var(--sb-faint);
}

@media (prefers-reduced-motion: reduce) {
  .dsw-cell.alarm::after {
    animation: none;
  }
}
</style>
