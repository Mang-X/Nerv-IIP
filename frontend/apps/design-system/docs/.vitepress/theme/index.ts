import type { Theme } from 'vitepress'
import DefaultTheme from 'vitepress/theme'
// IMPORTANT: import style.css BEFORE the .vue components below. Its first line
// declares the cascade-layer order (`@layer theme, nv-tokens, base, components,
// nv-components;`), and Vite injects CSS in module-evaluation order. If a component
// SFC (`<style scoped> @layer nv-components {…}`) is evaluated first, `nv-components`
// registers ahead of `base`, so Tailwind Preflight's `*{ padding: 0 }` (layer base)
// beats every scoped-CSS component (NvMobileGrid / NvFab / bordered NvDescriptions /
// pagination / dialog collapse to zero padding). Importing style.css first pins the
// order. Do NOT move this below the component imports.
import './style.css'
import Demo from './Demo.vue'
import Layout from './Layout.vue'
import MobileDoc from './MobileDoc.vue'
import SceneBadge from './SceneBadge.vue'
import ScreenDemo from './ScreenDemo.vue'
import ScreenGallery from './screen/ScreenGallery.vue'

// Nerv-IIP docs theme — extends VitePress's default, re-skinned via style.css
// (the `--vp-*` → design-token mapping) and wrapped by Layout.vue for the accent
// picker. `<Demo>` is the global live-preview wrapper used by component pages;
// `<ScreenDemo>` is its dark industrial-blue counterpart for the screen layer.
export default {
  extends: DefaultTheme,
  Layout,
  enhanceApp({ app }) {
    app.component('Demo', Demo)
    app.component('MobileDoc', MobileDoc)
    app.component('SceneBadge', SceneBadge)
    app.component('ScreenDemo', ScreenDemo)
    app.component('ScreenGallery', ScreenGallery)
  },
} satisfies Theme
