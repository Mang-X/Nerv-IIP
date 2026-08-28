# ADR 导航

本目录记录会在当前任务结束后继续约束实现的长期决策，回答“为什么这样选择”。当前组件、命令、代码落点和实施状态分别由 Architecture、Governance、Runbook、Status 与 GitHub/Linear 承担。

新增或修订 ADR 前，先读 [`../governance/decisions/records.md`](../governance/decisions/records.md)。本页在 M0 只建立完整导航，不宣称所有“已接受”记录都未经修订地完全有效；Area、整篇取代、部分修订、复评触发和当前实现映射由 [GitHub #2291](https://github.com/Mang-X/Nerv-IIP/issues/2291) 补齐。

## 当前记录

- [ADR 0001](0001-backend-solution-and-service-boundaries.md) — `backend-solution-and-service-boundaries`
- [ADR 0002](0002-connector-host-and-app-integration-contract.md) — `connector-host-and-app-integration-contract`
- [ADR 0003](0003-data-and-messaging-baseline.md) — `data-and-messaging-baseline`
- [ADR 0004](0004-ai-integration-boundary-and-governance.md) — `ai-integration-boundary-and-governance`
- [ADR 0005](0005-knowledge-ingestion-and-retrieval.md) — `knowledge-ingestion-and-retrieval`
- [ADR 0006](0006-frontend-workspace-structure.md) — `frontend-workspace-structure`
- [ADR 0007](0007-vue-router-file-routing-colocation.md) — `vue-router-file-routing-colocation`
- [ADR 0008](0008-multi-target-deployment-and-aspire-apphost.md) — `multi-target-deployment-and-aspire-apphost`
- [ADR 0009](0009-database-migration-release-and-seed-strategy.md) — `database-migration-release-and-seed-strategy`
- [ADR 0010](0010-automation-script-trusted-execution-governance.md) — `automation-script-trusted-execution-governance`
- [ADR 0011](0011-integration-event-contract-baseline.md) — `integration-event-contract-baseline`
- [ADR 0012](0012-business-platform-domain-layering.md) — `business-platform-domain-layering`
- [ADR 0013](0013-business-master-data-governance.md) — `business-master-data-governance`
- [ADR 0014](0014-aps-and-iiot-scheduling-boundary.md) — `aps-and-iiot-scheduling-boundary`
- [ADR 0015](0015-gateway-http-client-resilience-strategy.md) — `gateway-http-client-resilience-strategy`
- [ADR 0016](0016-victorialogs-central-log-backend.md) — `victorialogs-central-log-backend`
- [ADR 0017](0017-business-process-manager-and-compensation-strategy.md) — `business-process-manager-and-compensation-strategy`
- [ADR 0018](0018-observability-alert-threshold-to-notification.md) — `observability-alert-threshold-to-notification`
- [ADR 0019](0019-wms-inventory-rpc-idempotency.md) — `wms-inventory-rpc-idempotency`
- [ADR 0020](0020-nvui-naming-token-namespaces-and-style-isolation.md) — `nvui-naming-token-namespaces-and-style-isolation`
- [ADR 0021](0021-product-docs-information-architecture.md) — `product-docs-information-architecture`
- [ADR 0022](0022-scheduling-rescheduling-evolution-freeze.md) — `scheduling-rescheduling-evolution-freeze`
- [ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md) — `filestorage-tus-proxy-staging-final-complete-invariants`
- [ADR 0024](0024-filestorage-storage-provider-and-local-production-semantics.md) — `filestorage-storage-provider-and-local-production-semantics`
- [ADR 0025](0025-field-capability-scope-shift.md) — `field-capability-scope-shift`
- [ADR 0026](0026-industrial-telemetry-historian-storage.md) — `industrial-telemetry-historian-storage`
- [ADR 0027](0027-filestorage-offline-migration-cutover-and-rollback.md) — `filestorage-offline-migration-cutover-and-rollback`
- [ADR 0028](0028-retired-vertical-slice-script-entry-boundary.md) — `retired-vertical-slice-script-entry-boundary`

## 阅读规则

1. 只读与当前任务相关的 ADR，不按编号从头通读。
2. ADR 决定“为什么和必须保持什么”，不能替代当前代码、Governance、Runbook 或验证证据。
3. 发现旧判断被推翻时，按决策记录 Governance 建立整篇/部分取代关系，不在旧记录末尾追加进度日志。
4. 找不到备选、理由或取代关系时明确登记待核，不根据当前实现反向编造历史。
