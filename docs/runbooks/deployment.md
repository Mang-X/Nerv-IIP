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

`bootstrap` 的真实实现是 `scripts/bootstrap-online.ps1`；它负责联网工具链检查/可选安装、本地 Development secrets、restore 与 AppHost build。它不负责离线包制作、生产 secret 生成、Windows Service/systemd 注册或备份恢复。

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

FileStorage 的 tus 目录同时承载已 complete 文件的字节，因此部署必须给它一个显式、绝对、持久的位置：

1. `FileStorage__UploadProvider=tus` 与 `FileStorage__Tus__RootPath` 由 AppHost 一起下发；生成的 Compose 产物把该路径放在 `nerv-iip-file-storage` 命名卷的挂载点内，legacy overlay 有对应的同名卷。
2. 只设 provider 不设 root path、或 root path 不是绝对路径时，服务在启动阶段拒绝并给出脱敏诊断（诊断只输出 `<missing>` / `<relative>`，不回显路径值）。[ADR 0024](../adr/0024-filestorage-storage-provider-and-local-production-semantics.md) §5 还要求该位置**持久**——系统临时目录与容器可写层不合规，但持久性当前不由启动校验判定（归 #1012），配错仍能启动，需要部署方自己保证。
3. 更换该卷或该路径等同于更换文件存储后端：已 complete 的文件元数据仍在数据库，但字节会读不到。迁移按 [`file-storage-offline-migration.md`](file-storage-offline-migration.md) 执行，不要靠重挂空卷绕过。

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
