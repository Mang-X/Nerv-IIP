<script setup lang="ts">
import { useNow } from '@vueuse/core'
import { Lock, User } from 'lucide-vue-next'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import FusionBg from '@/components/backgrounds/FusionBg.vue'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()
const account = ref('')
const password = ref('')
const loading = ref(false)

const now = useNow()
function pad(n: number) {
  return String(n).padStart(2, '0')
}
const clock = computed(() => {
  const d = now.value
  return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
})

async function onSubmit() {
  if (loading.value) return
  loading.value = true
  await new Promise((resolve) => setTimeout(resolve, 480))
  auth.login(account.value)
  loading.value = false
  router.push('/')
}
</script>

<template>
  <div class="login">
    <FusionBg class="bg" />

    <!-- 四角 HUD meta -->
    <div class="hud tl">CONTROL PLANE · v0.1</div>
    <div class="hud tr mono">{{ clock }}</div>
    <div class="hud bl"><i class="live" aria-hidden="true" />系统在线</div>
    <div class="hud br mono">华东一厂 · EAST-01</div>

    <!-- 居中登录面板 -->
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
          <input v-model="password" type="password" placeholder="登录密码" autocomplete="current-password" />
        </span>
      </label>

      <button class="submit reveal" style="--d: 0.42s" type="submit" :disabled="loading">
        <span>{{ loading ? '登录中' : '进入大屏' }}</span>
        <svg v-if="!loading" class="arrow" viewBox="0 0 16 16" aria-hidden="true">
          <path d="M3 8h10M9 4l4 4-4 4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
        <span v-else class="spinner" aria-hidden="true" />
      </button>

      <p class="hint reveal" style="--d: 0.5s">演示模式 · 任意账号密码即可进入（数据为 mock）</p>
    </form>
  </div>
</template>

<style scoped>
.login {
  position: fixed;
  inset: 0;
  display: grid;
  place-items: center;
  background: radial-gradient(82% 72% at 50% 44%, #0b1426, var(--sb-bg) 78%);
  color: var(--sb-text);
  overflow: hidden;
  font-variant-numeric: tabular-nums;
}
.bg {
  position: absolute;
  inset: 0;
}

/* 四角 HUD */
.hud {
  position: absolute;
  font-size: 12px;
  letter-spacing: 0.08em;
  color: var(--sb-faint);
  display: inline-flex;
  align-items: center;
  gap: 7px;
  z-index: 2;
}
.hud.mono { letter-spacing: 0.04em; }
.hud.tl { top: 32px; left: 34px; }
.hud.tr { top: 32px; right: 34px; }
.hud.bl { bottom: 32px; left: 34px; }
.hud.br { bottom: 32px; right: 34px; }
.live {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--sb-green);
  box-shadow: 0 0 8px var(--sb-green);
  animation: breathe 4.5s ease-in-out infinite;
}

/* ── 居中面板：大气、舒展、纯净圆角发丝边 ── */
.panel {
  position: relative;
  z-index: 2;
  width: min(452px, calc(100vw - 44px));
  padding: 52px 56px 40px;
  border-radius: 16px;
  border: 1px solid var(--sb-line-2);
  background: linear-gradient(180deg, rgba(17, 26, 45, 0.82), rgba(9, 14, 26, 0.78));
  box-shadow:
    inset 0 1px 0 var(--sb-highlight),
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
  background: var(--sb-accent-fill);
  box-shadow: var(--sb-glow);
  animation: breathe 4s ease-in-out infinite;
}
.logo-word {
  font-size: 17px;
  font-weight: 600;
  letter-spacing: 0.17em;
  color: #fff;
}
.sep { color: var(--sb-cyan); margin: 0 1px; }

.head { margin-bottom: 40px; }
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
  color: var(--sb-muted);
}

.field-row { display: block; margin-bottom: 22px; }
.lbl {
  display: block;
  font-size: 12.5px;
  letter-spacing: 0.06em;
  color: var(--sb-muted);
  margin-bottom: 10px;
}
.inp { position: relative; display: flex; align-items: center; }
.ic {
  position: absolute;
  left: 15px;
  color: var(--sb-faint);
  transition: color 0.25s var(--sb-ease);
}
.inp input {
  width: 100%;
  box-sizing: border-box;
  height: 50px;
  padding: 0 16px 0 44px;
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid var(--sb-line-2);
  color: var(--sb-text);
  font-size: 15px;
  outline: none;
  transition:
    border-color 0.25s var(--sb-ease),
    background-color 0.25s var(--sb-ease),
    box-shadow 0.25s var(--sb-ease);
}
.inp input::placeholder { color: var(--sb-faint); }
.inp:focus-within .ic { color: var(--sb-cyan); }
.inp input:focus {
  border-color: var(--sb-accent-edge);
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
  background: var(--sb-accent-fill);
  box-shadow: inset 0 1px 0 var(--sb-highlight);
  transition:
    filter 0.25s var(--sb-ease),
    transform 0.12s var(--sb-ease);
}
.submit:hover { filter: brightness(1.07); }
.submit:active { transform: scale(0.985); }
.submit:disabled { cursor: default; filter: saturate(0.7) brightness(0.92); }
.arrow {
  width: 16px;
  height: 16px;
  transition: transform 0.25s var(--sb-ease);
}
.submit:hover .arrow { transform: translateX(3px); }
.spinner {
  width: 15px;
  height: 15px;
  border: 2px solid rgba(4, 16, 31, 0.35);
  border-top-color: #04101f;
  border-radius: 50%;
  animation: spin-fast 0.7s linear infinite;
}
.hint {
  margin: 24px 0 0;
  text-align: center;
  font-size: 12px;
  color: var(--sb-faint);
}

/* ── 动效：克制、慢 ── */
.reveal {
  animation: reveal 0.7s var(--sb-ease-emphasized) both;
  animation-delay: var(--d, 0s);
}
@keyframes reveal {
  from { opacity: 0; transform: translateY(14px); }
  to { opacity: 1; transform: none; }
}
@keyframes breathe {
  0%, 100% { opacity: 0.55; }
  50% { opacity: 1; }
}
@keyframes spin-fast {
  to { transform: rotate(360deg); }
}

@media (max-width: 560px) {
  .hud.tr, .hud.br { display: none; }
  .panel { padding: 44px 32px 34px; }
}

@media (prefers-reduced-motion: reduce) {
  .reveal, .logo-mark, .live, .mote, .spinner { animation: none; }
  .reveal { opacity: 1; transform: none; }
}
</style>
