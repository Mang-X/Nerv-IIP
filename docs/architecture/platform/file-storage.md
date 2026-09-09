# File Storage 当前架构

本文描述 Nerv-IIP 主平台 **当前通用文件能力的事实所有权、公开边界与现行字节路径**。迁移前混合基线包含大量按日期追加的实施状态、操作细节和已批准未实现目标，已按原 Git blob 冻结到 [`../../reports/file-storage-baseline-pre-m2-k-2026-09-07.md`](../../reports/file-storage-baseline-pre-m2-k-2026-09-07.md)。

## 事实所有权

File Storage 拥有文件元数据、上传会话、下载授权、内部对象定位、用途策略、保留与清理事实。业务服务只通过 `fileId`、`FileReference`、公开 API 或 Platform SDK 使用文件能力，不把对象存储 key、内部路径或长期凭据提升为业务契约。

Knowledge、Ops、AppHub 与业务域可以引用文件，但文件的业务语义仍由引用方拥有；File Storage 不解释知识源、运维任务、工程文档或业务附件本身的领域含义。

## 当前字节与元数据边界

1. 当前通用文件 metadata 使用 PostgreSQL-backed 实现；`filestorage` schema 与服务代码是持久化事实来源，运行环境不以 InMemory 作为 metadata 实现。
8. BusinessGateway 当前有两组面向业务控制台的文件面：工程 SOP 文件的下载授权与内容（门在 `business.engineering.documents.read`，以 `fileId` 为入参、网关侧不复核 purpose，并把 download grant id 交给调用方），以及交接班附件的上传与下载（门在 `business.mes.handovers.manage` / `business.mes.handovers.read`，用途与 owner 由网关固定，grant id 不出网关）。**两组的形态不同不是笔误**：适用于新开业务面的约束、既有 SOP 面的存量例外及其失效条件，以 [ADR 0030](../../adr/0030-business-gateway-purpose-scoped-file-transfer.md) 决策 2/3 为准，本页不复述规则。

## 契约与目标边界

- 当前公开 DTO、endpoint、purpose 注册、上传 provider 行为与 fail-closed 条件，以 File Storage 代码、Contracts、配置、迁移和测试为准。
- tus staging/final complete 等长期目标由相关 ADR 约束；“已批准目标”不等于当前 endpoint、provider、schema 或生产就绪事实。ADR 0023：[`../../adr/0023-filestorage-tus-proxy-staging-final-complete-invariants.md`](../../adr/0023-filestorage-tus-proxy-staging-final-complete-invariants.md)。
- SDK 边界见 [`sdk.md`](sdk.md)；API/Gateway 公开契约见 [`../integration/api-contracts.md`](../integration/api-contracts.md)。
- 受控 tus 代理入口不再只属 PlatformGateway；BusinessGateway 并列暴露业务面入口，两者同受「客户端只取得网关自有 URL」约束。ADR 0030：[`../../adr/0030-business-gateway-purpose-scoped-file-transfer.md`](../../adr/0030-business-gateway-purpose-scoped-file-transfer.md)。

## 操作与查询路由

- File Storage 停服离线迁移、切换与回滚：[`../../runbooks/file-storage-offline-migration.md`](../../runbooks/file-storage-offline-migration.md)。
- 部署配置与交付排障：[`../../runbooks/deployment.md`](../../runbooks/deployment.md)。
- 当前 Schema 人工目录：[`../../reference/data/database-schema-catalog.md`](../../reference/data/database-schema-catalog.md)。

操作命令、配置键、版本、一次性迁移步骤和阶段历史不在 Current Architecture 重复维护。
