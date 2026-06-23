<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import BottomSheet from '../bottom-sheet/BottomSheet.vue'
import MobileButton from '../button/MobileButton.vue'

/**
 * Mobile Picker — single-column wheel selector (Vant / tdesign-mobile style),
 * hosted in a BottomSheet. CSS scroll-snap wheel with a centre highlight band;
 * Cancel / Confirm commit. v-model:open controls visibility, v-model the value.
 */
export interface PickerOption {
  label: string
  value: string
}

const props = withDefaults(defineProps<{ options: PickerOption[]; title?: string }>(), {
  title: '请选择',
})
const open = defineModel<boolean>('open', { default: false })
const model = defineModel<string>()

const ITEM = 40
const scroller = ref<HTMLElement>()
const draftIndex = ref(0)

function onScroll() {
  const top = scroller.value?.scrollTop ?? 0
  draftIndex.value = Math.max(0, Math.min(props.options.length - 1, Math.round(top / ITEM)))
}
function confirm() {
  const opt = props.options[draftIndex.value]
  if (opt) model.value = opt.value
  open.value = false
}

watch(open, (isOpen) => {
  if (!isOpen) return
  nextTick(() => {
    const i = Math.max(
      0,
      props.options.findIndex((o) => o.value === model.value),
    )
    draftIndex.value = i
    scroller.value?.scrollTo({ top: i * ITEM })
  })
})
</script>

<template>
  <BottomSheet :open="open" @update:open="open = $event">
    <div class="ds-picker">
      <div class="flex items-center justify-between pb-1">
        <MobileButton variant="text" size="md" class="text-muted-foreground" @click="open = false">
          取消
        </MobileButton>
        <span class="text-[15px] font-medium">{{ title }}</span>
        <MobileButton variant="text" size="md" @click="confirm">确定</MobileButton>
      </div>

      <div class="ds-picker-wheel">
        <div class="ds-picker-band" aria-hidden="true" />
        <div ref="scroller" class="ds-picker-scroll" @scroll="onScroll">
          <div class="ds-picker-pad" />
          <div
            v-for="(opt, i) in options"
            :key="opt.value"
            :class="[
              'ds-picker-item',
              i === draftIndex ? 'font-medium text-foreground' : 'text-muted-foreground',
            ]"
          >
            {{ opt.label }}
          </div>
          <div class="ds-picker-pad" />
        </div>
      </div>
    </div>
  </BottomSheet>
</template>

<style scoped>
.ds-picker-wheel {
  position: relative;
  height: 200px;
}
.ds-picker-band {
  position: absolute;
  top: 80px;
  right: 0;
  left: 0;
  height: 40px;
  border-top: 1px solid var(--border);
  border-bottom: 1px solid var(--border);
  pointer-events: none;
}
.ds-picker-scroll {
  height: 100%;
  overflow-y: auto;
  scroll-snap-type: y mandatory;
  scrollbar-width: none;
}
.ds-picker-scroll::-webkit-scrollbar {
  display: none;
}
.ds-picker-pad {
  height: 80px;
}
.ds-picker-item {
  display: flex;
  height: 40px;
  align-items: center;
  justify-content: center;
  scroll-snap-align: center;
  font-size: 1rem;
  transition: color 0.15s ease;
}
</style>
