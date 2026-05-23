# BusinessApproval MVP Design

## Goal

Build BusinessApproval as the business document approval fact source for templates, approval chains, approval steps, decisions and approval result events.

BusinessApproval handles domain documents such as ECO, purchase requisitions, work orders, count variances and sales discounts. It does not replace Ops platform operation approval.

## Current State

BusinessApproval has no service directory. Ops owns platform operation tasks and platform approval/audit lifecycle. IAM owns users, roles, permissions and authorization scopes.

## Owned Facts

BusinessApproval owns:

1. ApprovalTemplate: document type, step definition, approver strategy and active status.
2. ApprovalChain: approval instance for a business document reference.
3. ApprovalStep: ordered approval step, assigned approver refs, status and due date.
4. ApprovalDecision: approve/reject/return action, actor, comment and timestamp.
5. ApprovalDocumentReference: source service, document type, document ID and optional line ID.

BusinessApproval does not own:

1. IAM user, role, permission or membership facts.
2. Ops operation tasks, operation attempts or platform approval lifecycle.
3. The business document state owned by ProductEngineering, Inventory, MES, WMS or ERP.
4. Notification delivery state.

## API Surface

| API | Purpose | Permission |
| --- | --- | --- |
| `POST /api/business/v1/approvals/templates` | Create or update an approval template. | `business.approvals.manage` |
| `GET /api/business/v1/approvals/templates` | List approval templates. | `business.approvals.read` |
| `POST /api/business/v1/approvals/chains` | Start an approval chain for a business document. | `business.approvals.manage` |
| `GET /api/business/v1/approvals/chains/{chainId}` | Read chain detail and decision history. | `business.approvals.read` |
| `GET /api/business/v1/approvals/tasks` | List pending approval steps for a user or service context. | `business.approvals.read` |
| `POST /api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/resolve` | Approve, reject or return a step. | `business.approvals.manage` |

## Rules

1. Approval chains are created from active templates.
2. A chain references a source service and business document reference; it does not copy the source document payload.
3. Steps resolve in configured order. In the MVP, `parallelGroupKey` is carried as template/query metadata for grouping same-numbered steps; it does not implement any-one approval semantics, and every pending approver in the same `stepNo` must approve before the next step can resolve.
4. Duplicate approver actions are idempotent only when the same actor repeats the same decision payload.
5. A conflicting duplicate action from the same actor is rejected.
6. A rejected or returned chain is terminal unless a future version adds reopen behavior.
7. Business services consume approval result events and update their own document state.
8. ApprovalTemplate may reference IAM user IDs, groups or permission codes, but BusinessApproval does not copy IAM roles or memberships.

## Events

BusinessApproval publishes ADR 0011 envelope events:

1. `businessApproval.ApprovalStarted`
2. `businessApproval.StepResolved`
3. `businessApproval.ApprovalApproved`
4. `businessApproval.ApprovalRejected`
5. `businessApproval.ApprovalReturned`

Events carry public approval IDs, source document references, actor references and result status. They do not carry IAM role internals or full business document payloads.

## Permissions

Initial permission codes:

1. `business.approvals.read`
2. `business.approvals.manage`

## Persistence

Default schema: `business_approval`.

Required tables:

1. `approval_templates`
2. `approval_template_steps`
3. `approval_chains`
4. `approval_steps`
5. `approval_decisions`

Each table and business column requires schema comments. PostgreSQL migrations history must use `business_approval.__EFMigrationsHistory`.

## Testing

Acceptance requires:

1. Domain tests for template activation, chain creation, ordered step resolution and terminal states.
2. Domain tests for duplicate and conflicting approver actions.
3. Web tests for route shape, authorization, validation and operation IDs.
4. Schema convention tests using `Nerv.IIP.Testing`.
5. Integration event converter/serialization tests for started, step resolved, approved, rejected and returned events.
6. Tests proving Ops types are not referenced by BusinessApproval Domain or Infrastructure.
