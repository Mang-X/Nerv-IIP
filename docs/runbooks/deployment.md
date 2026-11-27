# 部署与交付运行手册

本文只给出当前受支持的 bootstrap、Compose 生成/部署和 release-install 操作入口。命令与参数以 `nerv.ps1 help`、目标脚本 `Get-Help`、AppHost 和当前配置为准；本文不维护版本、服务数量或 secret 值。

架构边界见 [`../architecture/platform/deployment.md`](../architecture/platform/deployment.md)，交付规则见 [`../governance/delivery/deployment.md`](../governance/delivery/deployment.md)。

## 前置条件

1. 在目标 commit 的仓库根目录执行 `./nerv.ps1 help`（Windows PowerShell 可使用 `.\nerv.ps1 help`），确认命令仍存在。
2. 明确目标是 Development、PoC/容器化私有化还是 release-install；不要把本地 `dev`/bootstrap 当作客户最终安装器。
3. 非 Development 部署先按目标 AppHost/安装脚本核对必填配置与 secret。任何 fail-fast 输入缺失都应停止，而不是临时写入仓库或关闭守卫。
4. 涉及数据库变更时，先执行 [`database-release.md`](database-release.md) 的备份、目标库与 migration preflight。

## 联网空白机 / 本地 Aspire

联网开发机、联网实施机或 PoC 预检可使用：

```powershell
.\nerv.ps1 bootstrap -InstallMissing
.\nerv.ps1 dev
```

`bootstrap` 的真实实现是 `scripts/dev/bootstrap-online-environment.ps1`；它负责联网工具链检查/可选安装、本地 Development secrets、restore 与 AppHost build。它不负责离线包制作、生产 secret 生成、Windows Service/systemd 注册或备份恢复。

启动后使用受支持的生命周期入口诊断：

```powershell
.\nerv.ps1 status
.\nerv.ps1 describe <resource>
.\nerv.ps1 logs <resource>
.\nerv.ps1 wait <resource>
.\nerv.ps1 stop
```

更详细的本地/worktree 排障见 [`local-development.md`](local-development.md)。

## 生成与部署 Aspire Compose

先从当前 AppHost 生成 Compose 产物：

```powershell
.\nerv.ps1 publish-compose -EnvironmentName Production
```

默认输出位置和所有可选参数以 `nerv.ps1 help` 为准。需要环境化准备或直接通过 Aspire 部署时使用：

```powershell
.\nerv.ps1 prepare-compose -EnvironmentName Production
.\nerv.ps1 deploy-compose -EnvironmentName Production
```

不要把 `infra/compose/` 的 legacy overlay 当成完整平台拓扑；它只用于已有迁移期验证/发布演练。生成产物进入交付前，运行当前仓库提供的 production deployment artifact / release rehearsal 验证入口；准确脚本名、参数和 impact 以 `scripts/` 与 CI producer 为准。

### FileStorage tus 字节落点

FileStorage 的 tus 根目录承载上传暂存，默认 `local` 模式也存放已完成文件。生产部署须使用绝对路径和持久挂载，并在首次启动前写入与部署配置一致的卷身份。

| 用途 | AppHost 进程输入 | Aspire 生成的 Compose 输入 | Legacy Compose 输入 |
| --- | --- | --- | --- |
| 卷身份 | `Parameters__file-storage-volume-identity` | `FILE_STORAGE_VOLUME_IDENTITY` | `NERV_IIP_FILE_STORAGE_VOLUME_IDENTITY` |
| PlatformGateway 下载令牌 | `Parameters__file-storage-platform-gateway-download-token` | `FILE_STORAGE_PLATFORM_GATEWAY_DOWNLOAD_TOKEN` | `NERV_IIP_FILE_STORAGE_PLATFORM_GATEWAY_DOWNLOAD_TOKEN` |
| BusinessGateway 下载令牌 | `Parameters__file-storage-business-gateway-download-token` | `FILE_STORAGE_BUSINESS_GATEWAY_DOWNLOAD_TOKEN` | `NERV_IIP_FILE_STORAGE_BUSINESS_GATEWAY_DOWNLOAD_TOKEN` |

两枚下载令牌由部署方分别生成，通过 secret manager 或受限的部署环境注入。两枚令牌必须彼此不同，也必须与 `InternalService__BearerToken` 不同。FileStorage 接收两枚令牌，每个网关只接收自己的令牌。

