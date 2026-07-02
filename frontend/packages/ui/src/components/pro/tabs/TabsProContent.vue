<script setup lang="ts">
import type { TabsContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { TabsContent } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — tabs panel. A real wrapper (not a bare reka re-export) so it keeps
 * `flex-1 outline-none` + the text size, matching base `TabsContent`.
 */
const props = defineProps<TabsContentProps & { class?: HTMLAttributes['class'] }>()

const delegatedProps = reactiveOmit(props, 'class')
</script>

<template>
  <TabsContent
    data-slot="tabs-pro-content"
    :class="cn('flex-1 text-sm outline-none', props.class)"
    v-bind="delegatedProps"
  >
    <slot />
  </TabsContent>
</template>
