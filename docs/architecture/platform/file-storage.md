# File Storage 当前架构

本文描述 Nerv-IIP 主平台 **当前通用文件能力的事实所有权、公开边界与现行字节路径**。迁移前混合基线包含大量按日期追加的实施状态、操作细节和已批准未实现目标，已按原 Git blob 冻结到 [`../../reports/file-storage-baseline-pre-m2-k-2026-09-07.md`](../../reports/file-storage-baseline-pre-m2-k-2026-09-07.md)。

## 事实所有权

File Storage 拥有文件元数据、上传会话、下载授权、内部对象定位、用途策略、保留与清理事实。业务服务只通过 `fileId`、`FileReference`、公开 API 或 Platform SDK 使用文件能力，不把对象存储 key、内部路径或长期凭据提升为业务契约。

Knowledge、Ops、AppHub 与业务域可以引用文件，但文件的业务语义仍由引用方拥有；File Storage 不解释知识源、运维任务、工程文档或业务附件本身的领域含义。

## 当前字节与元数据边界

1. 当前通用文件 metadata 使用 PostgreSQL-backed 实现；`filestorage` schema 与服务代码是持久化事实来源，运行环境不以 InMemory 作为 metadata 实现。
2. 当前通用文件字节只有本地 tus 链路可用，当前部署（AppHost 生成产物与 legacy overlay）都显式选择它；代码内的 provider 缺省值仍是 `server-proxy`，而 `server-proxy` 只产生占位上传指令，仓库没有与其对应的通用字节 `PUT` endpoint，其 complete 会失败关闭并把上传会话重新打开。
3. complete 的提交证据由服务从本地 tus 字节读回实际 size 与 canonical SHA-256，再与冻结的提交意图比对；该字节目录同时承载已 complete 文件，必须是显式、绝对、持久的位置（ADR 0024 §5）；其中「显式且绝对」在启动期校验，不满足即失败关闭，「持久」的判定手段仍未实现。staging/final 分区、按 `ObjectKey` 定位 final 与 atomic promote 仍未实现。
4. MinIO 当前服务独立的 `VersionedArchive` 合规归档边界，不是通用 File Storage 的当前字节后端。
5. `ObjectKey` 可以作为 File Storage 内部持久化事实存在，但不得暴露到公开 API、Gateway facade、SDK DTO 或业务持久化模型。
6. UI、外部应用、Connector Host 与业务服务不得绕过 File Storage 直接访问对象存储；上传、完成、下载都必须经过受控会话或授权入口。
7. purpose、content type / extension、quota、retention 等当前策略由 File Storage 自己的配置与实现解释；其它服务不能维护平行 allowlist 或配额事实。
8. 面向业务控制台的字节通路由 BusinessGateway 按用途单独开门，不存在通用文件门面：每条门面固定一个 `filePurpose` 与 owner，并在交付字节前复核目标文件用途，使业务域读权限不会退化成通用文件读权限。业务面不得把 File Storage 的 download grant id 交给调用方——该 id 是全服务共用命名空间且兑换面不校验用途——授权由网关在服务端签发并立即兑换。见 [ADR 0030](../../adr/0030-business-gateway-purpose-scoped-file-transfer.md)。

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
