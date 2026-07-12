<script setup lang="ts">
import type { LineState } from '@/data/contracts/line'

/**
 * 超大产线状态灯（spec §四 一眼焦点①）：现场远距（数米外）一眼判断
 * 正常（绿·稳）/ 需关注（黄·缓呼吸）/ 报警（红·急呼吸）；
 * 数据源断线设备 >0 时右上失联角标（防假绿）。reduced-motion 全静止。
 */
defineProps<{
  state: LineState
  stateLabel: string
  offlineDevices: number
}>()
</script>

<template>
  <div class="lah" :class="state">
    <div class="lah-lamp">
      <span class="lah-core" />
    </div>
    <div class="lah-label">{{ stateLabel }}</div>
    <div v-if="offlineDevices > 0" class="lah-off">{{ offlineDevices }} 台设备数据失联</div>
  </div>
</template>

<style scoped>
@layer app {
  .lah {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 18px;
    position: relative;
  }
  .lah-lamp {
    width: 200px;
    height: 200px;
    border-radius: 50%;
    display: grid;
    place-items: center;
    border: 1px solid var(--nv-scr-line-2);
    background: rgba(255, 255, 255, 0.02);
  }
  .lah-core {
    width: 158px;
    height: 158px;
    border-radius: 50%;
  }
  .lah.run .lah-core {
    background: radial-gradient(circle at 38% 32%, #7dedb2, var(--nv-scr-green) 55%, #0b7a44);
    box-shadow: 0 0 44px rgba(69, 208, 137, 0.5);
    animation: lah-breathe 4s ease-in-out infinite;
  }
  .lah.attention .lah-core {
    background: radial-gradient(circle at 38% 32%, #ffe29a, var(--nv-scr-amber) 55%, #8f6c14);
    box-shadow: 0 0 44px rgba(242, 193, 78, 0.55);
    animation: lah-breathe 2.4s ease-in-out infinite;
  }
  .lah.alarm .lah-core {
    background: radial-gradient(circle at 38% 32%, #ff9aa1, var(--nv-scr-red) 55%, #8f1b22);
    box-shadow: 0 0 54px rgba(239, 90, 99, 0.65);
    animation: lah-breathe 1.2s ease-in-out infinite;
  }
  @keyframes lah-breathe {
    50% {
      opacity: 0.55;
    }
  }
  .lah-label {
    font-size: 34px;
    font-weight: 800;
    letter-spacing: 0.14em;
    color: #fff;
  }
  .lah.alarm .lah-label {
    color: var(--nv-scr-red);
    text-shadow: 0 0 22px rgba(239, 90, 99, 0.55);
  }
  .lah.attention .lah-label {
    color: var(--nv-scr-amber);
  }
  .lah.run .lah-label {
    color: var(--nv-scr-green);
  }
  /* 失联角标：灰斜纹条（防假绿的显著提示） */
  .lah-off {
    padding: 5px 12px;
    border-radius: 6px;
    border: 1px dashed rgba(255, 255, 255, 0.28);
    background: repeating-linear-gradient(
      -45deg,
      rgba(255, 255, 255, 0.05) 0 8px,
      transparent 8px 16px
    );
    font-size: 13.5px;
    color: var(--nv-scr-muted);
  }
  @media (prefers-reduced-motion: reduce) {
    .lah-core,
    .lah.run .lah-core,
    .lah.attention .lah-core,
    .lah.alarm .lah-core {
      animation: none;
    }
  }
}
</style>
