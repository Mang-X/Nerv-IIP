<script setup lang="ts">
/**
 * Screen — flowing divider. A horizontal gradient hairline with a bright cyan
 * dot at center; an optional sheen travels along the line (off under
 * reduced-motion). A quiet separator between board sections — restrained glow,
 * single layer. Zero required props.
 */
withDefaults(
  defineProps<{
    /** Animate a light sheen sliding along the line. */
    flow?: boolean
  }>(),
  { flow: true },
)
</script>

<template>
  <div class="sb-gd" :class="{ flow }">
    <span class="sb-gd-dot" />
  </div>
</template>

<style scoped>
.sb-gd {
  position: relative;
  height: 1px;
  width: 100%;
  background: linear-gradient(
    90deg,
    transparent,
    var(--sb-line-2) 25%,
    var(--sb-cyan) 50%,
    var(--sb-line-2) 75%,
    transparent
  );
}
/* center glow dot */
.sb-gd-dot {
  position: absolute;
  top: 50%;
  left: 50%;
  width: 5px;
  height: 5px;
  border-radius: 50%;
  transform: translate(-50%, -50%);
  background: var(--sb-cyan);
  box-shadow: var(--sb-glow);
}
/* travelling sheen */
.sb-gd.flow::after {
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  height: 100%;
  width: 22%;
  background: linear-gradient(90deg, transparent, var(--sb-cyan), transparent);
  opacity: 0.7;
  animation: sb-gd-slide 3.4s ease-in-out infinite;
}
@keyframes sb-gd-slide {
  0% {
    transform: translateX(-30%);
    opacity: 0;
  }
  20% {
    opacity: 0.7;
  }
  80% {
    opacity: 0.7;
  }
  100% {
    transform: translateX(480%);
    opacity: 0;
  }
}
@media (prefers-reduced-motion: reduce) {
  .sb-gd.flow::after {
    animation: none;
    display: none;
  }
}
</style>
