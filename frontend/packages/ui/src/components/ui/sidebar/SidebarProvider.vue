<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted, getCurrentInstance } from 'vue'
import { TooltipProvider } from '../tooltip'
import { provideSidebar } from './utils'
import { cn } from '../../../lib/utils'

const props = withDefaults(defineProps<{
  defaultOpen?: boolean
  open?: boolean
  storageKey?: string
  class?: string
}>(), {
  defaultOpen: true,
  storageKey: 'sidebar_state',
})

const emit = defineEmits<{
  'update:open': [value: boolean]
}>()

const isMobile = ref(false)

function checkMobile() {
  isMobile.value = window.innerWidth < 768
}

function persistState(val: boolean) {
  try { localStorage.setItem(props.storageKey, String(val)) } catch { /* noop */ }
}

const isControlled = Object.keys(getCurrentInstance()!.vnode.props ?? {}).some(
  k => k === 'open' || k === 'onUpdate:open',
)

function getStoredState(initial: boolean): boolean {
  if (isControlled) return props.open!
  try {
    const val = localStorage.getItem(props.storageKey)
    if (val === 'true') return true
    if (val === 'false') return false
    return initial
  }
  catch {
    return initial
  }
}

const _open = ref(getStoredState(props.defaultOpen))

const open = computed({
  get: () => isControlled ? (props.open ?? _open.value) : _open.value,
  set: (val) => {
    _open.value = val
    emit('update:open', val)
    persistState(val)
  },
})

const state = computed(() => open.value ? 'expanded' : 'collapsed')

function setOpen(value: boolean) {
  open.value = value
}

function toggleSidebar() {
  open.value = !open.value
}

onMounted(() => {
  checkMobile()
  window.addEventListener('resize', checkMobile)
})

onUnmounted(() => {
  window.removeEventListener('resize', checkMobile)
})

provideSidebar({ state, open, setOpen, toggleSidebar, isMobile })
</script>

<template>
  <div
    :data-slot="'sidebar-wrapper'"
    :data-state="state"
    :class="cn('group/sidebar-wrapper flex min-h-svh w-full has-data-[variant=inset]:bg-sidebar', props.class)"
    :style="{
      '--sidebar-width': '16rem',
      '--sidebar-width-icon': '3rem',
    }"
  >
    <TooltipProvider :delay-duration="0">
      <slot />
    </TooltipProvider>
  </div>
</template>
