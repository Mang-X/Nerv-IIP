<script setup lang="ts">
import BottomSheet from '../bottom-sheet/BottomSheet.vue'
import MobileButton from '../button/MobileButton.vue'

/**
 * Mobile ActionSheet — a bottom action list (Vant / tdesign-mobile style) built
 * on BottomSheet: stacked actions + a separated Cancel. Emits the picked value.
 */
export interface ActionItem {
  label: string
  value: string
  danger?: boolean
}

withDefaults(
  defineProps<{
    actions: ActionItem[]
    title?: string
    description?: string
    cancelText?: string
  }>(),
  { cancelText: '取消' },
)
const emit = defineEmits<{ select: [value: string] }>()
const open = defineModel<boolean>('open', { default: false })

function pick(action: ActionItem) {
  emit('select', action.value)
  open.value = false
}
</script>

<template>
  <BottomSheet :open="open" :title="title" :description="description" @update:open="open = $event">
    <div class="space-y-2 pb-1">
      <MobileButton
        v-for="action in actions"
        :key="action.value"
        :variant="action.danger ? 'danger' : 'default'"
        size="lg"
        block
        @click="pick(action)"
      >
        {{ action.label }}
      </MobileButton>
      <MobileButton
        variant="text"
        size="lg"
        block
        class="mt-1 text-foreground"
        @click="open = false"
      >
        {{ cancelText }}
      </MobileButton>
    </div>
  </BottomSheet>
</template>
