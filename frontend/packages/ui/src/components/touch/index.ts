/**
 * Touch — large, touch-optimized components for tablet station boards (工位看板)
 * and workshop all-in-one kiosks (车间一体机). Principles: minimal operation
 * paths, big tap targets, glanceable at distance. Token-driven; never edits原版.
 */
export {
  default as NvTouchButton,
  /** @deprecated Use `NvTouchButton` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as TouchButton,
} from './TouchButton.vue'
export {
  default as NvStatTile,
  /** @deprecated Use `NvStatTile` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as StatTile,
} from './StatTile.vue'
export {
  default as NvQtyStepper,
  /** @deprecated Use `NvQtyStepper` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as QtyStepper,
} from './QtyStepper.vue'
export {
  default as NvTouchSegmented,
  /** @deprecated Use `NvTouchSegmented` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as TouchSegmented,
  type SegmentOption,
} from './TouchSegmented.vue'
export {
  default as NvStationBar,
  /** @deprecated Use `NvStationBar` (ADR 0020 NvUI); alias removed after codemod #789. */
  default as StationBar,
} from './StationBar.vue'
