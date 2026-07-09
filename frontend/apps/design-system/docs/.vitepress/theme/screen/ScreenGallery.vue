<script setup lang="ts">
import {
  NvAlarmTable,
  NvBorderPanel,
  NvCapsuleBar,
  NvDigitalFlop,
  NvGlowDivider,
  NvKpiBar,
  NvOeeHero,
  NvRingGauge,
  NvScreenButton,
  NvScreenHeader,
  NvScreenInput,
  NvScreenPagination,
  NvScreenPanel,
  NvScreenSearch,
  NvScreenSegmented,
  NvScreenSelect,
  NvScreenSwitch,
  NvScreenTable,
  NvScreenTabs,
  NvSparkline,
  NvScreenStatusCard,
  NvScreenStatusLight,
  NvScreenStatusTag,
  NvTaktGantt,
  NvTechFrame,
  NvTitleBar,
  NvScreenTrendChart,
} from '@nerv-iip/ui'
import { ref } from 'vue'

const input = ref('WO-2406-0312')
const search = ref('')
const sel = ref('line-a')
const seg = ref('today')
const tabs = ref('output')
const sw = ref(true)
const page = ref(2)

const lines = [
  {
    name: '焊接线 A',
    state: '运行',
    label: '运行中',
    tone: 'run',
    plan: '1,240',
    actual: '1,156',
    rate: '93.2%',
    downtime: '28 分钟',
  },
  {
    name: '装配线 B',
    state: '待机',
    label: '待机中',
    tone: 'idle',
    plan: '980',
    actual: '742',
    rate: '75.7%',
    downtime: '42 分钟',
  },
  {
    name: 'CNC 线 C',
    state: '报警',
    label: '报警中',
    tone: 'alarm',
    plan: '760',
    actual: '312',
    rate: '41.1%',
    downtime: '114 分钟',
  },
] as const
const tones = { run: '运行中', idle: '待机', alarm: '报警' } as const
</script>

