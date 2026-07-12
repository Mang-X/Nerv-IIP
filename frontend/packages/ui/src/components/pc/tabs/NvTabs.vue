<script setup lang="ts">
import type { TabsRootEmits, TabsRootProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { TabsRoot, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — tabs root. A real wrapper (not a bare reka re-export) so it keeps the
 * flex layout + `group/tabs` orientation hook that `NvTabsList` / `NvTabsContent`
 * rely on, matching base `Tabs`.
 */
const props = defineProps<TabsRootProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<TabsRootEmits>()

const delegatedProps = reactiveOmit(props, 'class')
const forwarded = useForwardPropsEmits(delegatedProps, emits)
</script>

<template>
  <TabsRoot
    v-slot="slotProps"
    data-slot="nv-tabs"
    :data-orientation="forwarded.orientation || 'horizontal'"
    v-bind="forwarded"
    :class="cn('group/tabs flex gap-2 data-horizontal:flex-col', props.class)"
  >
    <slot v-bind="slotProps" />
  </TabsRoot>
</template>
