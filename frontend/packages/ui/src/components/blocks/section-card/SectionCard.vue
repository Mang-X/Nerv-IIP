<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { MinusIcon, TrendingDownIcon, TrendingUpIcon } from 'lucide-vue-next'
import {
  Card,
  CardAction,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '../../ui/card'
import { Badge } from '../../ui/badge'
import { cn } from '../../../lib/utils'

export type TrendDirection = 'up' | 'down' | 'flat'

const props = defineProps<{
  /** Small label above the metric. */
  description: string
  /** The headline metric (rendered tabular-nums). */
  value: string | number
  /** Optional trend pill (e.g. '+12.5%'). */
  trend?: { value: string, direction?: TrendDirection }
  /** Optional emphasized footnote line. */
  footnote?: string
  /** Optional secondary (muted) footnote line. */
  hint?: string
  class?: HTMLAttributes['class']
}>()

const direction = computed<TrendDirection>(() => props.trend?.direction ?? 'flat')
const trendIcon = computed(() =>
  direction.value === 'up' ? TrendingUpIcon : direction.value === 'down' ? TrendingDownIcon : MinusIcon,
)
const trendClass = computed(() =>
  direction.value === 'up'
    ? 'border-success/30 bg-success/10 text-success'
    : direction.value === 'down'
      ? 'border-destructive/30 bg-destructive/10 text-destructive'
      : 'border-border bg-muted text-muted-foreground',
)
</script>

<template>
  <Card :class="cn('@container/card gap-3 bg-gradient-to-t from-primary/5 to-card', props.class)">
    <CardHeader>
      <CardDescription>{{ description }}</CardDescription>
      <CardTitle class="text-2xl font-semibold tabular-nums @[200px]/card:text-3xl">
        {{ value }}
      </CardTitle>
      <CardAction v-if="trend">
        <Badge variant="outline" :class="cn('gap-1 rounded-sm', trendClass)">
          <component :is="trendIcon" class="size-3" aria-hidden="true" />
          {{ trend.value }}
        </Badge>
      </CardAction>
    </CardHeader>
    <CardFooter v-if="footnote || hint || $slots.default" class="flex-col items-start gap-1 text-sm">
      <slot>
        <div v-if="footnote" class="line-clamp-1 font-medium text-foreground">{{ footnote }}</div>
        <div v-if="hint" class="text-muted-foreground">{{ hint }}</div>
      </slot>
    </CardFooter>
  </Card>
</template>
