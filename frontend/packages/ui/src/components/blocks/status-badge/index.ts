// The StatusBadge block component was superseded by the Pro-layer NvStatusBadge
// (ADR 0020 §1.3) and removed at codemod closure (#789). `resolveStatus` and its
// status types stay — they are shared helpers, not a component.
export { resolveStatus } from './statusMap'
export type { ResolvedStatus, StatusTone } from './statusMap'
