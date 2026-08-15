# 主平台开发入口设计

## 目的

本设计将现有平台拓扑转化为一等开发体验。

仓库已经在 `infra/aspire/Nerv.IIP.AppHost` 下提供平台级 Aspire AppHost，该 AppHost 描述真实本地拓扑：Gateway、IAM、AppHub、Ops、FileStorage、Connector Host、Console、PostgreSQL、Redis、RabbitMQ、MinIO 和 OpenTelemetry Collector。目前缺少的是仓库根目录中的统一命令入口。

期望结果很简单：开发者克隆仓库并安装文档所列 runtime 后，即可通过一个稳定命令启动主平台，无需重新摸索服务依赖顺序和分散的端口。

## 当前背景

1. Aspire AppHost 已是本地开发与集成的拓扑来源。
2. `infra/docker-compose.dev.yml` 继续作为验证脚本使用的稳定依赖专用后备入口。
3. 现有验证脚本位于 `scripts/` 下，并受 `docs/architecture/script-automation-governance.md` 治理。
4. `scripts/lib/ScriptAutomation.ps1` 已为原生命令、后台进程、日志、超时清理和限定范围的环境变量提供安全 wrapper。
5. 前端命令目前由 `frontend/package.json` 和 `frontend/apps/console/package.json` 暴露。
6. 服务启动端口不一致：Gateway 使用 `5073`，Ops 使用 `5105`，AppHub 使用 `5204`，FileStorage 使用 `5261`，IAM 使用 `5283`；旧文档和 fallback 代码仍提及 `5100`、`5103`、`5104` 和 `5105`。
7. Console 开发当前使用 `127.0.0.1:5173`，这是 Vite 常见默认端口，不应成为平台规范 Console 端口。
8. Aspire AppHost 与 `infra/docker-compose.dev.yml` 中的本地 MinIO runtime 引用目前均使用 `minio/minio`；本地开发应迁移到仍受维护的 `pgsty/minio` image 系列。

## 推荐方案

在受治理脚本之上使用轻量 CLI wrapper。

所选方案分为两层：

1. 仓库根目录的 `nerv.ps1` 是面向人的命令入口。
2. `scripts/dev.ps1` 包含受治理的本地启动实现，并将进程执行委托给 `scripts/lib/ScriptAutomation.ps1`。

这样既能让项目呈现为完整平台，又不会过早引入重量级 CLI framework。根 CLI 应有意保持轻量；长期行为应放在受治理脚本中。

## 已考虑的替代方案

1. **仅使用脚本**：增加 `scripts/dev.ps1` 并编写文档。该方案稳定，但仓库仍会显得像多个子项目的集合，而不是一个平台。
2. **完整 CLI 工具**：构建 .NET 或 Node CLI。长期看很有吸引力，但会在命令集尚不足以支撑其必要性时，引入新的打包和版本管理面。
3. **轻量 CLI 加受治理脚本**：在根目录暴露 `.\nerv.ps1 dev`，把实际工作保留在 `scripts/` 中，仅在命令面扩大后再演进为完整 CLI。本设计选择该方案。

## 范围

### 范围内

1. 增加根目录 `nerv.ps1` 命令 wrapper。
2. 增加 `scripts/dev.ps1`，作为主要本地平台启动脚本。
3. 增加通过 `.\nerv.ps1 ports` 暴露的简单端口矩阵命令。
4. 将本地 HTTP 服务端口规范为连续的平台区间。
5. 更新服务 `launchSettings.json` 文件和本地 fallback base URLs，使其与矩阵一致。
6. 必要时更新 Console 服务端默认 API base URL。
7. 在 README 和架构文档中更新日常启动路径。
8. 为新脚本增加脚本治理覆盖。
9. 为命令路由、脚本治理和 AppHost build 增加 focused verification。
10. 使用显式 release tag，将本地 MinIO container image 引用从 `minio/minio` 更新为 `pgsty/minio`。

