<script setup lang="ts">
import {
  Activity,
  AlertTriangle,
  Calendar,
  CheckCircle2,
  ClipboardCheck,
  ClipboardList,
  Clock,
  Filter,
  Info,
  ListChecks,
  Menu,
  Monitor,
  ShieldCheck,
  Zap,
} from 'lucide-vue-next'
import { onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * MES 运营看板 — independent screen surface (decoupled from the PC/mobile token
 * sets, follows only the shared design philosophy: restraint, real codes, honest
 * data). Fixed 1920×1080 design canvas, scaled to fit any viewport.
 */
const stage = ref<HTMLElement>()
const scale = ref(1)
function fit() {
  scale.value = Math.min(window.innerWidth / 1920, window.innerHeight / 1080)
}
onMounted(() => {
  fit()
  window.addEventListener('resize', fit)
})
onBeforeUnmount(() => window.removeEventListener('resize', fit))

const lines = [
  { name: '焊接线 A', state: '运行', label: '运行中', tone: 'run', plan: '1,240', act: '1,156', rate: '93.2%', down: '28 分钟' },
  { name: '装配线 B', state: '待机', label: '待机中', tone: 'idle', plan: '980', act: '742', rate: '75.7%', down: '42 分钟' },
  { name: 'CNC 线 C', state: '报警', label: '报警中', tone: 'alarm', plan: '760', act: '312', rate: '41.1%', down: '114 分钟' },
]

// takt segments per line (width % + tone)
const takt = [
  { nm: '焊接线 A', segs: [['run', 34], ['idle', 6], ['run', 60]] },
  { nm: '装配线 B', segs: [['idle', 22], ['stop', 8], ['run', 18], ['idle', 30], ['run', 22]] },
  { nm: 'CNC 线 C', segs: [['alarm', 14], ['stop', 20], ['run', 12], ['alarm', 10], ['stop', 14], ['run', 12], ['alarm', 18]] },
]

const alarms = [
  { time: '10:23:14', line: 'CNC 线 C', lvl: 'sev', name: '主轴电机过载', wo: 'WO-2406-0421', st: '未确认' },
  { time: '10:18:07', line: '焊接线 A', lvl: 'gen', name: '焊枪温度异常', wo: 'WO-2406-0418', st: '处理中' },
  { time: '10:12:55', line: '装配线 B', lvl: 'gen', name: '物料短缺', wo: 'WO-2406-0415', st: '处理中' },
  { time: '10:05:31', line: 'CNC 线 C', lvl: 'sev', name: '刀具寿命到期', wo: 'WO-2406-0409', st: '待确认' },
  { time: '09:58:22', line: '焊接线 A', lvl: 'gen', name: '气压低于阈值', wo: 'WO-2406-0406', st: '已恢复' },
]
</script>

<template>
  <div class="sb-fit">
    <div ref="stage" class="sb-stage" :style="{ transform: `scale(${scale})` }">
      <div class="sb-grid" /><div class="sb-noise" /><div class="sb-glow" />
      <div class="sb-wrap">

        <!-- Header -->
        <header class="sb-hd">
          <h1>智能工厂 MES 运营看板</h1>
          <div class="sb-tools">
            <span><Clock :size="15" />2024-06-12 10:24:36</span>
            <span><Calendar :size="15" />星期三</span>
            <span><Filter :size="15" />全部产线</span>
            <span><Monitor :size="15" />中央控制室大屏 01</span>
            <Menu :size="18" />
          </div>
        </header>

        <!-- Row 1 -->
        <div class="sb-row r1">
          <section class="sb-panel">
            <div class="sb-ph"><span class="t">设备综合效率 OEE <Info :size="13" /></span></div>
            <div class="oee"><div class="oee-n">92.4<small>%</small></div><div class="oee-d">较昨日 +2.7%</div></div>
            <svg class="oee-spark" viewBox="0 0 460 96" preserveAspectRatio="none">
              <defs>
                <linearGradient id="sbOee" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#00E5FF" stop-opacity=".25" /><stop offset="1" stop-color="#00E5FF" stop-opacity="0" /></linearGradient>
                <filter id="sbGl" x="-5%" y="-40%" width="110%" height="180%"><feGaussianBlur stdDeviation="2.2" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
              </defs>
              <path d="M0 74 L46 62 L92 68 L138 48 L184 58 L230 40 L276 54 L322 32 L368 46 L414 28 L460 22 V96 H0 Z" fill="url(#sbOee)" />
              <path d="M0 74 L46 62 L92 68 L138 48 L184 58 L230 40 L276 54 L322 32 L368 46 L414 28 L460 22" fill="none" stroke="#00E5FF" stroke-width="2" filter="url(#sbGl)" />
            </svg>
            <div class="sb-axis"><span>00:00</span><span>06:00</span><span>12:00</span><span>18:00</span><span>24:00</span></div>
          </section>

          <section v-for="l in lines" :key="l.name" class="sb-panel lc">
            <span class="sb-accent" :class="l.tone" />
            <div class="lc-top"><div class="lc-nm">{{ l.name }} · {{ l.state }}</div><span class="lc-dot" :class="l.tone" /></div>
            <div class="lc-state">当前状态</div>
            <div class="lc-big" :class="l.tone">{{ l.label }}</div>
            <div class="lc-stats">
              <div><i>计划产量</i><b>{{ l.plan }} 件</b></div>
              <div><i>实际产量</i><b>{{ l.act }} 件</b></div>
              <div><i>达成率</i><b>{{ l.rate }}</b></div>
            </div>
            <div class="lc-foot"><span>停机时长</span><b>{{ l.down }}</b></div>
          </section>
        </div>

        <!-- Row 2 takt -->
        <div class="sb-row">
          <section class="sb-panel">
            <div class="sb-ph"><span class="t">节拍 Takt 58s</span>
              <div class="legend"><span><i class="run" />运行</span><span><i class="idle" />待机</span><span><i class="stop" />停机</span><span><i class="alarm" />报警</span></div>
            </div>
            <div class="gantt-axis"><span>09:30</span><span>09:40</span><span>09:50</span><span>10:00</span><span>10:10</span><span>10:20</span><span>10:30</span></div>
            <div v-for="r in takt" :key="r.nm" class="gantt-row">
              <span class="nm">{{ r.nm }}</span>
              <div class="gantt-bar"><span v-for="(s, i) in r.segs" :key="i" class="seg" :class="s[0]" :style="{ width: s[1] + '%' }" /></div>
            </div>
          </section>
        </div>

        <!-- Row 3 -->
        <div class="sb-row r3">
          <section class="sb-panel trend">
            <div class="sb-ph"><span class="t">产量趋势（件）<em>— 实际产量　--- 计划产量</em></span>
              <div class="tabs"><span class="on">今日</span><span>近7天</span><span>近30天</span></div>
            </div>
            <div class="trend-body">
              <div class="trend-y"><span>1,500</span><span>1,200</span><span>900</span><span>600</span><span>300</span><span>0</span></div>
              <svg class="trend-svg" viewBox="0 0 940 300" preserveAspectRatio="none">
                <defs>
                  <linearGradient id="sbTr" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#00E5FF" stop-opacity=".2" /><stop offset="1" stop-color="#00E5FF" stop-opacity="0" /></linearGradient>
                  <filter id="sbTrGl" x="-3%" y="-40%" width="106%" height="180%"><feGaussianBlur stdDeviation="2.4" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
                </defs>
                <g stroke="rgba(255,255,255,.045)" stroke-dasharray="3 6"><line x1="46" y1="22" x2="940" y2="22" /><line x1="46" y1="78" x2="940" y2="78" /><line x1="46" y1="134" x2="940" y2="134" /><line x1="46" y1="190" x2="940" y2="190" /><line x1="46" y1="246" x2="940" y2="246" /></g>
                <path d="M46 286 L130 220 L215 168 L305 150 L400 118 L495 138 L548 96 L600 150 L690 128 L770 132 L860 130 L920 132 V300 H46 Z" fill="url(#sbTr)" />
                <path d="M46 286 L130 220 L215 168 L305 150 L400 118 L495 138 L548 96 L600 150 L690 128 L770 132 L860 130 L920 132" fill="none" stroke="#00E5FF" stroke-width="2.2" filter="url(#sbTrGl)" />
                <path d="M46 280 L170 198 L320 150 L460 120 L548 106 L680 90 L800 68 L920 54" fill="none" stroke="#A78BFA" stroke-width="1.5" stroke-dasharray="5 5" opacity=".85" />
                <line x1="548" y1="22" x2="548" y2="288" stroke="#00E5FF" stroke-width="1" stroke-dasharray="2 4" opacity=".55" />
                <circle cx="548" cy="96" r="4" fill="#00E5FF" filter="url(#sbTrGl)" />
                <g transform="translate(440,150)"><rect width="160" height="66" rx="6" fill="#0a1322" stroke="rgba(0,229,255,.3)" /><text x="14" y="23" fill="#7A8699" font-size="12">10:00</text><text x="14" y="43" fill="#fff" font-size="13">● 实际产量　1,086</text><text x="14" y="59" fill="#A78BFA" font-size="13">┄ 计划产量　1,150</text></g>
              </svg>
            </div>
            <div class="trend-x"><span>00:00</span><span>04:00</span><span>08:00</span><span>12:00</span><span>16:00</span><span>20:00</span><span>24:00</span></div>
          </section>

          <section class="sb-panel">
            <div class="sb-ph"><span class="t">告警列表</span><span class="more">查看全部 ›</span></div>
            <table class="atbl">
              <thead><tr><th>告警时间</th><th>产线</th><th>告警级别</th><th>告警内容</th><th>工单号</th><th>状态</th></tr></thead>
              <tbody>
                <tr v-for="a in alarms" :key="a.wo">
                  <td>{{ a.time }}</td><td>{{ a.line }}</td>
                  <td><span class="lvl" :class="a.lvl"><i />{{ a.lvl === 'sev' ? '严重' : '一般' }}</span></td>
                  <td>{{ a.name }}</td><td class="mono">{{ a.wo }}</td>
                  <td :class="{ ok: a.st === '已恢复' }">{{ a.st }}</td>
                </tr>
              </tbody>
            </table>
          </section>
        </div>

        <!-- Row 4 KPIs -->
        <div class="sb-kpis">
          <div class="kpi"><span class="ic"><ClipboardList :size="19" /></span><div><div class="v">24</div><div class="k">工单总数</div></div></div>
          <div class="kpi"><span class="ic"><ListChecks :size="19" /></span><div><div class="v">8</div><div class="k">进行中</div></div></div>
          <div class="kpi"><span class="ic"><ClipboardCheck :size="19" /></span><div><div class="v">16</div><div class="k">已完成</div></div></div>
          <div class="kpi">
            <svg width="42" height="42" viewBox="0 0 42 42" class="ring"><circle cx="21" cy="21" r="16.5" fill="none" stroke="rgba(255,255,255,.08)" stroke-width="4" /><circle cx="21" cy="21" r="16.5" fill="none" stroke="#00E5FF" stroke-width="4" stroke-linecap="round" stroke-dasharray="103.7" stroke-dashoffset="2.8" transform="rotate(-90 21 21)" /></svg>
            <div><div class="v cy">97.3%</div><div class="k">良品率</div></div>
          </div>
          <div class="kpi"><span class="ic warn"><AlertTriangle :size="19" /></span><div><div class="v">36</div><div class="k">不良数</div></div></div>
          <div class="kpi"><span class="ic"><Zap :size="19" /></span><div><div class="v">1,284<em> kWh</em></div><div class="k">能耗电量</div></div></div>
          <div class="kpi"><span class="ic"><ShieldCheck :size="19" /></span><div><div class="v">128<em> 天</em></div><div class="k">安全运行</div></div></div>
          <div class="kpi"><span class="ic okc"><Activity :size="19" /></span><div><div class="v ok">正常</div><div class="k">系统状态</div></div></div>
        </div>

      </div>
    </div>
  </div>
</template>

<style scoped>
.sb-fit{position:fixed;inset:0;z-index:9999;display:flex;align-items:center;justify-content:center;background:#05070d;overflow:hidden}
.sb-stage{position:relative;width:1920px;height:1080px;flex:none;transform-origin:center;
  background:radial-gradient(120% 82% at 50% -6%, #16294e 0%, #0e1a32 36%, #0a1120 60%, #080c16 80%, #05070d 100%);
  font-variant-numeric:tabular-nums;color:#fff;
  font-family:ui-sans-serif,system-ui,-apple-system,"Segoe UI","PingFang SC","Microsoft YaHei",sans-serif}
.sb-grid{position:absolute;inset:0;background-image:linear-gradient(rgba(125,170,255,.06) 1px,transparent 1px),linear-gradient(90deg,rgba(125,170,255,.06) 1px,transparent 1px);background-size:60px 60px;mask-image:radial-gradient(90% 80% at 50% 28%,#000 20%,transparent 100%);opacity:.5}
.sb-noise{position:absolute;inset:0;pointer-events:none;opacity:.035;mix-blend-mode:overlay;background-image:url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='160' height='160'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E")}
.sb-glow{position:absolute;top:-160px;left:50%;width:900px;height:380px;transform:translateX(-50%);background:radial-gradient(closest-side,rgba(0,160,220,.12),transparent)}
.sb-wrap{position:relative;height:100%;display:flex;flex-direction:column;padding:0 30px 24px}

.sb-hd{height:66px;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid rgba(255,255,255,.07)}
.sb-hd h1{font-size:26px;font-weight:600;letter-spacing:.03em}
.sb-tools{display:flex;align-items:center;gap:28px;color:#7A8699;font-size:14px}
.sb-tools span{display:inline-flex;align-items:center;gap:8px}
.sb-tools svg{color:#5f6b80}

.sb-row{display:grid;gap:18px;margin-top:18px}
.r1{grid-template-columns:1.4fr 1fr 1fr 1fr}
.r3{grid-template-columns:1.55fr 1fr;flex:1;min-height:0}

.sb-panel{position:relative;background:linear-gradient(180deg,#0f1a2e,#0b1320);border:1px solid rgba(255,255,255,.07);border-radius:7px;padding:17px 20px;box-shadow:inset 0 1px 0 rgba(255,255,255,.045),0 2px 12px -6px rgba(0,0,0,.6);overflow:hidden}
.sb-panel::before{content:"";position:absolute;inset:0;border-radius:7px;background:linear-gradient(140deg,rgba(125,170,255,.05),transparent 40%);pointer-events:none}
.sb-ph{display:flex;align-items:center;justify-content:space-between;margin-bottom:12px}
.sb-ph .t{font-size:16px;font-weight:500;color:#C8D2E0;display:inline-flex;align-items:center;gap:6px}
.sb-ph .t svg{color:#5f6b80}
.sb-ph .t em{font-size:12px;color:#7A8699;font-style:normal;font-weight:400;margin-left:12px}
.sb-ph .more{font-size:13px;color:#7A8699}

.sb-accent{position:absolute;top:0;left:14px;right:14px;height:2px;border-radius:2px}
.sb-accent.run{background:linear-gradient(90deg,transparent,#00E5FF,transparent);box-shadow:0 0 10px rgba(0,229,255,.5)}
.sb-accent.idle{background:linear-gradient(90deg,transparent,#FFD600,transparent);box-shadow:0 0 10px rgba(255,214,0,.45)}
.sb-accent.alarm{background:linear-gradient(90deg,transparent,#FF1744,transparent);box-shadow:0 0 10px rgba(255,23,68,.45)}

.oee{display:flex;align-items:baseline}
.oee-n{font-size:54px;font-weight:700;color:#00E5FF;line-height:1;text-shadow:0 0 24px rgba(0,229,255,.45);letter-spacing:-.01em}
.oee-n small{font-size:25px;font-weight:600}
.oee-d{color:#00E676;font-size:14px;margin-left:13px}
.oee-spark{width:100%;height:92px;margin-top:8px;overflow:visible}
.sb-axis{display:flex;justify-content:space-between;color:#4f5a6e;font-size:11px;margin-top:5px}

.lc-top{display:flex;align-items:center;justify-content:space-between}
.lc-nm{font-size:17px;font-weight:600}
.lc-dot{width:11px;height:11px;border-radius:50%;animation:sbBreathe 2s ease-in-out infinite}
.lc-dot.run{background:#00E676;box-shadow:0 0 9px #00E676} .lc-dot.idle{background:#FFD600;box-shadow:0 0 9px #FFD600} .lc-dot.alarm{background:#FF1744;box-shadow:0 0 9px #FF1744}
.lc-state{font-size:13px;color:#7A8699;margin:14px 0 3px}
.lc-big{font-size:21px;font-weight:600}
.lc-big.run{color:#00E676} .lc-big.idle{color:#FFD600} .lc-big.alarm{color:#FF1744}
.lc-stats{display:flex;justify-content:space-between;margin-top:16px}
.lc-stats i{font-size:12px;color:#7A8699;font-style:normal} .lc-stats b{display:block;font-size:16px;font-weight:600;margin-top:4px}
.lc-foot{display:flex;justify-content:space-between;align-items:center;margin-top:14px;padding-top:12px;border-top:1px solid #18233a;font-size:12px;color:#7A8699}
.lc-foot b{font-size:15px;color:#C8D2E0;font-weight:600}
@keyframes sbBreathe{0%,100%{opacity:.55}50%{opacity:1}}

.legend{display:flex;gap:18px;font-size:12px;color:#7A8699}
.legend span{display:inline-flex;align-items:center;gap:6px}
.legend i{width:11px;height:11px;border-radius:2px}
.run{background:#00E5FF} .idle{background:#FFD600} .stop{background:#37445a} .alarm{background:#FF1744}
.gantt-axis{display:flex;justify-content:space-between;color:#4f5a6e;font-size:11px;margin:6px 0 9px;padding-left:78px}
.gantt-row{display:flex;align-items:center;gap:14px;margin:8px 0}
.gantt-row .nm{width:64px;font-size:12px;color:#7A8699;text-align:right;flex:none}
.gantt-bar{flex:1;height:16px;border-radius:3px;overflow:hidden;display:flex;box-shadow:inset 0 0 0 1px rgba(255,255,255,.04)}
.seg{height:100%}

.trend{display:flex;flex-direction:column}
.tabs{display:flex;border:1px solid rgba(255,255,255,.12);border-radius:6px;overflow:hidden;font-size:12px}
.tabs span{padding:5px 14px;color:#7A8699} .tabs span.on{background:rgba(0,229,255,.13);color:#00E5FF}
.trend-body{flex:1;position:relative;min-height:0}
.trend-y{position:absolute;left:0;top:0;height:100%;display:flex;flex-direction:column;justify-content:space-between;color:#4f5a6e;font-size:11px;padding:4px 0}
.trend-svg{width:100%;height:100%;overflow:visible}
.trend-x{display:flex;justify-content:space-between;color:#4f5a6e;font-size:11px;margin-top:4px;padding-left:46px}

.atbl{width:100%;border-collapse:collapse;font-size:13px}
.atbl th{color:#7A8699;font-weight:400;text-align:left;padding:9px 8px;border-bottom:1px solid #18233a}
.atbl td{padding:9px 8px;border-bottom:1px solid rgba(255,255,255,.05);color:#C8D2E0}
.atbl td.mono{font-family:ui-monospace,monospace;color:#7A8699;font-size:12px}
.atbl td.ok{color:#00E676}
.lvl{display:inline-flex;align-items:center;gap:6px}
.lvl i{width:7px;height:7px;border-radius:50%}
.lvl.sev{color:#FF1744} .lvl.sev i{background:#FF1744;box-shadow:0 0 7px #FF1744}
.lvl.gen{color:#FFD600} .lvl.gen i{background:#FFD600;box-shadow:0 0 7px #FFD600}

.sb-kpis{display:grid;grid-template-columns:repeat(8,1fr);margin-top:18px;border:1px solid rgba(255,255,255,.07);border-radius:8px;background:linear-gradient(180deg,#0f1a2e,#0b1320);padding:15px 0;box-shadow:inset 0 1px 0 rgba(255,255,255,.045)}
.kpi{display:flex;align-items:center;gap:13px;padding:0 22px;position:relative}
.kpi+.kpi::before{content:"";position:absolute;left:0;top:5px;bottom:5px;width:1px;background:#18233a}
.kpi .ic{width:38px;height:38px;border-radius:8px;display:grid;place-items:center;color:#00E5FF;background:rgba(0,229,255,.08);border:1px solid rgba(0,229,255,.18);flex:none}
.kpi .ic.warn{color:#FFD600;background:rgba(255,214,0,.08);border-color:rgba(255,214,0,.18)}
.kpi .ic.okc{color:#00E676;background:rgba(0,230,118,.08);border-color:rgba(0,230,118,.18)}
.kpi .ring{flex:none;filter:drop-shadow(0 0 3px rgba(0,229,255,.4))}
.kpi .v{font-size:22px;font-weight:700} .kpi .v em{font-size:13px;color:#7A8699;font-style:normal} .kpi .v.cy{color:#00E5FF} .kpi .v.ok{color:#00E676}
.kpi .k{font-size:12px;color:#7A8699;margin-top:2px}
@media (prefers-reduced-motion: reduce){.lc-dot{animation:none}}
</style>
