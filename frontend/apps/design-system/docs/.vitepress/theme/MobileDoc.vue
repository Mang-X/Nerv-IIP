<script setup lang="ts">
// Mobile component doc layout (Arco Design Mobile style): documentation prose +
// code on the LEFT, a sticky phone simulator on the RIGHT showing the live
// components. Use `layout: page` so the two columns have room.
//   <MobileDoc>
//     <template #phone> …live components… </template>
//     …markdown prose / code / props… (default slot)
//   </MobileDoc>
</script>

<template>
  <div class="ds-mdoc">
    <div class="ds-mdoc-main vp-doc">
      <slot />
    </div>
    <aside class="ds-mdoc-aside">
      <div class="ds-mdoc-sticky">
        <ClientOnly>
          <div class="ds-mdoc-phone">
            <div class="ds-mdoc-statusbar">
              <span class="font-semibold tabular-nums">9:41</span>
              <span class="ds-mdoc-notch" aria-hidden="true" />
              <span class="ds-mdoc-signal" aria-hidden="true" />
            </div>
            <div class="ds-mdoc-screen">
              <slot name="phone" />
            </div>
          </div>
        </ClientOnly>
      </div>
    </aside>
  </div>
</template>

<style>
.ds-mdoc {
  display: grid;
  gap: 2rem;
  align-items: start;
}
@media (min-width: 1024px) {
  .ds-mdoc {
    grid-template-columns: minmax(0, 1fr) 360px;
  }
}
.ds-mdoc-main {
  min-width: 0;
}
.ds-mdoc-sticky {
  position: static;
}
@media (min-width: 1024px) {
  .ds-mdoc-sticky {
    position: sticky;
    top: 5.5rem;
  }
}
.ds-mdoc-phone {
  width: 100%;
  max-width: 340px;
  margin-inline: auto;
  border: 1px solid var(--border);
  border-radius: 28px;
  background: var(--background);
  overflow: hidden;
  box-shadow:
    0 1px 0 0 color-mix(in oklch, white 6%, transparent) inset,
    0 20px 50px -20px color-mix(in oklch, black 60%, transparent);
}
.ds-mdoc-statusbar {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 2.25rem;
  padding-inline: 1.25rem;
  font-size: 0.75rem;
  color: var(--foreground);
}
.ds-mdoc-notch {
  position: absolute;
  left: 50%;
  top: 0.5rem;
  height: 1.1rem;
  width: 5rem;
  transform: translateX(-50%);
  border-radius: 9999px;
  background: color-mix(in oklch, var(--foreground) 88%, transparent);
}
.ds-mdoc-signal {
  width: 1.1rem;
  height: 0.6rem;
  border-radius: 2px;
  background: color-mix(in oklch, var(--foreground) 70%, transparent);
}
.ds-mdoc-screen {
  height: 560px;
  overflow-y: auto;
  background: var(--background);
  scrollbar-width: thin;
}
/* tidy default rhythm for stacked demos inside the phone */
.ds-mdoc-screen :where(section) {
  padding: 1rem;
}
.ds-mdoc-screen :where(section) + :where(section) {
  border-top: 1px solid var(--border);
}
.ds-mdoc-label {
  margin-bottom: 0.625rem;
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.02em;
  color: var(--muted-foreground);
}
</style>
