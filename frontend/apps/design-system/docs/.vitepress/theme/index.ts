import type { Theme } from 'vitepress'
import DefaultTheme from 'vitepress/theme'
import Demo from './Demo.vue'
import Layout from './Layout.vue'
import MobileDoc from './MobileDoc.vue'
import ScreenDemo from './ScreenDemo.vue'
import ScreenGallery from './screen/ScreenGallery.vue'
import './style.css'
// DHTMLX 自身的布局/网格/时间轴 CSS(排产组件渲染必需,否则只有 DOM 无布局)。经 config.mts
// 条件 alias 指向本地 vendor 或空 stub(无试用包时组件显示占位,import 解析为空、不报错)。
// 排产皮肤 scheduling.css 由组件自引,此处只补 DHTMLX 基础布局。
import '@dhx/trial-gantt/codebase/dhtmlxgantt.css'

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
    app.component('ScreenDemo', ScreenDemo)
    app.component('ScreenGallery', ScreenGallery)
  },
} satisfies Theme
