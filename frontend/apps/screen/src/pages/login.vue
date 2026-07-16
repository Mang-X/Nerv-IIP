<script setup lang="ts">
import { useNow } from '@vueuse/core'
import { Lock, User } from '@lucide/vue'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import heroUrl from '@/assets/login-hero.webp'
import { IS_REAL_DATA } from '@/data/config'
import { useAuthStore } from '@/stores/auth'
import { useRealAuthStore } from '@/stores/realAuth'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const account = ref('')
const password = ref('')
const loading = ref(false)
const errorMsg = ref('')

// 秒级展示：固定 1s 间隔，避免默认 rAF 每帧触发重算
const now = useNow({ interval: 1000 })
function pad(n: number) {
  return String(n).padStart(2, '0')
}
const clock = computed(() => {
  const d = now.value
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
})

// 呼吸微光：坐标为背景插画（1672×941）内各模块的百分比位置，随 .art 盒子等比缩放
const glows = [
  { x: 10.0, y: 36.0, s: 90, tone: 'cyan', d: 0 }, // 人员/CRM
  { x: 20.0, y: 29.5, s: 80, tone: 'amber', d: 1.2 }, // 图表/经营
  { x: 43.3, y: 33.0, s: 84, tone: 'cyan', d: 2.4 }, // 盾牌/质量
  { x: 12.2, y: 61.0, s: 88, tone: 'cyan', d: 3.4 }, // 云/IIoT
  { x: 28.5, y: 51.0, s: 130, tone: 'cyan', d: 0.8 }, // 中央产线/MES
  { x: 31.0, y: 72.0, s: 86, tone: 'amber', d: 2.0 }, // 下方产线
  { x: 48.5, y: 58.0, s: 96, tone: 'cyan', d: 4.2 }, // 仓库/WMS
]

// 全屏氛围粒子（视口百分比定位，缓慢上浮呼吸，克制）
const particles = [
  { x: 8, y: 22, s: 7, tone: 'cyan', dur: 12, d: 0, dy: -18 },
  { x: 16, y: 78, s: 5, tone: 'indigo', dur: 15, d: 2.4, dy: -14 },
  { x: 24, y: 12, s: 6, tone: 'cyan', dur: 10, d: 4.8, dy: -20 },
  { x: 33, y: 88, s: 8, tone: 'cyan', dur: 13, d: 1.2, dy: -16 },
  { x: 42, y: 30, s: 5, tone: 'indigo', dur: 16, d: 5.6, dy: -12 },
  { x: 50, y: 66, s: 6, tone: 'cyan', dur: 11, d: 3.2, dy: -22 },
  { x: 57, y: 14, s: 7, tone: 'amber', dur: 14, d: 6.4, dy: -15 },
  { x: 63, y: 82, s: 5, tone: 'cyan', dur: 12, d: 0.8, dy: -18 },
  { x: 70, y: 40, s: 6, tone: 'indigo', dur: 15, d: 4.0, dy: -13 },
  { x: 78, y: 70, s: 8, tone: 'cyan', dur: 10, d: 2.0, dy: -20 },
  { x: 85, y: 24, s: 5, tone: 'cyan', dur: 13, d: 5.2, dy: -16 },
  { x: 91, y: 56, s: 6, tone: 'indigo', dur: 16, d: 1.6, dy: -14 },
  { x: 95, y: 86, s: 7, tone: 'cyan', dur: 12, d: 3.8, dy: -19 },
  { x: 74, y: 8, s: 5, tone: 'amber', dur: 14, d: 7.2, dy: -12 },
]

function redirectTarget(): string {
  const redirect = route.query.redirect
  return typeof redirect === 'string' && redirect.startsWith('/') && !redirect.startsWith('//')
    ? redirect
    : '/'
}

