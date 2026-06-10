import { initTheme } from '@nerv-iip/ui'
import { createApp } from 'vue'
import SchedulingPreview from './dev/SchedulingPreview.vue'
import './assets/main.css'

// 开发预览入口(非产品):scheduling-preview.html → 此文件。不接路由/认证/后端。
initTheme()
createApp(SchedulingPreview).mount('#app')
