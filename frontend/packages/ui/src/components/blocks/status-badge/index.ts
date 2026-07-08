// StatusBadge (block) is superseded by the Pro-layer NvStatusBadge (was
// StatusBadgePro) per ADR 0020 §1.3; kept only for the transition, removed at
// codemod closure (#789). `resolveStatus` + its types stay (not a component).
export {
  /** @deprecated Superseded by `NvStatusBadge` (was StatusBadgePro) per ADR 0020; removed after codemod #789. */
  default as StatusBadge,
} from './StatusBadge.vue'
export { resolveStatus } from './statusMap'
export type { ResolvedStatus, StatusTone } from './statusMap'