### 范围外

1. 构建打包的 .NET global tool、npm package 或独立二进制 CLI。
2. 替换作为拓扑来源的 Aspire AppHost。
3. 从 Aspire 生成生产 Docker Compose。
4. 实现 Windows Service、systemd 或离线安装工作流。
5. 在生态默认端口更易识别时，强行把基础设施服务端口改成连续区间。
6. 更改生产配置、客户部署端口或网络策略。
7. 启动或停止当前本地开发拓扑之外、由用户管理的 Docker 资源。
8. 替换 FileStorage object-storage 抽象，或选择非 MinIO 的 S3-compatible backend。

## 命令面

首个版本支持以下命令：

```powershell
.\nerv.ps1 dev
.\nerv.ps1 dev -NoBuild
.\nerv.ps1 dev -InfraOnly
.\nerv.ps1 dev -OpenDashboard
.\nerv.ps1 ports
.\nerv.ps1 help
```

`.\nerv.ps1 dev` 运行 Aspire AppHost：

```powershell
dotnet run --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

当底层命令支持时，`-NoBuild` 转发预期的 `--no-build` 行为。

`-InfraOnly` 仅通过 `infra/docker-compose.dev.yml` 启动依赖服务。该选项用于聚焦后端的测试和迁移工作，不用于完整平台。

`-OpenDashboard` 预留用于在能够可靠发现 Aspire dashboard URL 时打开或显示该地址。如果首版实现无法发现它，脚本应输出清晰消息，并将该标志保留为 no-op，而不是猜测地址。

`.\nerv.ps1 ports` 输出规范的本地开发端口矩阵。

## 端口矩阵

平台 HTTP 服务使用从 `5100` 开始的连续区间：

| 端口 | 服务 |
| --- | --- |
| `5100` | PlatformGateway |
| `5101` | AppHub |
| `5102` | IAM |
| `5103` | Ops |
| `5104` | FileStorage |
| `5105` | Console |

基础设施服务保留生态中熟悉的端口：

| 端口 | 服务 |
| --- | --- |
| `15432` | PostgreSQL host mapping |
| `6379` | Redis |
| `5672` | RabbitMQ AMQP |
| `15672` | RabbitMQ Management |
| `9000` | MinIO API |
| `9001` | MinIO Console |
| `4317` | OTLP gRPC |
| `4318` | OTLP HTTP |

## Container Image 基线

本地开发应使用 `pgsty/minio`，而不是 `minio/minio`。

实现应更新以下两个本地拓扑入口：

1. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
2. `infra/docker-compose.dev.yml`

使用显式 release tag，而不是 `latest`；设计时预期 tag 为：

```text
pgsty/minio:RELEASE.2026-04-17T00-00-00Z
```

如果实现时存在更新的 `pgsty/minio` release，应在检查 image metadata 后优先使用最新稳定 release tag。该变更仅涉及本地开发 container image。平台仍通过 FileStorage provider 抽象处理 object storage，并继续兼容 MinIO 或同等 S3-compatible object storage。

服务端口更新应涉及：

1. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Properties/launchSettings.json`
2. `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Properties/launchSettings.json`
3. `backend/services/Iam/src/Nerv.IIP.Iam.Web/Properties/launchSettings.json`
4. `backend/services/Ops/src/Nerv.IIP.Ops.Web/Properties/launchSettings.json`
5. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Properties/launchSettings.json`
6. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json`
7. `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json`
8. `Program.cs` 文件中代表本地开发默认值的硬编码 fallback URLs。
9. 如果 `frontend/packages/api-client/src/transport/base-url.ts` 的服务端默认值仍指向旧 Gateway 端口，则更新该文件。
10. 更新 `frontend/apps/console/package.json` 和 `frontend/apps/console/vite.config.ts`，使 Console dev server 不再使用 Vite 默认的 `5173` 端口。

## 架构

### 根 CLI Wrapper

