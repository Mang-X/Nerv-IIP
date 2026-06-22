<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import BottomSheet from '../bottom-sheet/BottomSheet.vue'
import MobileButton from '../button/MobileButton.vue'

/**
 * Mobile DatePicker — year / month / day wheel (Vant / tdesign-mobile style),
 * hosted in a BottomSheet. CSS scroll-snap columns + centre band; Cancel /
 * Confirm commit. v-model:open controls visibility, v-model the 'YYYY-MM-DD'.
 */
const props = withDefaults(defineProps<{ title?: string; minYear?: number; maxYear?: number }>(), {
  title: '选择日期',
})
const open = defineModel<boolean>('open', { default: false })
const model = defineModel<string>()

const ITEM = 40
const now = new Date()
const minY = computed(() => props.minYear ?? now.getFullYear() - 10)
const maxY = computed(() => props.maxYear ?? now.getFullYear() + 5)

const draft = ref({ y: now.getFullYear(), m: now.getMonth() + 1, d: now.getDate() })

const years = computed(() =>
  Array.from({ length: maxY.value - minY.value + 1 }, (_, i) => minY.value + i),
)
const months = Array.from({ length: 12 }, (_, i) => i + 1)
const days = computed(() => {
  const n = new Date(draft.value.y, draft.value.m, 0).getDate()
  return Array.from({ length: n }, (_, i) => i + 1)
})

const cols = computed(() => [
  { key: 'y' as const, items: years.value, unit: '年' },
  { key: 'm' as const, items: months, unit: '月' },
  { key: 'd' as const, items: days.value, unit: '日' },
])

const scrollers = ref<Record<string, HTMLElement>>({})
function setScroller(key: string, el: unknown) {
  if (el) scrollers.value[key] = el as HTMLElement
}
function onScroll(key: 'y' | 'm' | 'd', items: number[]) {
  const top = scrollers.value[key]?.scrollTop ?? 0
  const idx = Math.max(0, Math.min(items.length - 1, Math.round(top / ITEM)))
  draft.value = { ...draft.value, [key]: items[idx] }
}
function scrollTo(key: 'y' | 'm' | 'd', items: number[], value: number) {
  const idx = Math.max(0, items.indexOf(value))
  scrollers.value[key]?.scrollTo({ top: idx * ITEM })
}
function pad(n: number) {
  return String(n).padStart(2, '0')
}
function confirm() {
  const { y, m, d } = draft.value
  model.value = `${y}-${pad(m)}-${pad(d)}`
  open.value = false
}

watch(open, (isOpen) => {
  if (!isOpen) return
  const v = (model.value ?? '').match(/^(\d{4})-(\d{2})-(\d{2})$/)
  draft.value = v
    ? { y: +v[1], m: +v[2], d: +v[3] }
    : { y: now.getFullYear(), m: now.getMonth() + 1, d: now.getDate() }
  nextTick(() => {
    scrollTo('y', years.value, draft.value.y)
    scrollTo('m', months, draft.value.m)
    scrollTo('d', days.value, draft.value.d)
  })
})
</script>

<template>
  <BottomSheet :open="open" @update:open="open = $event">
    <div class="ds-mdp">
      <div class="flex items-center justify-between pb-1">
        <MobileButton variant="text" size="md" class="text-muted-foreground" @click="open = false"
          >取消</MobileButton
        >
        <span class="text-[15px] font-medium">{{ title }}</span>
        <MobileButton variant="text" size="md" @click="confirm">确定</MobileButton>
      </div>
      <div class="ds-mdp-wheel">
        <div class="ds-mdp-band" aria-hidden="true" />
        <div class="flex h-full">
          <div
            v-for="col in cols"
            :key="col.key"
            :ref="(el) => setScroller(col.key, el)"
            class="ds-mdp-scroll"
            @scroll="onScroll(col.key, col.items)"
          >
            <div class="ds-mdp-pad" />
            <div
              v-for="it in col.items"
              :key="it"
              :class="[
                'ds-mdp-item',
                draft[col.key] === it ? 'font-medium text-foreground' : 'text-muted-foreground',
              ]"
            >
              {{ it }}{{ col.unit }}
            </div>
            <div class="ds-mdp-pad" />
          </div>
        </div>
      </div>
    </div>
  </BottomSheet>
</template>

<style scoped>
.ds-mdp-wheel {
  position: relative;
  height: 200px;
}
.ds-mdp-band {
  position: absolute;
  top: 80px;
  right: 0;
  left: 0;
  height: 40px;
  border-top: 1px solid var(--border);
  border-bottom: 1px solid var(--border);
  pointer-events: none;
}
.ds-mdp-scroll {
  height: 100%;
  flex: 1;
  overflow-y: auto;
  scroll-snap-type: y mandatory;
  scrollbar-width: none;
}
.ds-mdp-scroll::-webkit-scrollbar {
  display: none;
}
.ds-mdp-pad {
  height: 80px;
}
.ds-mdp-item {
  display: flex;
  height: 40px;
  align-items: center;
  justify-content: center;
  scroll-snap-align: center;
  font-size: 1rem;
  font-variant-numeric: tabular-nums;
  transition: color 0.15s ease;
}
</style>
