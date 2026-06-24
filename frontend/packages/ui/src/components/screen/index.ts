/**
 * Screen — big-board (control-room) component layer. Independent dark
 * "industrial-blue" surface (its own `--sb-*` tokens, no light mode), decoupled
 * from the PC/mobile layers and following only the shared design philosophy.
 * Importing any screen component pulls in the token sheet via this barrel.
 */
import './tokens.css'

// Containers / surfaces
export { default as ScreenPanel } from './ScreenPanel.vue'

// Indicators & charts
export { default as OeeHero } from './OeeHero.vue'
export { default as RingGauge } from './RingGauge.vue'
export { default as WaterLevel } from './WaterLevel.vue'
export { default as CapsuleBar } from './CapsuleBar.vue'
export { default as DigitalFlop } from './DigitalFlop.vue'
export { default as Sparkline } from './Sparkline.vue'
export { default as TrendChart } from './TrendChart.vue'
export { default as TaktGantt } from './TaktGantt.vue'

// Data & status
export { default as StatusCard } from './StatusCard.vue'
export { default as KpiBar } from './KpiBar.vue'
export { default as AlarmTable } from './AlarmTable.vue'
