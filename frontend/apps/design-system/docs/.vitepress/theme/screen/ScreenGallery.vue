<script setup lang="ts">
import {
  AlarmTable,
  BorderPanel,
  CapsuleBar,
  DigitalFlop,
  GlowDivider,
  KpiBar,
  OeeHero,
  RingGauge,
  ScreenButton,
  ScreenHeader,
  ScreenInput,
  ScreenPagination,
  ScreenPanel,
  ScreenSearch,
  ScreenSegmented,
  ScreenSelect,
  ScreenSwitch,
  ScreenTable,
  ScreenTabs,
  Sparkline,
  StatusCard,
  StatusLight,
  StatusTag,
  TaktGantt,
  TechFrame,
  TitleBar,
  TrendChart,
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
  { name: '焊接线 A', state: '运行', label: '运行中', tone: 'run', plan: '1,240', actual: '1,156', rate: '93.2%', downtime: '28 分钟' },
  { name: '装配线 B', state: '待机', label: '待机中', tone: 'idle', plan: '980', actual: '742', rate: '75.7%', downtime: '42 分钟' },
  { name: 'CNC 线 C', state: '报警', label: '报警中', tone: 'alarm', plan: '760', actual: '312', rate: '41.1%', downtime: '114 分钟' },
] as const
const tones = { run: '运行中', idle: '待机', alarm: '报警' } as const
</script>

<template>
  <div class="g-stage">
    <ScreenHeader title="智能工厂 · 大屏组件总览" time="2024-06-12 10:24:36" date="星期三" line="全部产线" screen="组件库 Gallery" />

    <h3 class="g-cat">容器 · 外壳</h3>
    <div class="g-grid">
      <div class="g-cell"><span class="g-t">ScreenPanel</span><ScreenPanel title="设备综合效率"><div class="g-fill">面板内容 · hairline + 白高光</div></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">BorderPanel</span><BorderPanel title="班次概览"><div class="g-fill">描边面板 · 顶部 notch</div></BorderPanel></div>
      <div class="g-cell"><span class="g-t">TechFrame</span><TechFrame><div class="g-fill g-pad">科技边框 · 角光渐隐</div></TechFrame></div>
      <div class="g-cell"><span class="g-t">TitleBar</span><div class="g-pad2"><TitleBar title="产线综合监控" sub="REAL-TIME OVERVIEW" /></div></div>
      <div class="g-cell g-span2"><span class="g-t">GlowDivider</span><div class="g-pad2"><GlowDivider /></div></div>
    </div>

    <h3 class="g-cat">指标 · 图表</h3>
    <div class="g-grid">
      <div class="g-cell"><span class="g-t">OeeHero</span><ScreenPanel><OeeHero label="设备综合效率 OEE" :value="92.4" unit="%" delta="较昨日 +2.7%" /></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">RingGauge</span><ScreenPanel><div class="g-center"><RingGauge :value="78" label="稼动率" /></div></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">DigitalFlop</span><ScreenPanel><div class="g-center"><DigitalFlop :value="1156" suffix="件" /></div></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">CapsuleBar</span><ScreenPanel><CapsuleBar /></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">Sparkline</span><ScreenPanel><div style="height:56px"><Sparkline area /></div></ScreenPanel></div>
      <div class="g-cell g-span2"><span class="g-t">TrendChart</span><TrendChart /></div>
      <div class="g-cell g-span2"><span class="g-t">TaktGantt</span><TaktGantt /></div>
    </div>

    <h3 class="g-cat">数据 · 状态</h3>
    <div class="g-grid">
      <div class="g-cell" v-for="l in lines" :key="l.name"><span class="g-t">StatusCard</span><StatusCard v-bind="l" /></div>
      <div class="g-cell g-span2"><span class="g-t">AlarmTable</span><AlarmTable /></div>
      <div class="g-cell"><span class="g-t">StatusLight / StatusTag</span>
        <ScreenPanel><div class="g-row">
          <StatusLight tone="run" label="运行" /><StatusLight tone="idle" label="待机" /><StatusLight tone="alarm" label="报警" />
        </div><div class="g-row" style="margin-top:12px">
          <StatusTag tone="run">运行中</StatusTag><StatusTag tone="idle">待机</StatusTag><StatusTag tone="alarm">报警</StatusTag>
        </div></ScreenPanel>
      </div>
      <div class="g-cell g-span3"><span class="g-t">KpiBar</span><KpiBar /></div>
    </div>

    <h3 class="g-cat">控件</h3>
    <div class="g-grid">
      <div class="g-cell"><span class="g-t">ScreenButton</span><ScreenPanel><div class="g-row">
        <ScreenButton>主操作</ScreenButton><ScreenButton variant="secondary">次要</ScreenButton><ScreenButton variant="ghost">幽灵</ScreenButton>
      </div></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">ScreenInput</span><ScreenPanel><ScreenInput v-model="input" suffix="件" /></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">ScreenSearch</span><ScreenPanel><ScreenSearch v-model="search" /></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">ScreenSelect</span><ScreenPanel><ScreenSelect v-model="sel" /></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">ScreenSegmented</span><ScreenPanel><div class="g-center"><ScreenSegmented v-model="seg" /></div></ScreenPanel></div>
      <div class="g-cell"><span class="g-t">ScreenSwitch</span><ScreenPanel><div class="g-center"><ScreenSwitch v-model="sw" /></div></ScreenPanel></div>
      <div class="g-cell g-span2"><span class="g-t">ScreenTabs</span><ScreenPanel><ScreenTabs v-model="tabs" /></ScreenPanel></div>
      <div class="g-cell g-span3"><span class="g-t">ScreenTable</span><ScreenTable /></div>
      <div class="g-cell g-span2"><span class="g-t">ScreenPagination</span><ScreenPanel><ScreenPagination v-model:page="page" :total="248" :page-size="10" /></ScreenPanel></div>
    </div>
  </div>
</template>

<style scoped>
.g-stage {
  background: radial-gradient(130% 100% at 50% -8%, #0c1730 0%, #0a1224 36%, #070c18 66%, #05080f 100%);
  min-height: 100vh;
  padding: 26px 30px 60px;
  color: var(--sb-text);
  font-family: ui-sans-serif, system-ui, -apple-system, 'Segoe UI', 'PingFang SC', 'Microsoft YaHei', sans-serif;
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
.g-span2 { grid-column: span 2; }
.g-span3 { grid-column: span 3; }
.g-cell { position: relative; }
.g-t {
  display: block;
  font-size: 11px;
  color: var(--sb-faint);
  margin-bottom: 7px;
  letter-spacing: 0.04em;
}
.g-fill { color: var(--sb-text-2); font-size: 13px; }
.g-pad { padding: 18px; }
.g-pad2 { padding: 10px 0; }
.g-center { display: grid; place-items: center; padding: 6px 0; }
.g-row { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
</style>
