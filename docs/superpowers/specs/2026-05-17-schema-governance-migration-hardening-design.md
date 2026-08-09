# Schema 治理与迁移强化设计

## 背景

第五阶段已将 AppHub 和 Ops 从 PostgreSQL `EnsureCreated()` 快捷方式迁移到 EF Core migrations 与显式 migration runners。这是正确的基础，但仓库中仍存在若干已知 schema 治理缺口，记录在 `docs/architecture/database-schema-conventions.md` 和 `docs/architecture/database-schema-catalog.md` 中。

这些缺口应在 IAM、FileStorage、Notification、高风险 Ops 工作流或新的持久化服务开始增加更多表之前关闭。否则，未来每个服务都需要针对表注释、JSON 兼容性说明、migrations history 位置、catalog 对齐和约定测试单独进行清理。

因此，第六阶段将 schema 规则从文档提醒转变为可强制执行的代码和 focused tests。它还会清理第五阶段合并带来的少量规划交接偏差，确保后续代理正确理解项目状态。

## 推荐方案

围绕已有真实 migrations 的 AppHub 和 Ops 两个服务，实施范围收敛的 schema 治理强化切片。补充缺失的 EF metadata，将 migrations history 固定在各服务 schema 中，创建可复用的 schema 约定断言，并更新架构文档，使其与已强制执行的规则一致。

已考虑的替代方案：

1. 先实施 IAM 或 FileStorage，并在构建时增加 schema 规则。这样可更早形成可见的平台能力，但治理修复可能分散在多个新服务工作中。
2. 先构建客户发布 migration bundles 和安装脚本。这对发布就绪阶段很有价值，但这些脚本要打包的 schema 规则尚未完全强制执行。
3. 先强化 schema 治理。其产品可见度较低，但能避免 IAM、FileStorage、Notification 和 Ops 审批引入长期数据时反复修补。

本设计选择第三种方案。

## 范围

范围内：

1. 规范规划交接文档，避免 README、技术参考和第五阶段 plan 状态误导下一位执行者。
2. 为 AppHub 和 Ops 业务表增加表注释。
3. 强化 AppHub 和 Ops 的 JSON/text 列注释，使其说明格式、producer、consumer 和兼容性预期。
4. 将 AppHub 和 Ops PostgreSQL `__EFMigrationsHistory` 表配置在各自服务 schema 内。
5. 在 `backend/common/Testing/Nerv.IIP.Testing` 下增加可复用的 schema 约定测试 helper。
6. 增加 AppHub 和 Ops 测试，强制执行业务表注释、业务列注释、JSON/text 兼容性注释、string 强类型 ID 规则和 migrations history schema 配置。
7. 仅在 metadata 变更需要时重新生成或调整 EF migrations/model snapshots。
8. 更新 schema catalog、schema conventions、implementation readiness 及相关文档，确保代码、测试和文档一致。

范围外：

1. IAM 实现、登录、authorization guards 或 seed commands。
2. FileStorage 上传/下载、MinIO provider 或下载授权。
3. Notification、Knowledge、AI Integration 或 Observability 索引表。
4. 客户发布 migration bundle、安装包、Windows/Linux installer 或备份/恢复演练。
5. GaussDB、DMDB 或其他数据库 profile 验证。
6. 前端页面、导航、组件样式或 Design System 决策。

## 架构

强化工作应贴近现有服务边界。

```text
backend/common/Testing/Nerv.IIP.Testing/
  EntityFramework/
    SchemaConventionAssertions.cs

backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/
  AppHubPersistenceServiceCollectionExtensions.cs
  EntityConfigurations/*.cs
  Migrations/*

backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/
  AppHubSchemaConventionTests.cs

backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/
  OpsPersistenceServiceCollectionExtensions.cs
  EntityConfigurations/*.cs
  Migrations/*

backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/
  OpsSchemaConventionTests.cs
```

共享 testing helper 应检查 EF Core metadata，而不是原始 migration 文本。服务测试使用 PostgreSQL provider 和虚拟 connection string 创建服务 `DbContextOptions`，从而无需真实数据库即可取得 provider annotations 与 relational metadata。

该 helper 应具有足以供未来服务复用的通用性，但不得过度构建。它只需包含 `database-schema-conventions.md` 已要求的断言：

1. 业务表必须具有表注释；
2. 业务属性必须具有列注释；
3. 已配置的 JSON/text 属性必须有提及 JSON、producer、consumer 和兼容性的注释；
4. string 强类型 ID key 必须使用 `ValueGeneratedNever()` 和最大长度；
5. PostgreSQL options 必须将 `__EFMigrationsHistory` 放在服务 schema 中。

