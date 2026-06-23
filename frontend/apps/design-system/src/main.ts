import { initTheme } from '@nerv-iip/ui'
import { createApp } from 'vue'
import App from './App.vue'
import './assets/main.css'
import { router } from './router'

// Apply persisted colour mode + dynamic accent before first paint.
initTheme()

createApp(App).use(router).mount('#app')