<template>
  <div class="g-stage">
    <NvScreenHeader
      title="智能工厂 · 大屏组件总览"
      time="2024-06-12 10:24:36"
      date="星期三"
      line="全部产线"
      screen="组件库 Gallery"
    />

    <h3 class="g-cat">容器 · 外壳</h3>
    <div class="g-grid">
      <div class="g-cell">
        <span class="g-t">NvScreenPanel</span
        ><NvScreenPanel title="设备综合效率"
          ><div class="g-fill">面板内容 · hairline + 白高光</div></NvScreenPanel
        >
      </div>
      <div class="g-cell">
        <span class="g-t">NvBorderPanel</span
        ><NvBorderPanel title="班次概览"
          ><div class="g-fill">描边面板 · 顶部 notch</div></NvBorderPanel
        >
      </div>
      <div class="g-cell">
        <span class="g-t">NvTechFrame</span
        ><NvTechFrame><div class="g-fill g-pad">科技边框 · 角光渐隐</div></NvTechFrame>
      </div>
      <div class="g-cell">
        <span class="g-t">NvTitleBar</span>
        <div class="g-pad2"><NvTitleBar title="产线综合监控" sub="REAL-TIME OVERVIEW" /></div>
      </div>
      <div class="g-cell g-span2">
        <span class="g-t">NvGlowDivider</span>
        <div class="g-pad2"><NvGlowDivider /></div>
      </div>
    </div>

    <h3 class="g-cat">NvScreenPanel 颜色变体</h3>
    <div class="g-grid">
      <div class="g-cell">
        <span class="g-t">默认</span
        ><NvScreenPanel title="默认面板"><div class="g-fill">蓝色边框</div></NvScreenPanel>
      </div>
      <div class="g-cell">
        <span class="g-t">accent=cyan</span
        ><NvScreenPanel accent="cyan" title="数据面板"
          ><div class="g-fill">青色变体</div></NvScreenPanel
        >
      </div>
      <div class="g-cell">
        <span class="g-t">accent=green</span
        ><NvScreenPanel accent="green" title="正常状态"
          ><div class="g-fill">绿色变体</div></NvScreenPanel
        >
      </div>
      <div class="g-cell">
        <span class="g-t">accent=amber</span
        ><NvScreenPanel accent="amber" title="预警状态"
          ><div class="g-fill">琥珀变体</div></NvScreenPanel
        >
      </div>
      <div class="g-cell">
        <span class="g-t">accent=red</span
        ><NvScreenPanel accent="red" title="报警状态"
          ><div class="g-fill">红色变体</div></NvScreenPanel
        >
      </div>
      <div class="g-cell">
        <span class="g-t">accent=indigo</span
        ><NvScreenPanel accent="indigo" title="次要信息"
          ><div class="g-fill">靛蓝变体</div></NvScreenPanel
        >
      </div>
    </div>

    <h3 class="g-cat">指标 · 图表</h3>
    <div class="g-grid">
      <div class="g-cell">
        <span class="g-t">NvOeeHero</span
        ><NvScreenPanel
          ><NvOeeHero label="设备综合效率 OEE" :value="92.4" unit="%" delta="较昨日 +2.7%"
        /></NvScreenPanel>
      </div>
      <div class="g-cell">
        <span class="g-t">NvRingGauge</span
        ><NvScreenPanel
          ><div class="g-center"><NvRingGauge :value="78" label="稼动率" /></div
        ></NvScreenPanel>
      </div>
      <div class="g-cell">
        <span class="g-t">NvDigitalFlop</span
        ><NvScreenPanel
          ><div class="g-center"><NvDigitalFlop :value="1156" suffix="件" /></div
        ></NvScreenPanel>
      </div>
      <div class="g-cell">
        <span class="g-t">NvCapsuleBar</span><NvScreenPanel><NvCapsuleBar /></NvScreenPanel>
      </div>
      <div class="g-cell">
        <span class="g-t">NvSparkline</span
        ><NvScreenPanel
          ><div style="height: 56px"><NvSparkline area /></div
        ></NvScreenPanel>
      </div>
      <div class="g-cell g-span2">
        <span class="g-t">NvScreenTrendChart</span><NvScreenTrendChart />
      </div>
      <div class="g-cell g-span2"><span class="g-t">NvTaktGantt</span><NvTaktGantt /></div>
    </div>

    <h3 class="g-cat">数据 · 状态</h3>
    <div class="g-grid">
      <div class="g-cell" v-for="l in lines" :key="l.name">
        <span class="g-t">NvScreenStatusCard</span><NvScreenStatusCard v-bind="l" />
      </div>
      <div class="g-cell g-span2"><span class="g-t">NvAlarmTable</span><NvAlarmTable /></div>
      <div class="g-cell">
        <span class="g-t">NvScreenStatusLight / NvScreenStatusTag</span>
        <NvScreenPanel
          ><div class="g-row">
            <NvScreenStatusLight tone="run" label="运行" /><NvScreenStatusLight
              tone="idle"
              label="待机"
            /><NvScreenStatusLight tone="alarm" label="报警" />
          </div>
          <div class="g-row" style="margin-top: 12px">
            <NvScreenStatusTag tone="run">运行中</NvScreenStatusTag
            ><NvScreenStatusTag tone="idle">待机</NvScreenStatusTag
            ><NvScreenStatusTag tone="alarm">报警</NvScreenStatusTag>
          </div></NvScreenPanel
        >
      </div>
      <div class="g-cell g-span3"><span class="g-t">NvKpiBar</span><NvKpiBar /></div>
    </div>

    <h3 class="g-cat">控件</h3>
    <div class="g-grid">
      <div class="g-cell">
        <span class="g-t">NvScreenButton</span
        ><NvScreenPanel
          ><div class="g-row">
            <NvScreenButton>主操作</NvScreenButton
            ><NvScreenButton variant="secondary">次要</NvScreenButton
            ><NvScreenButton variant="ghost">幽灵</NvScreenButton>
          </div></NvScreenPanel
        >
      </div>
      <div class="g-cell">
        <span class="g-t">NvScreenInput</span
        ><NvScreenPanel><NvScreenInput v-model="input" suffix="件" /></NvScreenPanel>
      </div>
      <div class="g-cell">
        <span class="g-t">NvScreenSearch</span
        ><NvScreenPanel><NvScreenSearch v-model="search" /></NvScreenPanel>
      </div>
      <div class="g-cell">
        <span class="g-t">NvScreenSelect</span
        ><NvScreenPanel><NvScreenSelect v-model="sel" /></NvScreenPanel>
      </div>
      <div class="g-cell">
        <span class="g-t">NvScreenSegmented</span
        ><NvScreenPanel
          ><div class="g-center"><NvScreenSegmented v-model="seg" /></div
        ></NvScreenPanel>
      </div>
      <div class="g-cell">
        <span class="g-t">NvScreenSwitch</span
        ><NvScreenPanel
          ><div class="g-center"><NvScreenSwitch v-model="sw" /></div
        ></NvScreenPanel>
      </div>
      <div class="g-cell g-span2">
        <span class="g-t">NvScreenTabs</span
        ><NvScreenPanel><NvScreenTabs v-model="tabs" /></NvScreenPanel>
      </div>
      <div class="g-cell g-span3"><span class="g-t">NvScreenTable</span><NvScreenTable /></div>
      <div class="g-cell g-span2">
        <span class="g-t">NvScreenPagination</span
        ><NvScreenPanel
          ><NvScreenPagination v-model:page="page" :total="248" :page-size="10"
        /></NvScreenPanel>
      </div>
    </div>
  </div>
</template>

<style scoped>
.g-stage {
  background: radial-gradient(
    130% 100% at 50% -8%,
    #081328 0%,
    #060d1c 38%,
    #04080f 68%,
    #03050a 100%
  );
  min-height: 100vh;
  padding: 26px 30px 60px;
  color: var(--sb-text);
  font-family:
    ui-sans-serif,
    system-ui,
    -apple-system,
    'Segoe UI',
    'PingFang SC',
    'Microsoft YaHei',
    sans-serif;
}
.g-cat {
  margin: 30px 0 14px;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.16em;
  color: var(--sb-cyan);
  text-transform: uppercase;
}
.g-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 18px;
}
.g-span2 {
  grid-column: span 2;
}
.g-span3 {
  grid-column: span 3;
}
.g-cell {
  position: relative;
}
.g-t {
  display: block;
  font-size: 11px;
  color: var(--sb-faint);
  margin-bottom: 7px;
  letter-spacing: 0.04em;
}
.g-fill {
  color: var(--sb-text-2);
  font-size: 13px;
}
.g-pad {
  padding: 18px;
}
.g-pad2 {
  padding: 10px 0;
}
.g-center {
  display: grid;
  place-items: center;
  padding: 6px 0;
}
.g-row {
  display: flex;
  align-items: center;
  gap: 14px;
  flex-wrap: wrap;
}
</style>
