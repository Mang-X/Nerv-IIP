# Aspire 本地开发

`Nerv.IIP.AppHost` 是 Gateway、AppHub、IAM、Ops、FileStorage、Connector Host、Business Gateway、Business Console 和共享基础设施的本地完整平台拓扑。

AppHost 有意不硬编码本地机密。`.\nerv.ps1 dev` 会在启动前检查必需的 AppHost 用户机密，缺失任一项即快速失败。这可避免 Aspire 悄然生成不再与现有 Docker 卷匹配的随机值。请将可重复使用的本地值存入 AppHost 用户机密存储：

在已联网的空白开发机器上，应优先使用仓库 bootstrap 入口：

```powershell
.\nerv.ps1 bootstrap -InstallMissing
```

该命令会检查所需工具链、在请求时通过 `winget` 安装缺失的 Windows 前置条件、
初始化缺失的本地 Development 用户机密、信任本地 HTTPS 开发者证书、还原后端/前端依赖，
并构建 AppHost。首次安装后，Docker Desktop 仍可能需要手动启动。以下手动命令仅适用于
你有意自行设置可重复使用本地值的情形。Bootstrap 不含固定的 IAM 管理员密码；如需已知的
本地登录密码，必须在首次数据库 seed 前传入 `-LocalAdminPassword`，或者自行检查/重置本地
用户机密值。

```powershell
dotnet user-secrets set "Parameters:iam-jwt-signing-key" "<at-least-32-byte-local-signing-key>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:internal-service-bearer-token" "<local-internal-service-token>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:postgres-password" "<local-postgres-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:redis-password" "<local-redis-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:minio-root-user" "<local-minio-user>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:minio-root-password" "<local-minio-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:iam-seed-admin-password" "<local-admin-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:iam-seed-connector-host-secret" "<local-connector-secret>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:connector-ingestion-token-signing-key" "<local-ingestion-token-signing-key>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

然后从仓库根目录启动平台：

```powershell
.\nerv.ps1 dev
```

`.\nerv.ps1 dev` 使用 `aspire start --non-interactive`，并会在从链接工作树打开仓库时
自动添加 `--isolated`。在隔离模式下，Aspire 可能分配动态主机端口而非规范本地端口；
请使用 `.\nerv.ps1 describe business-console` 或 Aspire Dashboard 资源页获取实际 URL。
必须通过 Aspire 停止平台，而不是终止 `dotnet`、AppHost 或 DCP 进程：

```powershell
.\nerv.ps1 stop
```

## 隔离的全栈验证会话

`dev` 是持久化的、由操作人员负责的开发环境。由代理负责或自动化执行的真实全栈验证必须改用临时会话：

```powershell
.\nerv.ps1 fullstack run -Scenario smoke
.\nerv.ps1 fullstack start
.\nerv.ps1 fullstack url business-console
.\nerv.ps1 fullstack status
.\nerv.ps1 fullstack logs gateway
.\nerv.ps1 fullstack stop
.\nerv.ps1 fullstack list
.\nerv.ps1 fullstack gc
```

每个会话都会获得已校验的 ID、动态分配的公共端口、会话专属的 PostgreSQL/Redis/MinIO/VictoriaLogs 卷、生成的仅 Development 使用的机密，以及容器归属标签。URL 应从会话清单中发现，而非使用规范端口矩阵。Aspire/DCP 提供逐会话代理；不存在需要在工作树之间协调的共享 Nginx 配置。默认准入上限为三个活跃会话，且没有最低可用内存规则。

`fullstack run` 无论成功或失败都会尝试精确清理，同时保留 `artifacts/fullstack/<sessionId>/`。交互式 `fullstack start` 仅可用于诊断，且必须配对执行 `fullstack stop`；`fullstack gc` 会核对并回收遗弃或过期的会话，而不会清理无关的 Docker 或 Aspire 资源。临时 PostgreSQL 为完整平台拓扑使用更高的连接上限，但持久化 `dev` 的镜像标签、卷名称和数据库设置保持不变。

## 本地可观测性

对于正常本地开发，`.\nerv.ps1 dev` 允许 Aspire AppHost 为其自身 Dashboard 注入 OTLP endpoint。
不得在此路径中为每个项目资源覆盖自定义 `OTEL_EXPORTER_OTLP_ENDPOINT`；这样会使 Dashboard
资源页保持健康，却因遥测被发送到其他位置而使 Structured logs、Traces 和 Metrics 页面为空。

可选的 AppHost OpenTelemetry Collector 路径仅用于 Collector/Compose 类测试：

```powershell
dotnet user-secrets set "Observability:UseCollector" "true" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Observability:AspireDashboardOtlpHttpEndpoint" "http://host.docker.internal:18890" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