1. 从目标 Compose 产物核对实际卷名、挂载点和 `FileStorage__Tus__RootPath`。默认 Aspire 命名卷挂到 `/home/app`，卷内相对路径为 `nerv-iip/file-storage/tus`。legacy overlay 挂到 `/var/lib/nerv-iip/filestorage`，卷内相对路径为 `tus`。使用 AppHost 直接启动进程时，按当前 AppHost 解析的宿主机路径准备目录。
2. 确认目标卷已挂载，按需在卷内创建 tus 根目录，并确保 FileStorage 运行账户可写根目录、可读标记。对 Docker 命名卷，在可访问该卷的 Docker 主机或挂载同一卷的 PowerShell 7 运维容器执行下列命令。将 `$tusRoot` 设为该执行环境中已核实的根目录绝对路径，将 `$volumeIdentity` 设为上表对应部署输入的同一稳定标识。初始化已有数据的卷前先核对其来源和备份。

   ```powershell
   pwsh -NoProfile -File ./scripts/install/initialize-file-storage-volume.ps1 -TusRoot $tusRoot -VolumeIdentity $volumeIdentity
   ```

   脚本只在指定目录写入 `.filestorage-volume-v1`，也可从当前进程的 `NERV_IIP_FILE_STORAGE_VOLUME_IDENTITY` 读取身份。成功日志为 `FileStorage 卷标记校验通过。`。使用同一身份重跑会保留已有标记。标记不匹配时停止发布，核对卷挂载、卷来源和输入，禁止覆盖标记。
3. 初始化成功后再启动 FileStorage 和网关。FileStorage 在非 Development 环境启动时检查标记；缺失或不匹配会拒绝启动。更换卷或根目录前按 [FileStorage 停服离线迁移运行手册](file-storage-offline-migration.md) 迁移字节并保留原卷供回滚。

### MES 安灯超时升级

先按 [数据库发布流程](database-release.md) 应用 MES `AddAndonEscalationPolicySnapshot` migration，再给 **MES 服务进程**注入 `Mes:AndonEscalation` 配置。生产者为 `AndonEscalationOptions` / `AndonEscalationWorker`；无策略时不启动扫描，日志明确报告 `Mes:AndonEscalation:Policies 未配置`，此时不得宣称超时升级可用。

```json
{
  "Mes": {
    "AndonEscalation": {
      "ScanInterval": "00:00:30",
      "Policies": [
        {
          "OrganizationId": "<组织 ID>",
          "EnvironmentId": "<环境 ID>",
          "Category": "Equipment",
          "UnclaimedTimeout": "00:05:00",
          "RecipientId": "<IAM 接收人 ID>"
        }
      ]
    }
  }
}
```

示例时限必须替换为组织确认的业务配置。环境变量等价形式为 `Mes__AndonEscalation__Policies__0__OrganizationId` 等键。每个组织/环境/类别只能有一条策略；类别取 `MaterialShortage`、`Equipment`、`Quality`、`Process`。时限与扫描间隔必须为正，组织、环境、接收人必须完整；无效或重复策略导致启动校验失败，修正配置后再启动，不生成默认阈值或接收人。扫描间隔默认 30 秒，仅为执行频率，不是业务响应时限。

策略在进程启动时加载，改动后重启 MES。尚未升级的在途呼叫使用本次扫描的策略（时限仍从原 `RaisedAtUtc` 起算）；缩短时限后已到期的呼叫可立即升级。升级时冻结接收人、实际时限和 UTC 时间，之后改配置或重启不会重写或再次发布；认领与关闭保持原首次响应事实。

成功启动日志给出策略数与扫描间隔；按 MES 既有详情/队列读面的 `escalatedAtUtc`、`escalationRecipientId` 验证结果。升级事件采用 `nerv-iip.<deployment-env>.business-mes.mes.andon-call-escalated.v1`，其中部署环境来自宿主环境名的小写形式，不能用租户 `EnvironmentId` 代替。事件与升级事实经同一 UoW 写入 CAP outbox；重复投递复用其 `EventId` 和 `andon-call-escalated:{CallId}` 业务去重身份。扫描发生持久化/发布错误时保留宿主错误日志、修复依赖后重启；不要清除升级事实来强制重发。这里验证的是 MES producer，站内通知消费及送达不由升级字段证明。

### 单实例 S3-compatible 正式文件

在 FileStorage 服务的部署环境中设定 `FileStorage__StorageProvider=s3`。沿用上述本地持久卷和卷身份配置来支持 tus 暂存及恢复。提前创建普通文件专用桶，关闭桶版本控制以使到期 GC 的删除回收字节，配置桶探测与所选命名空间内的对象读取、条件写入和删除权限。

| FileStorage 环境变量 | 部署值 |
| --- | --- |
| `FileStorage__S3__Endpoint` | 必填，绝对 `http` 或 `https` 服务地址，使用根路径。生产环境使用 TLS。 |
| `FileStorage__S3__Bucket` | 必填，已创建的正式文件桶。 |
| `FileStorage__S3__AccessKey`、`FileStorage__S3__SecretKey` | 必填，通过 secret manager 或受限部署环境注入。 |
| `FileStorage__S3__Region` | 默认 `us-east-1`，应与桶的签名区域匹配。 |
| `FileStorage__S3__ForcePathStyle` | 默认 `true`。设为 `false` 时，桶名作为主机名前缀，需相应 DNS 和证书。 |
| `FileStorage__S3__KeyPrefix` | 可选，相对对象键前缀，例如 `production/files`。 |

