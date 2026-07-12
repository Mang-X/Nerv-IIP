<script setup lang="ts">
// Scene-availability badge (MAN-437 / #791). Rendered once at the top of every
// component page (injected via Layout `#doc-before` for doc-layout surfaces and
// via MobileDoc for the PDA pages), it answers two questions for any component:
//   1. 场景归属 — which surface layer owns this component (highlighted chip);
//   2. 其他场景对应件 — the counterpart on each other surface, linked, or marked
//      unavailable when the component is surface-exclusive.
// Data comes from `scene-map.ts`; the current page is auto-detected from the route
// so no per-page markup is needed.
import { computed } from 'vue'
import { useData, withBase } from 'vitepress'
import { MonitorIcon, PresentationIcon, SmartphoneIcon, TabletIcon } from 'lucide-vue-next'
import { resolveScene, SURFACE_META, SURFACES, type Surface } from './scene-map'

const { page } = useData()

const ICONS: Record<Surface, unknown> = {
  desktop: MonitorIcon,
  mobile: SmartphoneIcon,
  touch: TabletIcon,
  screen: PresentationIcon,
}

const scene = computed(() => resolveScene(page.value.relativePath))

const chips = computed(() => {
  const s = scene.value
  if (!s) return []
  return SURFACES.map((surface) => {
    const meta = SURFACE_META[surface]
    const member = s.family?.[surface]
    const isCurrent = surface === s.surface
    const available = isCurrent || !!member
    return {
      surface,
      label: meta.label,
      icon: ICONS[surface],
      isCurrent,
      available,
      name: member?.name,
      href: !isCurrent && member ? withBase(`/components/${meta.seg}/${member.slug}`) : undefined,
    }
  })
})

const ownerPkg = computed(() => (scene.value ? SURFACE_META[scene.value.surface].pkg : ''))
const exclusive = computed(() => scene.value && !scene.value.family)
</script>

<template>
  <div v-if="scene" class="nv-scene vp-raw" role="note" aria-label="场景可用性">
    <span class="nv-scene-lead">场景可用性</span>
    <div class="nv-scene-chips">
      <template v-for="c in chips" :key="c.surface">
        <a
          v-if="c.href"
          class="nv-scene-chip is-link"
          :href="c.href"
          :title="c.name ? `${c.label}：${c.name}` : c.label"
        >
          <component :is="c.icon" class="nv-scene-ic" aria-hidden="true" />
          <span>{{ c.label }}</span>
        </a>
        <span
          v-else
          class="nv-scene-chip"
          :class="{ 'is-current': c.isCurrent, 'is-off': !c.available }"
          :aria-current="c.isCurrent ? 'page' : undefined"
          :title="
            c.isCurrent
              ? `${c.label}：本页所属表面`
              : c.available
                ? c.label
                : `${c.label}：暂无对应件`
          "
        >
          <component :is="c.icon" class="nv-scene-ic" aria-hidden="true" />
          <span>{{ c.label }}</span>
          <span v-if="!c.available" class="nv-scene-dash" aria-hidden="true">—</span>
        </span>
      </template>
    </div>
    <span class="nv-scene-owner">
      {{ exclusive ? '本表面专属' : '归属' }} · <code>{{ ownerPkg }}</code>
    </span>
  </div>
</template>

<style>
.nv-scene {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem 0.75rem;
  margin: 0 0 1.25rem;
  padding: 0.625rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 10px;
  background: color-mix(in oklch, var(--muted) 40%, transparent);
  font-size: 0.8125rem;
  line-height: 1.2;
}
.nv-scene-lead {
  font-weight: 600;
  color: var(--muted-foreground);
}
.nv-scene-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.375rem;
}
.nv-scene-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.25rem 0.55rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--card);
  color: var(--muted-foreground);
  text-decoration: none;
  font-weight: 500;
  white-space: nowrap;
}
.nv-scene-ic {
  width: 0.9rem;
  height: 0.9rem;
}
/* Mix the brand accent with the neutral surfaces in oklab, NOT oklch: mixing a
   blue (hue 256°) with an achromatic --border/--card in oklch interpolates the
   hue on the shortest arc toward 0°, which passes through purple (~300°) and
   tints the chip magenta. oklab is rectangular (no hue channel), so the mix just
   desaturates toward the neutral while keeping the true blue hue. */
.nv-scene-chip.is-current {
  border-color: color-mix(in oklab, var(--nv-brand) 55%, var(--border));
  background: color-mix(in oklab, var(--nv-brand) 14%, var(--card));
  color: var(--nv-brand-strong);
  font-weight: 600;
}
.nv-scene-chip.is-link {
  color: var(--foreground);
  transition:
    border-color 0.15s var(--nv-ease-out-quart, ease-out),
    color 0.15s var(--nv-ease-out-quart, ease-out);
}
.nv-scene-chip.is-link:hover {
  border-color: color-mix(in oklab, var(--nv-brand) 45%, var(--border));
  color: var(--nv-brand-strong);
}
.nv-scene-chip.is-off {
  opacity: 0.55;
}
.nv-scene-dash {
  margin-inline-start: 0.1rem;
  font-variant-numeric: tabular-nums;
}
.nv-scene-owner {
  color: var(--muted-foreground);
}
.nv-scene-owner code {
  font-size: 0.75rem;
  color: var(--foreground);
}
@media (max-width: 640px) {
  .nv-scene-owner {
    flex-basis: 100%;
  }
}
</style>
