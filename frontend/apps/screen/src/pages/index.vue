<script setup lang="ts">
import { OeeHero, RingGauge, ScreenPanel, StatusCard } from '@nerv-iip/ui'
import { computed } from 'vue'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { createFactoryOverview, type FactoryOverview, fetchFactoryOverviewMock } from '@/mock/factory'
import { ScrollBoard, useScreenData } from '@/screen-kit'

const { data, isStale } = useScreenData<FactoryOverview>(fetchFactoryOverviewMock, {
  intervalMs: 4000,
  initialData: createFactoryOverview(),
})

const ov = computed(() => data.value ?? createFactoryOverview())
const kpiAccents = ['cyan', 'green', 'amber', 'indigo'] as const
</script>

<template>
  <ScreenLayout title="Nerv-IIP 工厂运营大屏" screen="指挥中心大屏 01">
    <div class="grid">
      <section class="kpis">
        <ScreenPanel
          v-for="(k, i) in ov.kpis"
          :key="k.label"
          :accent="kpiAccents[i % kpiAccents.length]"
        >
          <OeeHero :label="k.label" :value="k.value" :unit="k.unit" :delta="k.delta" :spark="k.spark" />
        </ScreenPanel>
      </section>

      <ScreenPanel class="matrix" title="车间运行状态">
        <div class="matrix-grid">
          <StatusCard v-for="w in ov.workshops" :key="w.name" v-bind="w" />
        </div>
      </ScreenPanel>

      <div class="side">
        <ScreenPanel title="综合效率 OEE" accent="cyan">
          <div class="rings">
            <RingGauge v-for="o in ov.oee" :key="o.label" :value="o.value" :label="o.label" />
          </div>
        </ScreenPanel>
        <ScreenPanel title="实时告警" accent="red" class="alarms">
          <template #extra>
            <span :class="['feed', { stale: isStale }]">{{ isStale ? '数据滞留' : '实时' }}</span>
          </template>
          <div class="alarm-scroll">
            <ScrollBoard :items="ov.alarms" :row-key="(a) => a.id" :speed="22">
              <template #row="{ item }">
                <div class="alarm-row" :class="item.level">
                  <span class="dot" />
                  <span class="txt">{{ item.text }}</span>
                  <span class="time">{{ item.time }}</span>
                </div>
              </template>
            </ScrollBoard>
          </div>
        </ScreenPanel>
      </div>
    </div>
  </ScreenLayout>
</template>

<style scoped>
.grid {
  height: 100%;
  display: grid;
  grid-template-columns: 1.55fr 0.95fr;
  grid-template-rows: auto 1fr;
  gap: 16px;
}
.kpis {
  grid-column: 1 / -1;
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
}
.matrix {
  grid-column: 1;
  grid-row: 2;
  height: 100%;
  display: flex;
  flex-direction: column;
}
.matrix-grid {
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  grid-template-rows: repeat(2, 1fr);
  gap: 14px;
}
.side {
  grid-column: 2;
  grid-row: 2;
  display: grid;
  grid-template-rows: auto 1fr;
  gap: 16px;
  min-height: 0;
}
.rings {
  display: flex;
  justify-content: space-around;
  align-items: center;
  padding: 10px 0 4px;
}
.alarms {
  height: 100%;
  display: flex;
  flex-direction: column;
}
.alarm-scroll {
  flex: 1;
  min-height: 0;
}
.feed {
  font-size: 12px;
  color: var(--sb-green);
}
.feed.stale {
  color: var(--sb-amber);
}
.alarm-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 2px;
  border-bottom: 1px solid var(--sb-divider);
  font-size: 15px;
}
.alarm-row .dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex: none;
}
.alarm-row.critical .dot {
  background: var(--sb-red);
  box-shadow: 0 0 8px var(--sb-red);
}
.alarm-row.warning .dot {
  background: var(--sb-amber);
  box-shadow: 0 0 8px var(--sb-amber);
}
.alarm-row .txt {
  flex: 1;
  color: var(--sb-text-2);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.alarm-row.critical .txt {
  color: #fff;
}
.alarm-row .time {
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
</style>
