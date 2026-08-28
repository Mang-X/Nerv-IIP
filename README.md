# Nerv-IIP

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/Mang-X/Nerv-IIP)

Nerv-IIP 是面向数字工厂的工业应用平台。仓库以通用平台控制面为底座，持续演进主数据、产品工程、计划、库存、质量、MES、WMS、ERP、设备运行、维护、审批、条码、APS lite、BusinessGateway、Business Console 与 Business PDA 等业务能力。

平台坚持逻辑边界优先、公开契约稳定、真实依赖可验证和多部署目标共享单一编排模型。当前实现、架构、决策、项目状态与历史记录分别维护，不再由一份全局总账同时承担。

## 项目目标

1. 建立覆盖身份、权限、组织、环境、文件、应用目录、运维、审计与通知的平台控制面。
2. 建立从主数据、工程资料、需求与排程，到采购、库存、生产、质量、设备维护和经营管理的数字工厂业务主干。
3. 通过 BusinessGateway、Business Console 与 Business PDA 提供受统一权限和契约治理的业务入口。
4. 通过 Connector Host 与 Connector 模型纳管 Docker、Windows Service、HTTP 服务和工业协议连接器。
5. 在明确业务闭环内引入受治理的 AI 查询、知识检索和低风险执行能力。

## 核心原则

- 逻辑边界先冻结，物理部署保留弹性；业务域不以共享库名义回退到大单体。
- Platform SDK 提供稳定公开契约，但不成为新的运行时中心，也不暴露主平台内部实现。
- 主平台只承载通用控制面；工厂、产线、设备等行业语义由业务服务和领域扩展持有。
- 跨域协作通过公开契约、集成事件和受治理的 Gateway facade 完成。
- Aspire 是开发联调和部署拓扑的统一编排模型；其它交付目标从该模型适配，不维护第二套完整服务图。
- 自动化脚本治理副作用、超时、日志、进程清理和敏感信息；临时审计结果不演化为永久影子框架。
- 文档按 ADR、Architecture、Governance、Runbook、Reference、Status、Report 与 Product 分工，避免重复事实源。
- 当前实现行为最终以代码、配置、公开契约、测试和命令帮助为准。

## 快速开始

主要本地入口面向 Windows PowerShell：

```powershell
.\nerv.ps1 bootstrap
.\nerv.ps1 dev
```

在有网的空白 Windows 开发机器上，可让 bootstrap 安装缺失工具：

```powershell
.\nerv.ps1 bootstrap -InstallMissing
```

`bootstrap` 检查 .NET SDK、Node.js、pnpm、Docker 与 Aspire CLI，初始化 Development-only user-secrets 和 HTTPS 证书，并完成基础 restore/install/build。需要固定首次 seed 的本地 IAM 管理员密码时显式传入 `-LocalAdminPassword`；否则命令会生成随机本机值，不写入仓库。

`dev` 通过 Aspire AppHost 启动本地平台拓扑。关联 worktree 会自动采用隔离模式，实际 URL 可能动态分配；使用以下命令读取当前实例地址：

```powershell
.\nerv.ps1 describe business-console
.\nerv.ps1 describe business-gateway
```

并行会话需要一次性真实全栈验证时，使用受隔离和自动回收的 fullstack 会话：

```powershell
.\nerv.ps1 fullstack run -Scenario smoke
.\nerv.ps1 fullstack start
.\nerv.ps1 fullstack status
.\nerv.ps1 fullstack logs gateway
.\nerv.ps1 fullstack stop
```

`fullstack run` 在独立 Aspire 拓扑中执行场景并回收进程、容器和专属卷；`fullstack start` 仅用于交互诊断，结束后必须执行 `fullstack stop`。诊断产物位于 `artifacts/fullstack/<sessionId>/`。

只启动本地依赖或查看端口时使用：

```powershell
.\nerv.ps1 dev -InfraOnly
.\nerv.ps1 ports
```

完整的前置条件、密钥、数据库、worktree、端口和故障处理见 [本地开发与排障](docs/runbooks/local-development.md) 与 [Aspire 基础设施说明](infra/aspire/README.md)。

## 技术与运行基线

| 层 | 当前方向 | 精确版本或事实源 |
| --- | --- | --- |
| 前端 | Vue、Vue Router 文件路由、Pinia / Pinia Colada、Vite+、Tailwind CSS、shadcn-vue、Hey API | `.node-version`、前端 workspace 配置与 lockfile |
| 后端 | .NET、FastEndpoints、CleanDDD / NetCorePal、ASP.NET Core Auth、OpenTelemetry | `backend/Directory.Packages.props` 与各项目文件 |
| 数据与消息 | PostgreSQL 为主要持久化 profile，Redis、RabbitMQ、MinIO、Qdrant 按能力接入 | AppHost、配置、迁移和 provider 测试 |
| 编排与交付 | Aspire AppHost 为统一拓扑，Compose、安装包和发布脚本为交付适配 | `infra/aspire/`、`infra/deploy/` 与受治理脚本 |
| AI | Microsoft.Extensions.AI、DataIngestion 与 VectorData；复杂自治框架按真实闭环引入 | 代码引用、ADR 与相关 Architecture |

不在 README 手工复制易漂移的依赖版本、固定端口或能力完成状态；精确值从上述权威生产者读取。

## 仓库结构

```text
Nerv-IIP/
  backend/          后端服务、Gateway、公共基础设施与测试
  frontend/         Console、Business Console、PDA、Docs 与共享包
  connector-hosts/  Connector Host 与连接器实现
  infra/            Aspire、部署与环境适配
  scripts/          受治理的开发、验证、发布与维护入口
  docs/             ADR、架构、产品、治理、运行手册、状态与历史记录
  artifacts/        可复现的本地或 CI 诊断产物
```

具体放置规则与依赖边界见 [仓库布局](docs/architecture/repo-layout.md)。

## 文档入口

- [文档总入口](docs/README.md)：按任务选择 Architecture、ADR、Runbook、Governance、Reference、Status、Report 或 Product 文档。
- [当前项目状态](docs/status/current.md)：只维护全仓级重点、阻塞、统一入口和少量跨域注意事项。
- [当前架构入口](docs/architecture/README.md)：系统当前组件、边界、事实所有权与交互。
- [当前 Runbook 入口](docs/runbooks/README.md)：启动、部署、迁移、恢复与排障操作。
- [ADR 导航](docs/adr/README.md)：长期决策、选择理由及其后继关系。
- [产品定位](PRODUCT.md) 与 [设计原则](DESIGN.md)。
- [上下文地图](docs/architecture/context-map.md)、[仓库布局](docs/architecture/repo-layout.md) 与 [API 契约及代码生成](docs/architecture/api-contract-and-codegen.md)。

## 权威来源

- 当前实现行为：代码、配置、公开契约、测试和命令帮助。
- 当前项目进度、负责人、依赖和验收证据：GitHub / Linear。
- 当前全仓级状态摘要：[`docs/status/current.md`](docs/status/current.md)。
- 当前架构：[`docs/architecture/`](docs/architecture/) 中由架构入口路由的现态文档。
- 当前操作手册：[`docs/runbooks/`](docs/runbooks/) 中由 Runbook 入口路由的现态操作文档。
- 长期决策及理由：[`docs/adr/`](docs/adr/)。
- 历史时点记录：[`docs/status/archive/`](docs/status/archive/)；历史快照不构成当前实现或交付裁决。

旧路径 [`docs/architecture/implementation-readiness.md`](docs/architecture/implementation-readiness.md) 仅保留兼容导航，不再承载项目状态或全局事实。
