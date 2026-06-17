import type { Theme } from 'vitepress'
import DefaultTheme from 'vitepress/theme'
import Demo from './Demo.vue'
import Layout from './Layout.vue'
import MobileDoc from './MobileDoc.vue'
import './style.css'

// Nerv-IIP docs theme — extends VitePress's default, re-skinned via style.css
// (the `--vp-*` → design-token mapping) and wrapped by Layout.vue for the accent
// picker. `<Demo>` is the global live-preview wrapper used by component pages.
export default {
  extends: DefaultTheme,
  Layout,
  enhanceApp({ app }) {
    app.component('Demo', Demo)
    app.component('MobileDoc', MobileDoc)
  },
} satisfies Theme