async function onSubmit() {
  if (loading.value) return
  loading.value = true
  errorMsg.value = ''
  try {
    if (IS_REAL_DATA) {
      // 真实登录：@nerv-iip/auth 会话，token 由 api-client 拦截器自动注入。
      await useRealAuthStore().login(account.value, password.value)
      await router.push(redirectTarget())
    } else {
      // mock：本地登录后进入大屏（演示模式，任意账号密码）。
      await new Promise((resolve) => setTimeout(resolve, 480))
      auth.login(account.value)
      await router.push('/')
    }
  } catch (error) {
    errorMsg.value = error instanceof Error ? error.message : '登录失败，请重试'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login">
    <!-- 左：数字工厂插画（列内居中自适应） + 模块呼吸微光 -->
    <div class="art-col" aria-hidden="true">
      <div class="art">
        <img class="art-img" :src="heroUrl" alt="" />
        <span
          v-for="(g, i) in glows"
          :key="i"
          class="glow"
          :class="g.tone"
          :style="{
            left: `${g.x}%`,
            top: `${g.y}%`,
            width: `${g.s}px`,
            height: `${g.s}px`,
            animationDelay: `${g.d}s`,
          }"
        />
      </div>
    </div>

    <!-- 全屏氛围：漂浮粒子 + 极淡流光 -->
    <div class="fx" aria-hidden="true">
      <span
        v-for="(p, i) in particles"
        :key="i"
        class="p"
        :class="p.tone"
        :style="{
          left: `${p.x}%`,
          top: `${p.y}%`,
          width: `${p.s}px`,
          height: `${p.s}px`,
          animationDuration: `${p.dur}s`,
          animationDelay: `${p.d}s`,
          '--dy': `${p.dy}px`,
        }"
      />
      <span class="streak s1" />
      <span class="streak s2" />
    </div>

    <!-- 四角 HUD meta -->
    <div class="hud tl">CONTROL PLANE · v0.1</div>
    <div class="hud tr mono">{{ clock }}</div>
    <div class="hud bl"><i class="live" aria-hidden="true" />系统在线</div>
    <div class="hud br mono">华东一厂 · EAST-01</div>

    <!-- 右：登录面板 -->
    <form class="panel" @submit.prevent="onSubmit">
      <div class="brand reveal" style="--d: 0.08s">
        <span class="logo-mark" aria-hidden="true" />
        <span class="logo-word">NERV<span class="sep">·</span>IIP</span>
      </div>
      <div class="head reveal" style="--d: 0.16s">
        <h1>工业数据指挥中心</h1>
        <p>请使用工厂账号进入大屏</p>
      </div>

      <label class="field-row reveal" style="--d: 0.26s">
        <span class="lbl">账号</span>
        <span class="inp">
          <User class="ic" :size="18" aria-hidden="true" />
          <input v-model="account" type="text" placeholder="工厂账号" autocomplete="username" />
        </span>
      </label>

      <label class="field-row reveal" style="--d: 0.34s">
        <span class="lbl">密码</span>
        <span class="inp">
          <Lock class="ic" :size="18" aria-hidden="true" />
          <input
            v-model="password"
            type="password"
            placeholder="登录密码"
            autocomplete="current-password"
          />
        </span>
      </label>

      <button class="submit reveal" style="--d: 0.42s" type="submit" :disabled="loading">
        <span>{{ loading ? '登录中' : '进入大屏' }}</span>
        <svg v-if="!loading" class="arrow" viewBox="0 0 16 16" aria-hidden="true">
          <path
            d="M3 8h10M9 4l4 4-4 4"
            fill="none"
            stroke="currentColor"
            stroke-width="1.6"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
        <span v-else class="spinner" aria-hidden="true" />
      </button>

      <p v-if="errorMsg" class="err reveal" role="alert">{{ errorMsg }}</p>
      <p class="hint reveal" style="--d: 0.5s">
        {{
          IS_REAL_DATA
            ? '请使用工厂账号登录（连接真实数据）'
            : '演示模式 · 任意账号密码即可进入（数据为 mock）'
        }}
      </p>
    </form>
  </div>
</template>