当 `Observability:UseCollector=true` 时，服务遥测会通过 HTTP/protobuf 发送至本地 Collector
资源。Collector 随后可转发至独立的 Aspire Dashboard OTLP/HTTP endpoint，例如
`http://host.docker.internal:18890`。常规 AppHost 工作流必须保持此开关未设置。

启动后可通过以下方式检查遥测，无需从 UI 猜测：

```powershell
aspire otel logs
aspire otel traces
```

独立 Aspire Dashboard 适用于开发、PoC 和短期诊断。它在内存中存储遥测，绝不得将其视为
生产日志留存或审计后端。

相同的 MinIO root 用户和密码会作为本地 MinIO access key 和 secret key 传递给 FileStorage。若未来本地 profile 配置独立的 MinIO 服务账户，必须同时更新 AppHost 参数连线和本文档。

FileStorage 元数据不存储在 MinIO 或 Redis 中。AppHost 配置独立的 `file-storage-db` PostgreSQL 资源（`nerv_iip_filestorage`），将其注入为 `ConnectionStrings__FileStorageDb`，选择 `Persistence__Provider=PostgreSQL`，并在启动 FileStorage 前等待该数据库。所有项目资源都继承 AppHost 环境：本地 Development 启用 Web-host 自动迁移，而 Aspire 生产发布会输出 `ASPNETCORE_ENVIRONMENT=Production`、`DOTNET_ENVIRONMENT=Production` 和 `Persistence__AutoMigrate=false`。PoC 和生产操作人员须先通过 `scripts/install/migrate-file-storage.ps1` 应用迁移，并从当前进程或机密管理器提供 `NERV_IIP_FILE_STORAGE_DB`。

这些值仅供本地开发。不得将真实凭据提交到 `appsettings*.json`、源文件或文档示例中。

## 运行时镜像版本

AppHost 固定持久化本地基础设施镜像标签，而不使用 provider 默认值或 `latest`：

| 资源 | 当前标签 | 原因 |
| --- | --- | --- |
| PostgreSQL | `18` | 使用当前 PostgreSQL 18 主版本线，同时避免无界的 `latest` 标签。PostgreSQL Docker 镜像 18+ 在 `/var/lib/postgresql` 下使用特定于主版本的数据目录，因此 AppHost 使用新的本地开发卷 `nerv-iip-postgres-18`，而不复用旧的 17 时代 `nerv-iip-postgres` 卷。AppHost 和 Compose 有意为 PostgreSQL 18 挂载完整的 `/var/lib/postgresql` 父目录；这已在镜像升级后使用 Windows Docker Desktop 上的空本地开发卷验证，并可避免挂载旧 `/var/lib/postgresql/data` 路径导致的 PostgreSQL 18 初始化失败。 |
| Redis | `8` | 使用当前 Redis 8 主版本线，同时避免无界的 `latest` 漂移。AppHost 保留持久化 `nerv-iip-redis` 卷并启用快照持久化，因为当 `Messaging:Provider=Redis` 时，Redis 还可能承载 CAP Redis Streams。仅作缓存的本地数据可以重建，但 Redis 消息 profile 必须将该卷视为消息总线持久性状态。 |

不得将这些标签改为 `latest`。升级到下一个 PostgreSQL 或 Redis 主版本应当作为有意创建的升级事项，
并包含空卷测试、适用时的保留卷迁移测试、AppHost 构建、Compose 发布验证和启动 smoke test。对于
PostgreSQL，必须执行显式 `pg_upgrade`/导出恢复计划，或引入新的开发卷名称；不得让默认容器镜像
决定该迁移。