`nerv.ps1` 应只负责命令分派：

1. 根据自身位置解析仓库根目录。
2. 解析第一个位置参数命令。
3. 将 `dev` 参数转发给 `scripts/dev.ps1`。
4. 为 `ports` 输出静态端口矩阵。
5. 对 `help` 或未知命令输出简洁用法文本。

它不得直接运行 `dotnet`、`docker`、`pnpm` 或其他原生工具。这样可将原生执行限制在受治理脚本中。

### 开发脚本

`scripts/dev.ps1` 应：

1. 声明 category 为 `check` 的 `Script-Governance` metadata。
2. dot-source `scripts/lib/ScriptAutomation.ps1`。
3. 使用 `Get-Command` 和清晰的错误消息校验所需工具。
4. 完整平台启动时，对 Aspire AppHost 调用 `Invoke-DotNet`。
5. 使用 `-InfraOnly` 时，对 `infra/docker-compose.dev.yml` 调用 `Invoke-DockerCompose`。
6. 使用 `ScriptAutomation.ps1` 中现有日志与脱敏行为。
7. 避免输出 secrets、完整 connection strings 或 tokens。

完整平台路径不得按依赖顺序手动启动服务。该依赖图由 Aspire 拥有。

### Aspire AppHost

AppHost 继续作为本地拓扑来源。如果直接 browser/API 访问需要固定本地服务端口，实现应通过 AppHost resource endpoint 配置或服务 launch settings 设置，且不得产生分叉的拓扑事实来源。

### 文档

README 应在当前状态或技术基线附近增加简短的“日常开发”章节：

```powershell
.\nerv.ps1 dev
```

其中应说明 Aspire 是完整平台入口，而 `.\nerv.ps1 dev -InfraOnly` 只启动依赖服务。

架构文档应记录：根 CLI 命令是受治理脚本的 wrappers，而不是独立部署模型。

## 错误处理

1. 文档应明确说明需要 PowerShell 7。脚本在不兼容的 PowerShell 下运行时应尽早失败。
2. 缺少 .NET SDK 时，应在调用 AppHost 前失败。
3. 只有使用 `-InfraOnly` 或 Aspire/container 资源需要 Docker 时，缺少 Docker 才应导致失败。
4. Console 是 AppHost 拓扑的一部分，因此完整 AppHost 启动前应报告 pnpm 缺失。
5. 文档应把端口冲突列为常见本地故障。首版实现可以依赖底层工具错误，但文档应引导用户运行 `.\nerv.ps1 ports`。
6. 未知 CLI 命令应返回非零退出码并输出用法文本。

## 测试与验证

Focused verification 应包含：

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1`
2. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 ports`
4. `pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 help`

如果时间和本地依赖允许，运行一次短时 `.\nerv.ps1 dev` 启动 smoke test，并在 AppHost 报告资源开始启动后停止。首版实现不应把该 smoke test 变成脆弱的长时间门禁。

## 迁移说明

1. 提及旧本地端口的文档如果属于面向用户的当前指南，则应更新。
2. 历史 Superpowers plans 如果明确属于归档实现说明，可以保留旧端口。
3. 除非测试明确验证本地默认值，否则测试应优先使用已配置的 base URLs，而不是硬编码端口。
4. `infra/docker-compose.dev.yml` 应继续将 PostgreSQL 保持在 `15432`，因为该决策有意避免与本地 `5432` 上的 PostgreSQL 冲突。

## 成功标准

1. 开发者可以在仓库根目录运行 `.\nerv.ps1 dev`，启动完整本地平台拓扑。
2. 开发者可以运行 `.\nerv.ps1 ports` 并查看规范的本地端口矩阵。
3. 平台 HTTP 服务端口连续且有文档记录。
4. 现有直接服务 fallback URLs 不与文档记录的端口矩阵冲突。
5. 新脚本通过脚本治理。
6. README 清晰展示日常启动路径，无需用户先阅读依赖拓扑。
