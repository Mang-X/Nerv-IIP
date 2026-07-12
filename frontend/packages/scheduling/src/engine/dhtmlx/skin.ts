import type { ThemeBinding } from '../engine'

// DHTMLX 自带皮肤与设计系统 v2 冲突。我们在容器加 scope class + 注入设计 token CSS 变量,
// 由 styles/scheduling.css 把 DHTMLX 关键视觉(条形/网格/今天线/选中)映射到 var(--nv-brand) 等。
export const DHX_SCOPE = 'nerv-dhx-scope'

export function applySkin(container: HTMLElement, theme: ThemeBinding): void {
  container.classList.add('nerv-gantt', 'nerv-gantt-dhx', DHX_SCOPE)
  container.classList.toggle('nerv-gantt-dark', theme.isDark)
  for (const [k, v] of Object.entries(theme.tokens)) {
    if (v) container.style.setProperty(k, v)
  }
}
