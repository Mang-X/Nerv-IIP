/**
 * Screen — big-board (control-room) component layer. Independent dark
 * "industrial-blue" surface (its own `--sb-*` tokens, no light mode), decoupled
 * from the PC/mobile layers and following only the shared design philosophy.
 * Importing any screen component pulls in the token sheet via this barrel.
 */
import './tokens.css'

// Containers / surfaces
export { default as ScreenPanel } from './ScreenPanel.vue'
export { default as ScreenScrollArea } from './ScreenScrollArea.vue'

// Big-board infrastructure（原 apps/screen screen-kit 上提，MAN-321）：
// 舞台缩放 / 无缝自动滚动 / 大屏取数（轮询 + 隐藏暂停 + 失败保活）
export { default as ScreenScaler } from './ScreenScaler.vue'
export { default as ScrollBoard } from './ScrollBoard.vue'
export * from './scale'
export * from './useScreenData'

// Indicators & charts
export { default as OeeHero } from './OeeHero.vue'
export { default as RingGauge } from './RingGauge.vue'
export { default as CapsuleBar } from './CapsuleBar.vue'
export { default as DigitalFlop } from './DigitalFlop.vue'
export { default as Sparkline } from './Sparkline.vue'
export { default as TrendChart } from './TrendChart.vue'
export { default as ScreenBarChart } from './ScreenBarChart.vue'
export { default as ScreenDonut } from './ScreenDonut.vue'
export { default as ScreenPareto } from './ScreenPareto.vue'
export { default as TaktGantt } from './TaktGantt.vue'

// Data & status
export { default as StatusCard } from './StatusCard.vue'
export { default as KpiBar } from './KpiBar.vue'
export { default as AlarmTable } from './AlarmTable.vue'

// Decoration / chrome
export { default as ScreenHeader } from './ScreenHeader.vue'
export { default as TechFrame } from './TechFrame.vue'
export { default as BorderPanel } from './BorderPanel.vue'
export { default as TitleBar } from './TitleBar.vue'
export { default as GlowDivider } from './GlowDivider.vue'
export { default as StatusLight } from './StatusLight.vue'
export { default as StatusTag } from './StatusTag.vue'

// Controls (big-screen)
export { default as ScreenButton } from './ScreenButton.vue'
export { default as ScreenTable } from './ScreenTable.vue'
export { default as ScreenSelect } from './ScreenSelect.vue'
export { default as ScreenSearch } from './ScreenSearch.vue'
export { default as ScreenInput } from './ScreenInput.vue'
export { default as ScreenTabs } from './ScreenTabs.vue'
export { default as ScreenSegmented } from './ScreenSegmented.vue'
export { default as ScreenSwitch } from './ScreenSwitch.vue'
export { default as ScreenPagination } from './ScreenPagination.vue'
