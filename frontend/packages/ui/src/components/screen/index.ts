/**
 * Screen — big-board (control-room) component layer. Independent dark
 * "industrial-blue" surface (its own `--sb-*` tokens, no light mode), decoupled
 * from the PC/mobile layers and following only the shared design philosophy.
 * Importing any screen component pulls in the token sheet via this barrel.
 */
import './tokens.css'

// Containers / surfaces
export {
  default as NvScreenPanel,
  /** @deprecated Use `NvScreenPanel` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenPanel,
} from './ScreenPanel.vue'
export {
  default as NvScreenScrollArea,
  /** @deprecated Use `NvScreenScrollArea` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenScrollArea,
} from './ScreenScrollArea.vue'

// Big-board infrastructure（原 apps/screen screen-kit 上提，MAN-321）：
// 舞台缩放 / 无缝自动滚动 / 大屏取数（轮询 + 隐藏暂停 + 失败保活）
export {
  default as NvScreenScaler,
  /** @deprecated Use `NvScreenScaler` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenScaler,
} from './ScreenScaler.vue'
export {
  default as NvScrollBoard,
  /** @deprecated Use `NvScrollBoard` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScrollBoard,
} from './ScrollBoard.vue'
export * from './scale'
export * from './useScreenData'

// Indicators & charts
export {
  default as NvOeeHero,
  /** @deprecated Use `NvOeeHero` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as OeeHero,
} from './OeeHero.vue'
export {
  default as NvRingGauge,
  /** @deprecated Use `NvRingGauge` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as RingGauge,
} from './RingGauge.vue'
export {
  default as NvCapsuleBar,
  /** @deprecated Use `NvCapsuleBar` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as CapsuleBar,
} from './CapsuleBar.vue'
export {
  default as NvDigitalFlop,
  /** @deprecated Use `NvDigitalFlop` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as DigitalFlop,
} from './DigitalFlop.vue'
export {
  default as NvSparkline,
  /** @deprecated Use `NvSparkline` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as Sparkline,
} from './Sparkline.vue'
export {
  default as NvScreenTrendChart,
  /** @deprecated Use `NvScreenTrendChart` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as TrendChart,
} from './TrendChart.vue'
export {
  default as NvScreenBarChart,
  /** @deprecated Use `NvScreenBarChart` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenBarChart,
} from './ScreenBarChart.vue'
export {
  default as NvScreenDonut,
  /** @deprecated Use `NvScreenDonut` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenDonut,
} from './ScreenDonut.vue'
export {
  default as NvScreenPareto,
  /** @deprecated Use `NvScreenPareto` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenPareto,
} from './ScreenPareto.vue'
export {
  default as NvTaktGantt,
  /** @deprecated Use `NvTaktGantt` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as TaktGantt,
} from './TaktGantt.vue'

// Data & status
export {
  default as NvScreenStatusCard,
  /** @deprecated Use `NvScreenStatusCard` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as StatusCard,
} from './StatusCard.vue'
export {
  default as NvKpiBar,
  /** @deprecated Use `NvKpiBar` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as KpiBar,
} from './KpiBar.vue'
export {
  default as NvAlarmTable,
  /** @deprecated Use `NvAlarmTable` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as AlarmTable,
} from './AlarmTable.vue'

// Decoration / chrome
export {
  default as NvScreenHeader,
  /** @deprecated Use `NvScreenHeader` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenHeader,
} from './ScreenHeader.vue'
export {
  default as NvTechFrame,
  /** @deprecated Use `NvTechFrame` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as TechFrame,
} from './TechFrame.vue'
export {
  default as NvBorderPanel,
  /** @deprecated Use `NvBorderPanel` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as BorderPanel,
} from './BorderPanel.vue'
export {
  default as NvTitleBar,
  /** @deprecated Use `NvTitleBar` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as TitleBar,
} from './TitleBar.vue'
export {
  default as NvGlowDivider,
  /** @deprecated Use `NvGlowDivider` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as GlowDivider,
} from './GlowDivider.vue'
export {
  default as NvScreenStatusLight,
  /** @deprecated Use `NvScreenStatusLight` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as StatusLight,
} from './StatusLight.vue'
export {
  default as NvScreenStatusTag,
  /** @deprecated Use `NvScreenStatusTag` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as StatusTag,
} from './StatusTag.vue'

// Controls (big-screen)
export {
  default as NvScreenButton,
  /** @deprecated Use `NvScreenButton` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenButton,
} from './ScreenButton.vue'
export {
  default as NvScreenTable,
  /** @deprecated Use `NvScreenTable` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenTable,
} from './ScreenTable.vue'
export {
  default as NvScreenSelect,
  /** @deprecated Use `NvScreenSelect` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenSelect,
} from './ScreenSelect.vue'
export {
  default as NvScreenSearch,
  /** @deprecated Use `NvScreenSearch` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenSearch,
} from './ScreenSearch.vue'
export {
  default as NvScreenInput,
  /** @deprecated Use `NvScreenInput` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenInput,
} from './ScreenInput.vue'
export {
  default as NvScreenTabs,
  /** @deprecated Use `NvScreenTabs` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenTabs,
} from './ScreenTabs.vue'
export {
  default as NvScreenSegmented,
  /** @deprecated Use `NvScreenSegmented` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenSegmented,
} from './ScreenSegmented.vue'
export {
  default as NvScreenSwitch,
  /** @deprecated Use `NvScreenSwitch` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenSwitch,
} from './ScreenSwitch.vue'
export {
  default as NvScreenPagination,
  /** @deprecated Use `NvScreenPagination` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as ScreenPagination,
} from './ScreenPagination.vue'
