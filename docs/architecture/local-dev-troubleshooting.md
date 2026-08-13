# 本地开发与 Aspire 排障

本文承载本地启动、Aspire、基础设施容器和部署产物的排障规则。部署拓扑由
`docs/adr/0008-multi-target-deployment-and-aspire-apphost.md` 与
`docs/architecture/deployment-baseline.md` 承载。

## 启动与生命周期

1. **绝不得使用 `dotnet run` 启动 AppHost。** 平台 AppHost 必须由 Aspire CLI 管理：
   `.\nerv.ps1 dev` / `aspire start`、`.\nerv.ps1 stop` /
   `aspire stop`、`.\nerv.ps1 wait <resource>`、`.\nerv.ps1 logs <resource>`。
   链接工作树（linked worktree）中启动必须使用 Aspire 隔离模式（isolated mode）；由 `scripts/dev.ps1` 处理。
   直接使用 `dotnet run` 会遗留过期 DCP/backchannel 状态，使后续 `aspire add`、部署和
   诊断不可靠。

2. **Aspire `Finished` 不是 Dashboard 问题。** 项目资源显示为 `Finished` 通常表示进程在
   启动期间已退出。修改代码或盲目重启前，先检查 `%TEMP%\aspire-dcp*` 下最新的 DCP stderr
   日志。真实错误通常位于资源进程日志，而不在 Aspire 本身。

3. **空白机器必须先完成 bootstrap。** 对新接入网络的 Windows 机器，先运行
   `.\nerv.ps1 bootstrap -InstallMissing`，再运行 `.\nerv.ps1 dev`。bootstrap 入口负责前置
   条件检查、可选工具安装、本地 AppHost 用户机密（user-secrets）初始化、依赖还原和 AppHost 构建。
   在该路径通过且 Docker Desktop 确实运行前，不得排查范围宽泛的请求失败。

4. **本地 HTTPS 证书。** Aspire Dashboard/DCP 和本地 HTTPS endpoint 需要受信任的开发证书。
   空白机器或 Aspire 证书缓存变更后，运行 `.\nerv.ps1 bootstrap -InstallMissing`，或用
   `dotnet dev-certs https --check --trust` 验证。若 AppHost 日志显示证书名称不匹配，使用
   `aspire certs clean`、`aspire certs trust` 和 `dotnet dev-certs https --trust` 重置。

5. **启动/停止脚本必须提供有界反馈。** `.\nerv.ps1 dev` 和 `.\nerv.ps1 stop` 必须显示阶段
   诊断并调用有界辅助函数。证书检查失败、容器退出、Aspire/DCP 卡死或启动成功都不得表现为
   “仍在等待”。Aspire CLI stop 超时时，stop 必须对当前仓库的 AppHost 进程和 Aspire
   usvc-dev 容器执行兜底清理。

## AppHost 配置

6. **新项目资源在本地以 Development 运行。** 平台 AppHost 是规范的开发启动器。除非有明确的
   测试/部署理由，新项目资源必须设置
   `ASPNETCORE_ENVIRONMENT=Development` 和 `DOTNET_ENVIRONMENT=Development` 运行。否则服务可能
   选择近似生产的持久化或消息分支，导致与本地预期不同的失败。

7. **PostgreSQL 服务需要启用本地 migration。** 若本地 Development 服务依赖 PostgreSQL
   migration，核实 AppHost 是否必须为该资源传入 `Persistence__AutoMigrate=true`。未启用 migration
   可能表现为范围宽泛的 Console 请求失败、下游 500s 或 Gateway circuit breaker；根因可能是缺少表，
   例如 `relation "...table..." does not exist`。已观察到的本地失败包括 AppHub
   `apphub.registration_idempotency`、MES 执行表、Maintenance readiness 表，以及 Notification 的
   `notification_messages` / `notification_tasks`。

8. **基础设施镜像 tag 必须固定。** 持久化本地资源必须在 AppHost 中显式固定版本。当前 PostgreSQL
   为 `18`、Redis 为 `8`；不得使用 `latest` 或未固定的 Aspire provider 默认值。PostgreSQL 18+ 的
   主版本数据目录布局与旧版（18 前）`/var/lib/postgresql/data` 不同，因此本地开发使用
   `nerv-iip-postgres-18`，绝不得让 PostgreSQL 18 指向旧
   `nerv-iip-postgres` 卷，除非已显式执行 `pg_upgrade` 或 dump/restore。没有受跟踪的升级计划、空卷测试、适用时的保留卷 migration 测试、AppHost
   build、Compose publish 验证和 smoke 启动，不得切换主版本。若 Redis 报告 RDB/AOF 格式错误，停止
   Aspire 后只移除本地 `nerv-iip-redis` cache 卷。

### 并行 worktree 共用真实依赖

普通开发只允许一个非 ephemeral AppHost 持有共享基础设施：从主 checkout 运行 `./nerv.ps1 dev`，
PostgreSQL 18 与 Redis 8 分别固定暴露在 `127.0.0.1:15432`、`127.0.0.1:6379`。其他 worktree
只作为测试客户端连接这两个 endpoint，不再各自启动 `./nerv.ps1 dev`；`fullstack run/start` 仍是一次性
隔离会话并使用动态端口和 session 专属卷，不能把其 endpoint 写入长期 profile。若固定端口已被占用，先用
`./nerv.ps1 status` 与 Docker 资源列表确认所有者，不得另起第二套共享 AppHost 或随意终止未知容器。