系统拥有的 CAP 表和 EF migrations history 无需具备完整的业务列注释，但必须继续在 catalog 中标记为系统拥有的基础设施表。

## 数据与 Metadata 流

AppHub 流程：

1. EF entity configurations 定义表名、注释、索引、转换和 JSON metadata。
2. Npgsql options 在 `apphub` schema 中定义 AppHub migrations history 表。
3. EF migrations/model snapshot 保留 PostgreSQL 将应用的 metadata。
4. AppHub schema 约定测试读取 `ApplicationDbContext.Model` 和 Npgsql relational options。
5. `database-schema-catalog.md` 描述相同的表、状态值来源和剩余服务边界。

Ops 流程：

1. EF entity configurations 定义 operation task、attempt 和 audit 表注释，以及 JSON/text 兼容性 metadata。
2. Npgsql options 在 `ops` schema 中定义 Ops migrations history 表。
3. EF migrations/model snapshot 保留该 metadata。
4. Ops schema 约定测试读取 `ApplicationDbContext.Model` 和 Npgsql relational options。
5. Catalog 和 readiness 文档反映当前已强制执行的治理基线。

## 错误处理

未来的持久化服务或表违反规则时，新测试必须明确失败。

失败信息应报告：

1. 服务名称；
2. entity/table 名称；
3. 相关时的 property/column 名称；
4. 缺失或不充分的约定；
5. migrations history 的预期 schema。

这些测试应避免依赖 Docker 或 PostgreSQL。真实 PostgreSQL 验证继续由 `scripts/verify-fifth-slice-persistence-foundation.ps1` 覆盖；schema metadata 规则应作为 backend solution 内的普通 unit tests 运行。

## 测试

实现应测试先行：

1. 增加会失败的 AppHub 和 Ops schema 约定测试，以暴露当前缺口。
2. 在 `Nerv.IIP.Testing` 中增加可复用的断言 helper。
3. 更新 AppHub/Ops 测试项目，使其引用 `Nerv.IIP.Testing`。
4. 增加表注释、JSON/text 注释和 migrations history schema 配置。
5. 如果 EF 检测到 metadata 变更，则重新生成 migrations 或 model snapshots。
6. 运行有针对性的 AppHub/Ops 测试。
7. 运行 `dotnet test backend/Nerv.IIP.sln`。
8. 如果 migrations 或 PostgreSQL 配置发生变化，运行 `pwsh scripts/verify-fifth-slice-persistence-foundation.ps1`。
9. 运行 `git diff --check`。

本阶段不触及 OpenAPI contracts 或前端文件，因此无需运行前端门禁。

## 文档

文档更新应保持精准：

1. `README.md` 应将当前基线描述为第五阶段已完成，并将第六阶段规划/实现描述为 schema 治理强化。
2. `docs/architecture/technology-stack-references.md` 不应再声称当前基线仅处于第四阶段。
3. `docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md` 应避免用已完成阶段后的未勾选任务误导后续代理。完成记录已经存在；任务清单可以明确标为历史记录，或在已完成项上勾选。
4. `docs/architecture/database-schema-conventions.md` 应区分当前已由测试强制执行的规则，以及新增持久化服务时适用的规则。
5. `docs/architecture/database-schema-catalog.md` 应移除本阶段已关闭的 AppHub/Ops 已知缺口，仅保留真实剩余缺口。
6. `docs/architecture/implementation-readiness.md` 应将本阶段标识为 IAM/FileStorage 持久化表之前的防护。

本阶段无需新增 ADR。ADR 0009 已记录持久迁移、发布和 seed 策略；本阶段实现并强制执行该既定决策的一部分。

## 完成定义

满足以下条件时，本阶段才可关闭：

1. AppHub 和 Ops 业务表具备表注释。
2. AppHub 和 Ops 业务列具备注释。
3. AppHub `Metadata` 和 `Capabilities` 注释解释 JSON 格式、producer、consumer 和兼容性。
4. Ops `ParametersJson` 和 `FailureJson` 注释解释 JSON 格式、producer、consumer 和兼容性。
5. AppHub 和 Ops 分别在 `apphub` 和 `ops` schema 中配置 `__EFMigrationsHistory`。
6. AppHub 和 Ops schema 约定测试无需真实数据库即可通过。
7. Backend solution 测试通过。
8. 如果 migrations 或 PostgreSQL provider 配置发生变化，第五阶段持久化验证仍然通过。
9. 文档不再把已关闭的 AppHub/Ops schema 治理缺口列为开放项。
10. 未引入任何前端功能或 Design System 工作。