该模式采用单次条件 PUT，每个正式文件上限为 5 GiB，业务用途可配置更小的限制。对象服务须支持 SigV4、`If-None-Match: *` 条件写入和写后立即回读。切换已有数据前，按停服离线迁移原则准备并逐对象复核副本，保持每条数据库记录对应的键，核对挂起提交与新环境的桶和前缀，保留原卷供回滚。服务切换后检查 `/health/tus-volume` 返回 `200 Healthy`，并抽样复核已有文件和新上传。探针返回 `503 Unhealthy` 时暂停开放流量并检查权限、DNS、TLS 和配置。本地正式文件对账在 S3 模式返回 409。回退前需停止服务并复核切换后新增的正式字节和数据库状态。省略 `FileStorage__StorageProvider` 或设为 `local` 即选择本地模式。

## Release-install 与数据库迁移

1. 平台 AppHost 的受治理启动入口位于 `scripts/install/start-nerv-iip-apphost.ps1`。执行前用 `Get-Help` 核对当前参数，并通过安全的外部渠道注入环境配置与 secret。
2. 平台、业务与 FileStorage 的数据库迁移使用 `scripts/install/migrate-*.ps1` 的当前受治理入口；执行顺序、备份、seed、停止与恢复条件以 [`database-release.md`](database-release.md) 为准。
3. zip/交付包生成入口位于 `scripts/package/`；不要从历史 bootstrap 计划推断当前包内容。
4. Connector Host 是独立分发单元；升级或回滚 Connector Host 时不要把其生命周期绑成主平台数据库/服务的隐式步骤。

## 停止条件

出现任一情况应停止部署并先修正输入或 producer：

- 非 Development 必填配置或 secret 缺失，或服务/AppHost fail-fast。
- 目标数据库、备份、migration preflight 不满足数据库 runbook。
- 生成的 Compose/交付物与当前 AppHost、验证脚本或 CI contract 不一致。
- 需要依赖手工修改 legacy Compose 才能形成完整服务拓扑。
- 只能通过关闭认证、持久化、消息、CORS 或环境守卫才能继续。
- 真实命令与本文不一致；此时以 `nerv.ps1 help`/脚本帮助为准并修正文档。

## 恢复与证据

- Compose 生成/准备失败：保留日志，修正输入后从 AppHost 重新生成，不手工把失败产物提升为新拓扑来源。
- 数据库步骤失败：按 [`database-release.md`](database-release.md) 判断前滚、恢复或备份回退，禁止盲目重放非幂等动作。
- AppHost/资源启动失败：使用 `status`、`describe`、`logs` 和 [`local-development.md`](local-development.md) 定位资源级失败。
- 交付验收记录 commit/release、目标环境、实际 producer/命令、验证结果和日志位置；不得记录 secret 明文。

## Maintenance 停机超阈值升级通知

给 **Maintenance 服务进程**注入 `Maintenance:DowntimeEscalation` 配置；生产者为 `MaintenanceDowntimeEscalationOptions` / `MaintenanceDowntimeEscalationScheduler` / `MaintenanceDowntimeEscalationScanner`。没有 `Scopes` 时不扫描，日志报告未配置组织范围及计划员，不得宣称停机升级已启用。

```json
{
  "Maintenance": {
    "DowntimeEscalation": {
      "Threshold": "02:00:00",
      "ScanInterval": "00:05:00",
      "Scopes": [
        {
          "OrganizationId": "org-001",
          "EnvironmentId": "env-dev",
          "PlannerRecipientRefs": ["user:planner-user-id"]
        }
      ]
    }
  }
}
```

阈值和扫描间隔必须为正；每个组织/环境仅配置一项，计划员引用必须为 `user:<IAM user id>`。示例阈值应替换为业务确认值，计划员必须属于对应组织/环境。配置在启动时加载，修改后重启服务。环境变量对应 `Maintenance__DowntimeEscalation__Scopes__0__OrganizationId` 等键。

通过现有部署入口启动前，配置 `Notification:BaseUrl` 为可达的 Notification 服务地址，并让 Maintenance 与 Notification 使用既有内部服务认证配置。客户端复用 `IInternalServiceTokenProvider`；不在配置示例写入密钥。Notification 未就绪、认证失败或 intent 提交失败会使扫描失败并保留异常，修正地址/认证/服务状态后重启 Maintenance；不增加专用重试机制。

扫描以实际 `AssetUnavailableFromUtc` 起算，严格超过阈值且尚未完成/取消恢复时提交站内消息；预计恢复时间、报警解除与维修开工不结束实际停机。重复扫描及重启使用同一工单和实际停机起点生成的稳定 `DedupeKey`，由 Notification 的现有持久化去重边界处理。收件人配置修改不会改写已提交的通知。停用时移除 `Scopes` 并重启，不删除历史消息。验收须分别检查 Maintenance 扫描日志及对应组织/环境的计划员通知列表，不能把扫描启动当作消息投递成功。
