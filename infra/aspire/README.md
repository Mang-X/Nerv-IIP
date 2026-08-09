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

`fullstack run` 无论成功或失败都会尝试精确清理，同时保留 `artifacts/fullstack/<sessionId>/`。交互式 `fullstack start` 仅可用于诊断，且必须配对执行 `fullstack stop`；`fullstack gc` 会协调遗弃或过期的会话，而不会清理无关的 Docker 或 Aspire 资源。临时 PostgreSQL 为完整平台拓扑使用更高的连接上限，但持久化 `dev` 的镜像标签、卷名称和数据库设置保持不变。

## 可重复的领导演示环境

MAN-519/#960 在隔离全栈运行时之上增加了受治理的操作人员工作流。请在仓库根目录中使用
PowerShell 7 执行。管理员密码只能生成到当前进程；不得打印、作为命令行参数传递、通过
`setx` 持久化、放入用户机密或写入文件：

```powershell
$env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD = `
  [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + 'Aa1!'

.\nerv.ps1 demo reset
.\nerv.ps1 demo health-check
```

完整命令集如下：

```powershell
.\nerv.ps1 demo start
.\nerv.ps1 demo reset
.\nerv.ps1 demo seed
.\nerv.ps1 demo health-check
.\nerv.ps1 demo stop
```

`start` 创建一个全新的隔离会话。`reset` 仅停止由机器本地 leader-demo 指针授权的精确会话，
验证其归属资源已经消失，启动干净会话，并执行 `seed` 和 `health-check`。它绝不删除持久化
`dev` 数据库、共享卷或无关的 Aspire/Docker 资源。`seed` 不直接写入表；它通过已认证的公开
Gateway 事实来验证由服务负责的 opt-in 启动 seed。除非会话清单报告
`Messaging Provider=Redis`，且必需的 PostgreSQL、Redis、服务和公开入口点均健康，
否则 `seed` 和 `health-check` 都必须失败。

seed 边界仅限前置条件。由服务负责的 opt-in 启动 seed 会准备固定键 `SO-DEMO-001`、
`WO-DEMO-Q01` 和 `DEV-CNC-DEMO`、已启用的遥测规则 `ALARM-DEMO-001`，以及一个由
`SourceAlarmId=ALARM-DEMO-001` 关联、演示引用为 `SourceReferenceId=MWO-DEMO-001` 的
未关闭告警来源 Maintenance 工单；`demo seed` 验证这些公开事实，而不自行写入表。前置条件
还包括主数据、工程、原材料和质量计划事实。它们不得创建任何生产报告或已完成数量、产成品
库存、检验结论、NCR/挂起/审批处置、发货、应收款、遥测样本、告警事件或已完成维护工单。
这些结果必须由真实的演示工作流产生。

除固定键外，opt-in 的**规模块**会 seed 批量前置条件，使排程工作台能够演示千订单自动排程。
它由 `LeaderDemo:Scale:OrderCount` 控制；AppHost 在 leader-demo profile 下默认设为 `1000`，
其他环境一律设为 `0`。仅为当前进程覆盖它：

```powershell
$env:NERV_IIP_LEADER_DEMO_SCALE_ORDERS = "200"   # 0 disables the scale block
.\nerv.ps1 demo reset
```

规模块使用专用 `*-SCALE-*` 段（`SO-SCALE-#####`、`WO-SCALE-#####`、`WC-SCALE-*`、
`DEV-SCALE-*`、`SKU-SCALE-*`），绝不触及上述固定键，因此精确匹配数量始终为一。它保持在
前置条件边界内：跨四个工作中心和 24 个设备资源、带四项前序关联操作的已发布销售订单和
已发布工单；不包含生产报告、检验、收货、发货或应收款。重复执行 `seed` 是幂等的。在本地
Docker PostgreSQL 上，seed 1000 个订单在 ERP 中约需 3 秒、在 MES 中约需 6 秒。工作台的
每次生成批处理上限为 500 个订单（`SchedulingWorkbenchLimits.MaxOrderCount`），因此 1000 个
已 seed 订单构成积压池，而一次生成最多消耗其中 500 个。

第二个 opt-in 块会 seed **factory world-bible L0 主数据**
（`docs/superpowers/plans/2026-07-26-factory-world-bible.md` 第 1-6 节）：3 个车间、
14 条生产线、17 个工作中心、46 个设备资产（跨 3 个采集连接器，共 96 个采集标签）、
84 个 SKU（含 BOM/工艺路线/生产版本）、58 名员工（含团队和技能）、8 个客户和 10 个供应商。
它由 `LeaderDemo:World:Enabled` 控制；AppHost 在 leader-demo profile 下启用它，其他环境一律
禁用。仅为当前进程覆盖它：

```powershell
$env:NERV_IIP_LEADER_DEMO_WORLD = "false"   # disables the world-bible L0 block
.\nerv.ps1 demo reset
```

该块使用专用 `WS-`、`LINE-WB-`、`WC-`、`DEV-`、`FG-/SF-/RM-/PK-`、`CUST-WB-`、
`SUP-WB-` 和 `EMP-` 段，绝不触及固定键或 `*-SCALE-*` 段，因此精确匹配数量始终为一，
且 `LINE-DEMO-01.WorkshopCode` 保持为空。它仅创建结构性主数据——不创建订单、工单、生产报告、
检验、库存、遥测样本、告警或维护工单。连接器标签绑定会以 `pending` 形式 seed：连接器是否在线
由真实连接器或模拟器心跳决定，绝不由 seed 决定。在本地 Docker PostgreSQL 上，seed 在
MasterData 中约需 1.1 秒、在 ProductEngineering 中约需 1.9 秒，重复执行是幂等的。

当同一 `LeaderDemo:World:Enabled` 开关为 true 时，AppHost 还会启用一等公民的模拟
Connector Host 适配器。它等待 IndustrialTelemetry，通过 Aspire 注入已有的内部服务 token 和
服务 endpoint，将采集设为 2 秒、Ops 轮询设为 1 秒，并在所有非 world profile 中保持该适配器
禁用。三个 AppHub 实例和采集连接器标识严格为 `CONN-OPCUA-01`、`CONN-MQTT-01` 和
`CONN-MODBUS-01`；其精简的已签入 profile 会展开为与 L0 seed 相同的 46 个设备和 96 个标签
（44 个 OPC UA、28 个 MQTT、24 个 Modbus 绑定）。

默认重复 profile 为 45 分钟：15 分钟正常，随后依次为 10 分钟降级、告警和恢复。
`DEV-CNC-03/vibration`、`DEV-CTG-02/bath-temperature` 和 `DEV-AUX-04/air-pressure`
具有错开的相位偏移。值由配置的 seed 加稳定点位标识和周期推导而来，因此在相同受控时间下重启/
重放可重复，新增或重排序设备也不会扰动其他流。该 profile 仅包含单位、范围、可写限制和协议形态
地址；不包含 credential、客户 key、token 或密码。

在运行层面，AppHub 心跳证明 Host 存活；模拟现场连接、采集器计数器/最后样本和标签样本存在性
仍是独立的健康事实。失败遥测保持相同的源序列，进行有界指数重试。待处理样本和操作回执均具有
配置的硬容量。支持的演示控制为 `write-tag`、`parameter-set` 和 `start-stop`；
`OperationTaskId` 提供进程内幂等性，每个终态结果均携带关联的 `Good`、`BadNotFound`、
`BadNotSupported` 或 `BadOutOfRange` 设备回执。请通过 `.\nerv.ps1 demo stop` 或上述精确会话的
`fullstack stop` 工作流停止会话；Connector Host 取消会停止轮询/重试循环，且不需要广泛清理进程。

第三个 opt-in 块会 seed **factory world-bible L1 背景历史**（同一计划，第 7 节）：自
2026-01-05 上线以来约 29 周的 ERP 和 MES 业务记录——约 3283 个销售订单 `SO-2026-#####`
（包含发货、应收款、现金收款和借贷平衡的记账凭证）；约 490 个采购订单 `PO-2026-####`
（包含收货）；以及约 3616 个工单 `WO-2026-#####`（另有 `WO-2026-R####` 返工），每个包含
6-8 个工序任务、齐套快照、领料申请、生产报告和已过账的产成品收货。它需要 L0 块，并由
`LeaderDemo:History:Enabled` 控制：

```powershell
$env:NERV_IIP_LEADER_DEMO_HISTORY = "false"        # disables the L1 history block
$env:NERV_IIP_LEADER_DEMO_HISTORY_SCALE = "0.1"    # ~1/10 of the volume, for a fast check
.\nerv.ps1 demo reset
```

全部内容均由固定 seed 生成，因此无论规模如何，给定单据都字节级一致。时间戳回填到上线窗口内，
并保持在两班制日历中（周日为停工日，春节 2026-02-09..02-22 为低谷，月末激增）。ERP 和 MES
无需彼此通信便能遵循共享计划，因此 `SO-2026-#####` 只按业务编码与 `WO-2026-#####` 配对——
不存在跨 schema 外键。AppHost 向两个服务发送相同的 `LeaderDemo:History:AsOfDate`，因此跨越
午夜的启动不会拆分这对数据。

每个服务都会在完成 seed 前运行各自的**故障关闭**一致性验证器：从订单到回款及从工单到收货的
数量和金额链、单调时间戳、第 7 节状态组合，以及输出到日志的 20 条抽样端到端链。任何不平衡链
都会抛出错误并使服务启动失败。在本地 Docker PostgreSQL 上，全量 seed 在 ERP 中约需 11 秒、
在 MES 中约需 28 秒，重复运行是幂等的（约 0.2 秒）。归档证据：
`scripts/verify-world-history.ps1` → `artifacts/world-history/<runId>/`.

同一开关现还驱动依附于同一订单计划的**phase-2 历史领域**：**Quality**（约 6873 个检验任务，
覆盖来料、过程和最终检验；6570 条记录，其中包括 90 次复检；164 个 `NCR-2026-####` 不合格报告，
包含返工/让步/报废处置及其挂起轨迹）、**Inventory**（约 61.9k 条库存移动——期初余额、采购收货、
质量放行、上架、领料、线边倒冲、产成品收货、发货和报废调整——覆盖 5035 条台账行和 3681 个批次，
并独立核对 `on-hand = opening + in - out`）、**WMS**（3659 个入库单、22677 个出库单和 26336 个
仓储任务，全部终态）以及 **BarcodeLabel**；后者从零构建：4 个标签模板和条码规则、900 个打印批次、
3373 个标签项、1346 个 EPCIS 事件以及 3000 条扫描记录，其时间戳与所属源单据一致。

这四个服务也各自运行故障关闭验证器。全量 seed 在 Quality 中约需 7 秒、Inventory 中 20 秒、
WMS 中 12 秒、BarcodeLabel 中 3 秒，因此完整 L1 链——phase 1 和 phase 2 合计——约为 82 秒，
远低于 5 分钟启动预算。`scripts/verify-world-history.ps1` 收集全部六个服务的证据，并额外输出
一张包含 20 个订单的跨领域可追溯性表。

每次成功或失败的 `seed` 和 `health-check` 都会在
`artifacts/leader-demo/<UTC-run-id>/evidence.json` 保留已脱敏证据。清单包含会话 ID、提交、
资源状态、非机密 URL、通过公开角色目录从 `/auth/me` 解析出的实际账户角色 ID、包含关键事件、
观测时间和精确匹配数量（重复即失败）的固定事实、Redis 断言、关联的
`artifacts/fullstack/<sessionId>/` 诊断，以及精确清理命令；其中绝不包含密码或 bearer token。

对于三次 reset 验收门禁，请保留每次返回的会话 ID 和全部证据路径，并在三个全新数据库之间
比较固定键的观测/数量：

```powershell
$session1 = (.\nerv.ps1 demo reset | Select-Object -Last 1)
.\nerv.ps1 demo health-check
$session2 = (.\nerv.ps1 demo reset | Select-Object -Last 1)
.\nerv.ps1 demo health-check
$session3 = (.\nerv.ps1 demo reset | Select-Object -Last 1)
.\nerv.ps1 demo health-check

.\nerv.ps1 demo stop
.\nerv.ps1 fullstack stop -SessionId $session3
.\nerv.ps1 fullstack status -SessionId $session3
Remove-Item Env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD
```

幂等的精确会话 `fullstack stop` 检查必须报告 `state=Stopped` 和 `remaining=0`；随后的状态
必须报告 `state=Stopped` 和 `containers=0`。若 reset 或健康检查失败，请保留输出的证据路径，
然后只能通过该精确会话受治理的 `.\nerv.ps1 fullstack
status|logs` 命令调查。不得对这一验收工作流使用手动 Docker 清理、广泛的 Aspire stop 或仅供诊断的
`fullstack start`。

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

FileStorage 元数据不存储在 MinIO 或 Redis 中。AppHost 配置独立的 `file-storage-db` PostgreSQL 资源（`nerv_iip_filestorage`），将其注入为 `ConnectionStrings__FileStorageDb`，选择 `Persistence__Provider=PostgreSQL`，并在启动 FileStorage 前等待该数据库。FileStorage 遵循 AppHost 环境，而不采用旧资源使用的本地开发兼容性覆盖：本地 Development 启用 Web-host 自动迁移，而 Aspire 生产发布会输出 `ASPNETCORE_ENVIRONMENT=Production`、`DOTNET_ENVIRONMENT=Production` 和 `Persistence__AutoMigrate=false`。PoC 和生产操作人员须先通过 `scripts/install/migrate-file-storage.ps1` 应用迁移，并从当前进程或机密管理器提供 `NERV_IIP_FILE_STORAGE_DB`。

这些值仅供本地开发。不得将真实凭据提交到 `appsettings*.json`、源文件或文档示例中。

## 运行时镜像版本

AppHost 固定持久化本地基础设施镜像标签，而不使用 provider 默认值或 `latest`：

| 资源 | 当前标签 | 原因 |
| --- | --- | --- |
| PostgreSQL | `18` | 使用当前 PostgreSQL 18 主版本线，同时避免无界的 `latest` 标签。PostgreSQL Docker 镜像 18+ 在 `/var/lib/postgresql` 下使用特定于主版本的数据目录，因此 AppHost 使用新的本地开发卷 `nerv-iip-postgres-18`，而不复用旧的 17 时代 `nerv-iip-postgres` 卷。AppHost 和 Compose 有意为 PostgreSQL 18 挂载完整的 `/var/lib/postgresql` 父目录；这已在镜像升级后使用 Windows Docker Desktop 上的空本地开发卷验证，并可避免挂载旧 `/var/lib/postgresql/data` 路径导致的 PostgreSQL 18 初始化失败。 |
| Redis | `8` | 使用当前 Redis 8 主版本线，同时避免无界的 `latest` 漂移。AppHost 保留持久化 `nerv-iip-redis` 卷并启用快照持久化，因为当 `Messaging:Provider=Redis` 时，Redis 还可能承载 CAP Redis Streams。仅作缓存的本地数据可以重建，但 Redis 消息 profile 必须将该卷视为消息总线持久性状态。 |

不得将这些标签改为 `latest`。升级到下一个 PostgreSQL 或 Redis 主版本必须是有意创建的升级事项，
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

当 Business Console 看似缓慢或卡住时，请先检查 Aspire Dashboard，再考虑反复重启：

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
