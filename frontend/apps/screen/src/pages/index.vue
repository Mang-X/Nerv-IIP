<script setup lang="ts">
import * as icons from 'lucide-vue-next'
import { ScreenPanel } from '@nerv-iip/ui'
import { type Component, computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useAccessScope } from '@/access/useAccessScope'
import { SCREENS } from '@/data/screens'

const scope = useAccessScope()
const cards = computed(() => SCREENS.filter((s) => scope.canSeeScreen(s.key)))
// lucide 图标按名取用；显式转 Component 以过 vue-tsc 的 <component :is> 类型校验。
const iconMap = icons as unknown as Record<string, Component>
function iconOf(name: string): Component {
  return iconMap[name] ?? iconMap.SquareDashed
}
</script>

<template>
  <div class="launcher">
    <header class="launcher__top">
      <div class="brand">
        <span class="brand__title">Nerv-IIP 工业数据大屏</span>
        <span class="brand__sub">生产指挥中心</span>
      </div>
      <div v-if="scope.factories.length > 1" class="factory-switch">
        <button
          v-for="f in scope.factories"
          :key="f.id"
          type="button"
          :class="['factory-switch__btn', { active: f.id === scope.currentFactoryId }]"
          @click="scope.switchFactory(f.id)"
        >
          {{ f.name }}
        </button>
      </div>
    </header>

    <main class="launcher__grid">
      <RouterLink v-for="s in cards" :key="s.key" :to="s.route" class="card-link">
        <ScreenPanel :accent="s.accent" class="card">
          <component :is="iconOf(s.icon)" class="card__icon" :size="46" :stroke-width="1.4" />
          <div class="card__title">{{ s.title }}</div>
          <div class="card__desc">{{ s.desc }}</div>
        </ScreenPanel>
      </RouterLink>
      <p v-if="cards.length === 0" class="empty">当前账号无可见大屏</p>
    </main>
  </div>
</template>

<style scoped>
.launcher {
  min-height: 100vh;
  background: var(--sb-bg);
  color: var(--sb-text);
  padding: 48px 64px;
  display: flex;
  flex-direction: column;
  gap: 40px;
}
.launcher__top {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.brand__title {
  font-size: 30px;
  font-weight: 600;
  letter-spacing: 0.06em;
}
.brand__sub {
  margin-left: 14px;
  color: var(--sb-muted);
}
.factory-switch {
  display: flex;
  gap: 8px;
}
.factory-switch__btn {
  padding: 8px 18px;
  border: 1px solid var(--sb-line-2);
  border-radius: var(--sb-radius);
  background: transparent;
  color: var(--sb-muted);
  cursor: pointer;
  transition: color 0.18s var(--sb-ease), border-color 0.18s var(--sb-ease);
}
.factory-switch__btn.active {
  color: var(--sb-cyan);
  border-color: var(--sb-cyan-dim);
}
.factory-switch__btn:active {
  transform: scale(0.985);
}
.launcher__grid {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 28px;
  align-content: start;
}
.card-link {
  text-decoration: none;
}
.card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 40px 32px;
  min-height: 220px;
  cursor: pointer;
  transition: transform 0.2s var(--sb-ease-emphasized);
}
.card-link:hover .card {
  transform: translateY(-4px);
}
.card__icon {
  color: var(--sb-cyan);
}
.card__title {
  font-size: 24px;
  font-weight: 600;
}
.card__desc {
  color: var(--sb-muted);
  font-size: 15px;
}
.empty {
  color: var(--sb-faint);
}
@media (prefers-reduced-motion: reduce) {
  .card,
  .factory-switch__btn {
    transition: none;
  }
  .card-link:hover .card {
    transform: none;
  }
}
</style>