真实依赖测试沿用 CI 同名入口变量。PostgreSQL 变量是能够 `CREATE DATABASE` / `DROP DATABASE` 的
管理员基础连接串，必须指向 `postgres` 管理库；Redis 变量使用 StackExchange.Redis endpoint 格式。
密码来自本机 AppHost user-secrets，下面的占位符必须在本机替换，绝不得提交到仓库：

```bash
export NERV_IIP_TEST_POSTGRES='Host=127.0.0.1;Port=15432;Database=postgres;Username=postgres;Password=<本机 Parameters:postgres-password>'
export NERV_IIP_TEST_REDIS='127.0.0.1:6379,password=<本机 Parameters:redis-password>,abortConnect=false'
```

需要长期复用时，建议把这两行放进权限为 `0600`、不在仓库内的本机环境文件，再由 shell profile
`source`；不要把明文凭据散落到各 worktree。测试使用 `PostgreSqlTestDatabase` 为每个用例创建
`nerv_*_<UUIDv7>` 临时库，并在 factory 构建、AutoMigrate 或 seed 前验证连接目标属于该用例。

测试运行期间不得执行 `./nerv.ps1 stop` 或 `./nerv.ps1 stop -All`。停止共享 AppHost 会中断连接并可能
留下临时库；普通 dev 的 `nerv-iip-postgres-18` 与 `nerv-iip-redis` 都是持久卷，stop/restart 不会清空
残留。崩溃或 Ctrl-C 后先预览至少存活 24 小时且没有活动连接的标准临时库，再显式清扫：

```bash
pwsh scripts/cleanup-stale-postgres-test-databases.ps1
pwsh scripts/cleanup-stale-postgres-test-databases.ps1 -MinimumAgeHours 24 -Apply
```

该入口需要 PowerShell 7、主机可用的 `psql` 和已设置的 `NERV_IIP_TEST_POSTGRES`。它只接受严格的
`nerv_*_<UUIDv7>` 名称，以 UUIDv7 时间与最小存活时长共同筛选；删除前再次检查活动连接，使用普通
`DROP DATABASE` 而非 `WITH (FORCE)`，并在删除后精确回读。业务库、非标准名称、未到年龄或有活动连接
的数据库都不会删除。性能基线测试继续使用独占实例，禁止接入共享 AppHost，以免资源竞争污染测量。

9. **Bootstrap seed 密码绝不硬编码。** 联网机器的 bootstrap 可以创建本地 Development user-secrets，
   但不得在源码中保留固定 IAM admin 密码。默认生成随机本地值，或要求操作者通过不记录日志的路径显式
   传入值。设置 secret 的命令必须将敏感参数标记为脚本日志脱敏对象。

10. **Connector 断连验收是 opt-in 的真实基础设施门禁。** 运行
    `pwsh scripts/verify-connector-health-disconnect.ps1 -Runs 3`；不得启用
    `ConnectorHealthAcceptance:Enabled` 供普通 `nerv.ps1 dev` 或客户 profile 使用。验收 profile 会将 session-scoped internal token、
    IndustrialTelemetry endpoint 和 loopback Modbus mapping 注入 Connector Host，随后把证据写入
    `artifacts/script-logs/connector-health-disconnect/<timestamp>/`。即使 simulator、script contract 和
    AppHost build 门禁通过，Docker daemon/runtime 健康失败仍表示未发生真实运行（例如 0/3）。修复
    Docker Desktop 后重跑；绝不得放宽固定十秒（ten-second）deadline。

## 服务启动失败模式

11. **CAP PostgreSQL profile 未注册 integration event publisher。** 含有
    domain-event-to-integration-event converter 的服务必须在活动 CAP profile（包括 PostgreSQL）中注册
    NetCorePal integration event publisher。若启动因未解析的
    `NetCorePal.Extensions.DistributedTransactions.IIntegrationEventPublisher` 失败，修改 handler 前先将该
    服务的 CAP 注册与已知可用服务进行比对。

12. **Redis 支持的服务在首次连接时中止启动。** 本地 Aspire 启动可能与 Redis readiness 发生竞争。
    服务构造 `ConnectionMultiplexer` 时，应以 `AbortOnConnectFail=false` 解析选项，使服务能够启动并重连，
    而不是把一次瞬时 Redis 竞争转为资源失败。

## 部署产物

13. **Aspire AppHost 是唯一拓扑来源。** 容器部署应新增/维护 Aspire deployment target，并使用
    `.\nerv.ps1 publish-compose` 生成 Docker Compose 产物，或使用 `.\nerv.ps1 deploy-compose` 部署。
    既有手写 Compose 文件可以保留用于依赖项、smoke 测试或 legacy overlay 验证，但绝不得成为竞争性的
    服务图。

14. **Vite 开发 proxy 不是生产路由。** `AddViteApp` 可用于本地开发，但 publish/deploy 需要明确的
    JavaScript 生产托管模型。Console 可使用 `PublishAsStaticWebsite("/api", gateway)`。Business Console
    需要两条生产 API route（`/api/console` 指向 PlatformGateway，`/api/business-console` 指向
    BusinessGateway），或等价的 BusinessGateway auth facade；在此之前，不能将 Compose 输出称为完整的
    Business Console 部署。

15. **离线部署是独立轨道。** 离线打包属于部署架构轨道，而非首个本地开发修复。即时启动路径应聚焦
    联网机器和 Aspire CLI/AppHost。后续离线脚本必须消费 Aspire 生成的产物，不得发明平行拓扑。
