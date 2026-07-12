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
    <div class="nv-m-picker">
      <div class="flex items-center justify-between pb-1">
        <MobileButton variant="text" size="md" class="text-muted-foreground" @click="open = false">
          取消
        </MobileButton>
        <span class="text-[15px] font-medium">{{ title }}</span>
        <MobileButton variant="text" size="md" @click="confirm">确定</MobileButton>
      </div>

      <div class="nv-m-picker-wheel">
        <div class="nv-m-picker-band" aria-hidden="true" />
        <div ref="scroller" class="nv-m-picker-scroll" @scroll="onScroll">
          <div class="nv-m-picker-pad" />
          <div
            v-for="(opt, i) in options"
            :key="opt.value"
            :class="[
              'nv-m-picker-item',
              i === draftIndex ? 'font-medium text-foreground' : 'text-muted-foreground',
            ]"
          >
            {{ opt.label }}
          </div>
          <div class="nv-m-picker-pad" />
        </div>
      </div>
    </div>
  </BottomSheet>
</template>

<style scoped>
@layer nv-components {
  .nv-m-picker-wheel {
    position: relative;
    height: 200px;
  }
  .nv-m-picker-band {
    position: absolute;
    top: 80px;
    right: 0;
    left: 0;
    height: 40px;
    border-top: 1px solid var(--border);
    border-bottom: 1px solid var(--border);
    pointer-events: none;
  }
  .nv-m-picker-scroll {
    height: 100%;
    overflow-y: auto;
    scroll-snap-type: y mandatory;
    scrollbar-width: none;
  }
  .nv-m-picker-scroll::-webkit-scrollbar {
    display: none;
  }
  .nv-m-picker-pad {
    height: 80px;
  }
  .nv-m-picker-item {
    display: flex;
    height: 40px;
    align-items: center;
    justify-content: center;
    scroll-snap-align: center;
    font-size: 1rem;
    transition: color 0.15s ease;
  }
}
</style>
