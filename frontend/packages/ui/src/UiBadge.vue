<script setup lang="ts">
withDefaults(defineProps<{
  tone?: 'neutral' | 'success' | 'warning' | 'danger'
}>(), {
  tone: 'neutral'
})
</script>

<template>
  <span class="ui-badge" :data-tone="tone">
    <slot />
  </span>
</template>

<style scoped>
.ui-badge {
  align-items: center;
  border: 1px solid transparent;
  border-radius: 999px;
  display: inline-flex;
  font-size: 0.75rem;
  font-weight: 700;
  gap: 0.35rem;
  letter-spacing: 0;
  line-height: 1;
  padding: 0.3rem 0.55rem;
  white-space: nowrap;
}

.ui-badge[data-tone='neutral'] {
  background: #eef2f7;
  border-color: #d7dee9;
  color: #334155;
}

.ui-badge[data-tone='success'] {
  background: #dcfce7;
  border-color: #bbf7d0;
  color: #166534;
}

.ui-badge[data-tone='warning'] {
  background: #fef3c7;
  border-color: #fde68a;
  color: #92400e;
}

.ui-badge[data-tone='danger'] {
  background: #fee2e2;
  border-color: #fecaca;
  color: #991b1b;
}
</style>