## 证书预检

Aspire AppHost、Dashboard/DCP 和本地 HTTPS 开发 endpoint 要求信任本地开发者证书。
`.\nerv.ps1 bootstrap -InstallMissing` 和 `.\nerv.ps1 dev` 现会在启动平台前检查此项。若检查
失败，请执行：

```powershell
aspire certs trust
dotnet dev-certs https --trust
```

如果 Aspire AppHost 日志在 Aspire 升级或证书缓存变更后报告证书名称不匹配，请重置本地
Aspire 证书缓存：

```powershell
aspire certs clean
aspire certs trust
dotnet dev-certs https --trust
```

参考：[Aspire 证书配置](https://aspire.dev/app-host/certificate-configuration/)。

## 启动故障排查

当 Business Console 看似缓慢或卡住时，不要反复重启，应先检查 Aspire Dashboard：

```powershell
dotnet user-secrets list --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
.\nerv.ps1 status
.\nerv.ps1 describe business-console
.\nerv.ps1 describe business-gateway
.\nerv.ps1 describe gateway
.\nerv.ps1 logs gateway -Tail 120
.\nerv.ps1 logs business-gateway -Tail 120
.\nerv.ps1 wait gateway -Status up -TimeoutSeconds 600
```

常见症状与修复：

| 症状 | 可能原因 | 处理方式 |
| --- | --- | --- |
| Dashboard 显示 `Unresolved parameters`，服务保持 `Waiting`。 | 缺少必需的 AppHost 参数。 | 设置上方全部 `Parameters:*` 用户机密，然后重启 `.\nerv.ps1 dev`。 |
| Aspire 显示资源已启动，但 `127.0.0.1:5119` 或 `127.0.0.1:5125` 拒绝连接。 | 仓库从链接工作树运行，且 `.\nerv.ps1 dev` 添加了 Aspire `--isolated`，因此主机端口为动态端口。 | 执行 `.\nerv.ps1 describe business-gateway` 和 `.\nerv.ps1 describe business-console`，使用其中显示的 URL。 |
| 前端端口 `5125` 有响应，但登录或 API 调用挂起。 | 存在过期的 AppHost/DCP 代理或失败的后端资源。 | 执行 `.\nerv.ps1 status`、`.\nerv.ps1 describe -IncludeHidden`，再在重新启动前执行 `.\nerv.ps1 stop`。除非 Aspire CLI 无法看到遗留进程，否则不得手动终止 AppHost/DCP。 |
| Aspire/provider 升级后，`postgres` 立即退出。 | AppHost 拉取的 PostgreSQL 镜像数据目录布局与现有 PostgreSQL 卷不兼容。 | PostgreSQL 18 使用 `nerv-iip-postgres-18`。除非正在执行显式迁移，否则不得将其指回旧 `nerv-iip-postgres` 卷。删除任何数据库卷前请检查 `.\nerv.ps1 logs postgres -Tail 120`。 |
| `postgres` 为 `Running (Unhealthy)`，依赖服务保持 `Waiting`。 | 持久化 `nerv-iip-postgres-18` Docker 卷使用的 `postgres` 密码不同于当前 Aspire `Parameters:postgres-password`。 | 应优先复用现有用户机密。若机密已改变且必须保留卷，请将容器的 `postgres` 用户密码与当前机密对齐。 |
| `redis` 因 RDB/AOF 格式错误退出。 | 本地 `nerv-iip-redis` 卷由不兼容的 Redis 主版本写入。 | 若 Redis 仅用于缓存/会话，请停止平台并仅移除 `nerv-iip-redis`，然后重启：`.\nerv.ps1 stop`；`docker volume rm nerv-iip-redis`；`.\nerv.ps1 dev`。若正在使用 `Messaging:Provider=Redis`，必须将该卷视为消息总线持久性状态，予以保留/备份/迁移，而不得随意删除。 |
| `.\nerv.ps1 dev` 或 `.\nerv.ps1 stop` 看似卡住。 | Aspire CLI 或 DCP 未及时返回。 | 脚本现使用有界命令并输出阶段诊断。检查最新 `artifacts/script-logs/dev-apphost/` 或 `artifacts/script-logs/aspire-stop/` 目录，然后重新执行 `.\nerv.ps1 status`。 |
| `http://127.0.0.1:5102/api/iam/v1/me` 返回 `401`。 | IAM 正在运行，但请求未认证。 | 登录前这是预期行为。可将其用作快速存活检查。 |
| `http://127.0.0.1:5119/swagger/v1/swagger.json` 返回 `200`。 | BusinessGateway 已启动。 | 登录后可从 `5125` 测试 Business Console API 代理。 |

## Docker Compose 产物

AppHost 包含 Aspire Docker Compose 部署目标。必须从 AppHost 生成 Compose 产物，而不是维护第二套
完整平台 compose 模型：

```powershell
.\nerv.ps1 publish-compose
```

发布前可运行下列真实产物验证；它会分别生成临时 Development 与 Production Compose 产物并逐资源检查环境、迁移和开发 seed 门控，随后删除该临时目录：

```powershell
pwsh scripts/checks/verify-aspire-apphost-environment-artifacts.ps1
```

默认输出路径为 `artifacts/aspire-output/compose`。要准备环境特定的值并构建镜像，请使用：

```powershell
.\nerv.ps1 prepare-compose -EnvironmentName Production
```

要让 Aspire 在一步中生成并应用 Docker Compose 部署：

```powershell
.\nerv.ps1 deploy-compose -EnvironmentName Production
```

当前限制：Platform Console 以 Aspire 静态网站发布，`/api` 代理至 PlatformGateway。Business
Console 在 Compose 输出中仍是开发 Vite 资源，直至 AppHost 建模其两条生产 API 路由（`/api/console`
至 PlatformGateway，`/api/business-console` 至 BusinessGateway），或由 BusinessGateway 负责
所需 auth facade。在添加并验证该生产服务模型前，绝不得将 Compose 发布视为完整的 Business Console
部署。

对于 PostgreSQL 密码不匹配的情况，除非可以接受丢失本地数据，否则避免删除
`nerv-iip-postgres-18`。要保留数据，请暂时放宽开发容器中的 `pg_hba.conf`，重置 `postgres`
密码后立刻还原该文件。若密码重置命令失败，必须在做任何其他操作前手动执行还原命令；否则容器
可能继续接受无密码本地连接。

```powershell
$container = "<postgres-container-name>"
$password = "<current-Parameters:postgres-password>"
$escaped = $password.Replace("'", "''")
$pgData = "/var/lib/postgresql/18/docker"

docker exec -u root $container sh -lc "cp $pgData/pg_hba.conf /tmp/pg_hba.conf.codex-bak && sed -i 's/scram-sha-256/trust/g' $pgData/pg_hba.conf && chown postgres:postgres $pgData/pg_hba.conf && chmod 600 $pgData/pg_hba.conf && kill -HUP 1"
docker exec $container psql -U postgres -h 127.0.0.1 -d postgres -c "ALTER USER postgres WITH PASSWORD '$escaped';"
docker exec -u root $container sh -lc "cp /tmp/pg_hba.conf.codex-bak $pgData/pg_hba.conf && chown postgres:postgres $pgData/pg_hba.conf && chmod 600 $pgData/pg_hba.conf && kill -HUP 1"
docker exec -e PGPASSWORD=$password $container psql -U postgres -h 127.0.0.1 -d postgres -c "select current_user;"
```

数据库健康后，预期的 Business Console 链路为：

1. `http://127.0.0.1:5125/login?redirect=/mes` 返回登录页。
2. 从 `.\nerv.ps1 describe business-gateway` 获取的 BusinessGateway URL 在
   `/swagger/v1/swagger.json` 返回 `200`。
3. 登录前，`http://127.0.0.1:5102/api/iam/v1/me` 返回 `401`。
4. 登录重定向至 `.\nerv.ps1 describe business-console` 提供的 Business Console URL，
   认证后通常为 `/mes`。