<style scoped>
@layer app {
  .login {
    position: fixed;
    inset: 0;
    display: flex;
    align-items: center;
    /* 与插画底色一致，插画右缘渐隐后无缝延展 */
    background: #050a15;
    color: var(--nv-scr-text);
    overflow: hidden;
    font-variant-numeric: tabular-nums;
  }

  /* ── 左：插画列（占满剩余宽度，插画内容区始终居中、随列宽自适应缩放） ── */
  .art-col {
    position: relative;
    flex: 1 1 auto;
    min-width: 0;
    height: 100%;
    container-type: size;
    pointer-events: none;
  }
  .art {
    position: absolute;
    top: 0;
    height: 100%;
    aspect-ratio: 1672 / 941;
    /* 全高铺满（上下无缝出血）。水平：列够宽时贴左缘；列变窄时向左平移，
     让插画内容中心（约整图宽 30.5% 处 = 54.2cqh）对准列中心；
     min() 钳制保证图左缘永不进入可视区露出底色 */
    left: min(0px, calc(50cqw - 54.2cqh));
  }
  .art-img {
    width: 100%;
    height: 100%;
    display: block;
  }
  /* 右缘渐入底色（替代 mask，合成更轻） */
  .art::after {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(90deg, transparent 70%, #050a15 99%);
  }
  /* 模块呼吸微光（叠在插画各模块上，克制） */
  .glow {
    position: absolute;
    translate: -50% -50%;
    border-radius: 50%;
    opacity: 0;
    animation: glow-breathe 5.6s ease-in-out infinite;
  }
  .glow.cyan {
    background: radial-gradient(circle, rgba(74, 166, 238, 0.2), transparent 68%);
  }
  .glow.amber {
    background: radial-gradient(circle, rgba(242, 193, 78, 0.16), transparent 68%);
  }

  /* ── 全屏氛围层：漂浮粒子 + 极淡流光 ── */
  .fx {
    position: absolute;
    inset: 0;
    overflow: hidden;
    pointer-events: none;
    z-index: 1;
  }
  .p {
    position: absolute;
    translate: -50% -50%;
    border-radius: 50%;
    opacity: 0;
    animation-name: p-float;
    animation-timing-function: ease-in-out;
    animation-iteration-count: infinite;
    animation-direction: alternate;
  }
  .p.cyan {
    background: radial-gradient(circle, rgba(120, 190, 245, 0.55), transparent 70%);
  }
  .p.indigo {
    background: radial-gradient(circle, rgba(139, 155, 230, 0.5), transparent 70%);
  }
  .p.amber {
    background: radial-gradient(circle, rgba(242, 193, 78, 0.4), transparent 70%);
  }
  @keyframes p-float {
    from {
      transform: translateY(6px);
      opacity: 0.08;
    }
    to {
      transform: translateY(var(--dy, -16px));
      opacity: 0.5;
    }
  }
  .streak {
    position: absolute;
    width: 150px;
    height: 1px;
    left: -160px;
    background: linear-gradient(90deg, transparent, rgba(74, 166, 238, 0.38), transparent);
    opacity: 0;
    animation: streak-move linear infinite;
  }
  .s1 {
    top: 20%;
    animation-duration: 26s;
    animation-delay: 4s;
  }
  .s2 {
    top: 68%;
    animation-duration: 34s;
    animation-delay: 16s;
  }
  @keyframes streak-move {
    0% {
      transform: translateX(0) rotate(-12deg);
      opacity: 0;
    }
    6% {
      opacity: 0.45;
    }
    46% {
      opacity: 0.3;
    }
    60% {
      transform: translateX(115vw) rotate(-12deg);
      opacity: 0;
    }
    100% {
      transform: translateX(115vw) rotate(-12deg);
      opacity: 0;
    }
  }

  /* ── 四角 HUD ── */
  .hud {
    position: absolute;
    font-size: 12px;
    letter-spacing: 0.08em;
    color: var(--nv-scr-faint);
    display: inline-flex;
    align-items: center;
    gap: 7px;
    z-index: 2;
  }
  .hud.mono {
    letter-spacing: 0.04em;
  }
  .hud.tl {
    top: 32px;
    left: 34px;
  }
  .hud.tr {
    top: 32px;
    right: 34px;
  }
  .hud.bl {
    bottom: 32px;
    left: 34px;
  }
  .hud.br {
    bottom: 32px;
    right: 34px;
  }
  .live {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--nv-scr-green);
    box-shadow: 0 0 8px var(--nv-scr-green);
    animation: breathe 4.5s ease-in-out infinite;
  }

  /* ── 右：登录面板（置于插画留白区） ── */
  .panel {
    position: relative;
    z-index: 2;
    width: min(452px, calc(100vw - 44px));
    margin-left: auto;
    margin-right: clamp(28px, 9vw, 170px);
    padding: 52px 56px 40px;
    border-radius: 16px;
    border: 1px solid var(--nv-scr-line-2);
    background: linear-gradient(180deg, rgba(17, 26, 45, 0.82), rgba(9, 14, 26, 0.78));
    box-shadow:
      inset 0 1px 0 var(--nv-scr-highlight),
      0 40px 100px -50px rgba(0, 0, 0, 0.92);
  }

  .brand {
    display: inline-flex;
    align-items: center;
    gap: 11px;
    margin-bottom: 30px;
  }
  .logo-mark {
    width: 19px;
    height: 19px;
    border-radius: 5px;
    background: var(--nv-scr-accent-fill);
    box-shadow: var(--nv-scr-glow);
    animation: breathe 4s ease-in-out infinite;
  }
  .logo-word {
    font-size: 17px;
    font-weight: 600;
    letter-spacing: 0.17em;
    color: #fff;
  }
  .sep {
    color: var(--nv-scr-cyan);
    margin: 0 1px;
  }

  .head {
    margin-bottom: 40px;
  }
  .head h1 {
    margin: 0;
    font-size: 29px;
    font-weight: 600;
    letter-spacing: -0.01em;
    color: #fff;
    text-wrap: balance;
  }
  .head p {
    margin: 12px 0 0;
    font-size: 14px;
    color: var(--nv-scr-muted);
  }

  .field-row {
    display: block;
    margin-bottom: 22px;
  }
  .lbl {
    display: block;
    font-size: 12.5px;
    letter-spacing: 0.06em;
    color: var(--nv-scr-muted);
    margin-bottom: 10px;
  }
  .inp {
    position: relative;
    display: flex;
    align-items: center;
  }
  .ic {
    position: absolute;
    left: 15px;
    color: var(--nv-scr-faint);
    transition: color 0.25s var(--nv-scr-ease);
  }
  .inp input {
    width: 100%;
    box-sizing: border-box;
    height: 50px;
    padding: 0 16px 0 44px;
    border-radius: 10px;
    background: rgba(255, 255, 255, 0.03);
    border: 1px solid var(--nv-scr-line-2);
    color: var(--nv-scr-text);
    font-size: 15px;
    outline: none;
    transition:
      border-color 0.25s var(--nv-scr-ease),
      background-color 0.25s var(--nv-scr-ease),
      box-shadow 0.25s var(--nv-scr-ease);
  }
  .inp input::placeholder {
    color: var(--nv-scr-faint);
  }
  .inp:focus-within .ic {
    color: var(--nv-scr-cyan);
  }
  .inp input:focus {
    border-color: var(--nv-scr-accent-edge);
    background: rgba(74, 166, 238, 0.06);
    box-shadow: 0 0 0 3px rgba(74, 166, 238, 0.12);
  }

  .submit {
    width: 100%;
    height: 52px;
    margin-top: 14px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 9px;
    border: 0;
    border-radius: 10px;
    cursor: pointer;
    font-size: 15px;
    font-weight: 600;
    letter-spacing: 0.05em;
    color: #04101f;
    background: var(--nv-scr-accent-fill);
    box-shadow: inset 0 1px 0 var(--nv-scr-highlight);
    transition:
      filter 0.25s var(--nv-scr-ease),
      transform 0.12s var(--nv-scr-ease);
  }
  .submit:hover {
    filter: brightness(1.07);
  }
  .submit:active {
    transform: scale(0.985);
  }
  .submit:disabled {
    cursor: default;
    filter: saturate(0.7) brightness(0.92);
  }
  .arrow {
    width: 16px;
    height: 16px;
    transition: transform 0.25s var(--nv-scr-ease);
  }
  .submit:hover .arrow {
    transform: translateX(3px);
  }
  .spinner {
    width: 15px;
    height: 15px;
    border: 2px solid rgba(4, 16, 31, 0.35);
    border-top-color: #04101f;
    border-radius: 50%;
    animation: spin-fast 0.7s linear infinite;
  }
  .err {
    margin: 18px 0 0;
    padding: 9px 12px;
    border-radius: 8px;
    border: 1px solid rgba(239, 90, 99, 0.4);
    background: rgba(239, 90, 99, 0.1);
    text-align: center;
    font-size: 12.5px;
    color: var(--nv-scr-red);
  }
  .hint {
    margin: 24px 0 0;
    text-align: center;
    font-size: 12px;
    color: var(--nv-scr-faint);
  }

  /* ── 动效：克制、慢 ── */
  .reveal {
    animation: reveal 0.7s var(--nv-scr-ease-emphasized) both;
    animation-delay: var(--d, 0s);
  }
  @keyframes reveal {
    from {
      opacity: 0;
      transform: translateY(14px);
    }
    to {
      opacity: 1;
      transform: none;
    }
  }
  @keyframes breathe {
    0%,
    100% {
      opacity: 0.55;
    }
    50% {
      opacity: 1;
    }
  }
  @keyframes glow-breathe {
    0%,
    100% {
      opacity: 0.1;
    }
    50% {
      opacity: 0.85;
    }
  }
  @keyframes spin-fast {
    to {
      transform: rotate(360deg);
    }
  }

  /* 窄屏：插画压暗作底（脱离布局流、全屏居中），面板居中 */
  @media (max-width: 960px) {
    .panel {
      margin-right: auto;
    }
    .art-col {
      position: absolute;
      inset: 0;
      opacity: 0.4;
    }
    .hud.tr,
    .hud.br {
      display: none;
    }
  }
  @media (max-width: 560px) {
    .panel {
      padding: 44px 32px 34px;
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .reveal,
    .logo-mark,
    .live,
    .glow,
    .spinner,
    .p {
      animation: none;
    }
    .reveal {
      opacity: 1;
      transform: none;
    }
    .glow {
      opacity: 0.3;
    }
    .p {
      opacity: 0.15;
    }
    .streak {
      display: none;
    }
  }
}
</style>
