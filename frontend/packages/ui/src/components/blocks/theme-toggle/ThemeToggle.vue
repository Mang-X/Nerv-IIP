<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { MoonIcon, SunIcon } from '@lucide/vue'
import { Button } from '../../ui/button'
import { useColorMode } from '../../../composables/useTheme'

const props = defineProps<{ class?: HTMLAttributes['class'] }>()

const { isDark, toggle } = useColorMode()
const label = computed(() => (isDark.value ? '切换到亮色主题' : '切换到暗色主题'))
</script>

<template>
  <Button
    type="button"
    variant="ghost"
    size="icon"
    :aria-label="label"
    :title="label"
    :class="props.class"
    @click="toggle"
  >
    <SunIcon v-if="isDark" class="size-4" aria-hidden="true" />
    <MoonIcon v-else class="size-4" aria-hidden="true" />
  </Button>
</template>
