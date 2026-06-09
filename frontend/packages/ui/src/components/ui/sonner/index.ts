// vue-sonner v2 不再自动注入样式，必须显式引入，否则 toast 渲染出来但无样式=不可见。
// 与 Toaster 导出同处，确保任何使用 Toaster 的 app 都拿到样式（不改 shadcn 原版 Sonner.vue）。
import 'vue-sonner/style.css'

export { default as Toaster } from './Sonner.vue'
