<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { PaletteIcon } from 'lucide-vue-next'
import { Button } from '../../ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../../ui/dropdown-menu'
import { useThemeAccent } from '../../../composables/useTheme'

const props = defineProps<{ class?: HTMLAttributes['class'] }>()

const { accent, setAccent, reset, presets } = useThemeAccent()
const entries = Object.entries(presets) as [string, string][]
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label="选择主题色"
        title="选择主题色"
        :class="props.class"
      >
        <PaletteIcon class="size-4" aria-hidden="true" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-56">
      <DropdownMenuLabel>主题色</DropdownMenuLabel>
      <div class="grid grid-cols-6 gap-2 p-2">
        <button
          v-for="[name, value] in entries"
          :key="name"
          type="button"
          class="size-6 rounded-full border border-border outline-none ring-offset-2 ring-offset-popover transition-transform hover:scale-110 focus-visible:ring-2 focus-visible:ring-ring"
          :class="accent === value ? 'ring-2 ring-ring' : ''"
          :style="{ backgroundColor: value }"
          :aria-label="`主题色 ${name}`"
          :aria-pressed="accent === value"
          @click="setAccent(value)"
        />
      </div>
      <DropdownMenuSeparator />
      <div class="p-1">
        <Button type="button" variant="ghost" size="sm" class="w-full justify-start" @click="reset">
          恢复默认
        </Button>
      </div>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
