# ADR 0037：FileStorage 单实例 S3-compatible 存储

- 状态：已接受
- 日期：2026-11-27
- 部分取代：[ADR 0024](2026-08-17-0024-filestorage-storage-provider-and-local-production-semantics.md)

## 背景

当前通用 FileStorage 面向单实例部署。tus 已经在本地持久卷暂存上传，也已有 PostgreSQL 提交意图、恢复与发布流程。为支持 S3-compatible 正式字节，同桶 tus staging 会额外引入远端分片暂存与续传状态管理，当前部署范围无需这层复杂度。

## 决策

1. 支持单实例。多机、多副本、跨节点共享上传和运行时 provider 切换在范围之外。
2. 部署时按 ordinal 精确匹配 `FileStorage:StorageProvider`。缺省与显式 `local` 都选择本地后端，`s3` 选择 S3-compatible 后端。显式大小写变体、空白、未知值及无效的 S3 设置在启动时拒绝。进程固定使用一个正式字节后端。
3. `s3` 使用本地持久 tus staging。按 [ADR 0023](2026-08-17-0023-filestorage-tus-proxy-staging-final-complete-invariants.md) 冻结提交意图，校验暂存大小和实际 SHA-256，以条件写建立正式对象，流式回读复验后提交正式元数据。已有对象仅在实际字节一致时收敛成功。Tx2 后清理暂存，恢复时保留尚需验证的副本。
4. 允许沿用受校验的 `{organizationId}/{fileId}`，无需先全量迁移到 canonical v1。S3 使用原样 `ObjectKey`，可追加配置的前缀作为物理命名空间。键和前缀都必须为相对路径，拒绝控制字符、空段、路径穿越、反斜杠和百分号编码别名。数据库继续保存 provider 无关的键。
5. S3 下载、删除和桶健康使用选定的正式后端，诊断脱敏。本地正式文件对账仅适用于 `local`。普通文件 S3 设置与版本化归档独立。

## 取代范围与影响

本决策取代 ADR 0024 决策 1–3 及后续层接口中的同 provider staging 要求，取代决策 2、决策 9 及健康与失败矩阵中的缺省 provider 拒绝要求，并推迟决策 4 及后续层接口中的 canonical v1 强制迁移。显式配置与本地持久卷验证在启动时执行，S3 桶可达性通过依赖健康探针报告。ADR 0024 的 Local 持久生产语义、正式字节归属和完整性要求继续有效。

保留同桶 staging 会增加续传状态与对象分片生命周期管理。本地暂存方案继续依赖单实例持久卷，S3 的正式字节需要单独备份和恢复。已有 Local 数据切换前应停服并验证迁移后的正式字节。引入多实例或跨节点续传时复评本决策。
