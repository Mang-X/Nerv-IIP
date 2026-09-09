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
2. 只设 provider 不设 root path、或 root path 不是绝对路径时，服务在启动阶段拒绝并给出脱敏诊断；系统临时目录与容器可写层都不接受（[ADR 0024](../adr/0024-filestorage-storage-provider-and-local-production-semantics.md) §5）。
3. 更换该卷或该路径等同于更换文件存储后端：已 complete 的文件元数据仍在数据库，但字节会读不到。迁移按 [`file-storage-offline-migration.md`](file-storage-offline-migration.md) 执行，不要靠重挂空卷绕过。

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
