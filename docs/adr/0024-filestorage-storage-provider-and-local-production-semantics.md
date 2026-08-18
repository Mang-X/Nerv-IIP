# ADR 0024：FileStorage storage provider 抽象与 Local 单机生产语义

- 状态：已接受
- 日期：2026-08-17
- 关联：[Issue #992](https://github.com/Mang-X/Nerv-IIP/issues/992)、[Issue #1627](https://github.com/Mang-X/Nerv-IIP/issues/1627)、[ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md)

## 背景

[ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md) 已经冻结通用 FileStorage 的上游提交协议：tus 是上传协议，PlatformGateway proxy 是受控入口拓扑，storage provider 是字节后端；tusdotnet 只写 upload-session staging；final 只由内部 `ObjectKey` 定位；complete 必须通过持久 `committing` 意图、幂等 promote、final 回读复验和最终数据库事务收敛。它没有决定 storage provider 的接口所有权，也没有定义 Local 如何成为可用于单机生产的字节后端。

当前运行时代码仍把上传传输和本地字节存储耦合在一起。`IFileStorageUploadProvider` 只产生 `UploadMode`、`Provider` 和上传指令；`LocalTusFileStore` 未配置 root 时回落系统临时目录，按 `SHA256(uploadSessionId).bin` 存放字节；complete validator 对非 `tus` provider 直接跳过校验。虽然单次 append 调用了 `Flush(flushToDisk: true)`，系统仍没有 final provider 抽象、staging/final 分区、atomic promote、目录元数据持久化、路径 confinement、稳定 storage identity、mount 替换检测或容量健康语义。

FileStorage 还包含独立的 `VersionedArchive` 合规归档子系统。它通过 `IVersionedObjectStore` 和 `MinioVersionedObjectStore` 直接使用 MinIO versioning、version id 与 legal hold，不经过通用文件的 upload session、`StoredFile` 或 download grant。通用文件 provider 和该归档接口需要共享最底层连接配置时仍必须保持语义隔离。

上述内容是截至 2026-08-17 的当前实现事实，不是本 ADR 接受的目标已经交付。

## 范围与非范围

本 ADR 只决定两组目标架构：

1. `IStorageProvider` 的职责、LocalFileSystem / S3-compatible 两种实现、部署期二选一，以及它与 tusdotnet `ITusStore` 上传面的边界；
2. LocalFileSystem 作为单机生产 provider 的持久 root、路径安全、持久化与 atomic promote、崩溃收敛、容量和 mount identity 健康语义。

以下内容不在本 ADR 范围内：

- 不修改运行时代码、测试、公开契约、SDK、endpoint、Gateway、OpenAPI 或 generated client；
- 不修改 EF entity/configuration、schema、migration、数据库列或索引；
- 不修改 AppHost、Compose、OpenShip、systemd、Windows Service、安装/发布脚本或环境模板；
- 不同步 FileStorage baseline、database schema catalog、deployment baseline、implementation readiness 或其他周边权威文档；
- 不编写离线迁移 manifest、复制、校验、切换、回退、清源命令或 runbook；
- 不重开 ADR 0023 的 tus、Gateway proxy、staging/final、`ObjectKey`、canonical checksum、共享栅栏或 Tx1/Tx2 决策；
- 不决定 `UploadMode` / `Provider` 公开字段的兼容、版本切换或调用方同步；
- 不建立 provider registry、provider instance 表、可选能力矩阵、per-file routing、多 placement、多副本、dual-write、read fallback 或在线迁移；
- 不支持 NFS/SMB/NAS 多节点共享写、跨节点 failover 或同步复制；
- 不恢复 direct S3 multipart / presigned upload，也不把 ETag 当作 canonical checksum；
- 不给通用文件面增加 object lock、legal hold、WORM、residency、应用层 envelope encryption、内容扫描体系或安全擦除承诺；
- 不扩展 retention 或删除语义，不修改 `VersionedArchive` 行为；
- 不钉死 C# 方法签名、配置键名、identity marker 文件格式、具体系统调用或第三方库。

本 ADR 的裁决只能被显式 ADR 修订取代。实施发现与本 ADR 冲突时，必须通过显式 ADR 修订重新裁决，不能在代码 PR 中静默改变语义，也不得让实施层的容量准入动作矩阵、健康状态分类或路径安全口径与本 ADR 分叉。

## 继承 ADR 0023 的约束

本 ADR 将以下规则视为不可变输入：

1. tus 是唯一目标上传协议；PlatformGateway proxy 是受控入口；storage provider 是字节后端，三者不得重新混为一类。
2. tusdotnet 只负责传输、offset 恢复和 staging 写入，不能生成 completed/available 文件事实。
3. staging 可续传、可过期、不可下载；final 只由相对、provider 无关且不公开的 `ObjectKey` 定位。本 ADR 在不改变该所有权的前提下冻结其 canonical 编码。
4. complete 对所有 provider 都必须证明实际字节存在、size 与 canonical SHA-256，并执行幂等 promote、final 回读复验和冲突检测。
5. FileStorage application 和 PostgreSQL 独占 upload-session 共享栅栏、持久 `committing` 意图、Tx1/Tx2 与唯一 completed/available 文件事实。provider 不提交业务状态，也不得持有数据库事务跨越 storage I/O。staging 上传面、`ObjectKey` locator 面与 `IStorageProvider` final 面都只消费这些已冻结的提交意图与提交证据：不得另造第二套提交协议，不得重新定义共享栅栏、Tx1/Tx2 或恢复流程的所有权，也不得由上传面或 locator 面持有 Tx1/Tx2。

## 决策

### 1. `IStorageProvider` 只拥有 final 字节后端语义

`IStorageProvider` 是通用文件 final 字节面的基础设施边界，不是上传协议、Gateway 入口、领域聚合或 provider registry。它不创建上传会话，不做 IAM 或业务授权，不生成公开上传指令，不持久化 `StoredFile` 或 upload-session 状态，也不拥有 complete 的 Tx1/Tx2。

tusdotnet `ITusStore` 是同一已选 storage backend 的 staging、offset 和 expiry 上传面。不得再新造一层与 `ITusStore` 重叠的上传存储抽象。FileStorage complete orchestrator 以 `ITusStore` 提供的 staging 身份和实际字节为输入，按 ADR 0023 的持久 `committing` 意图调用 `IStorageProvider` 建立或收敛 final，再完成 final 回读复验与 Tx2。

职责固定如下：

| 责任 | tusdotnet `ITusStore` staging 面 | `IStorageProvider` final 面 | FileStorage application / PostgreSQL |
| --- | --- | --- | --- |
| tus offset、PATCH、expiry | 负责 | 不负责 | 执行会话准入与共享栅栏 |
| staging 字节与续传身份 | 负责 | 不拥有 final 之外的业务事实 | 编排存在性、size 与 SHA-256 证明 |
| promote 到 `ObjectKey` | 提供 provider-specific staging 身份或字节 | 以统一结果语义建立或收敛 final | 按持久 intent 编排，不跨 storage I/O 持有数据库事务 |
| final Head / OpenRead / Range / Delete | 不负责 | 负责 | 执行授权、生命周期与状态判断 |
| completed/available 事实 | 不负责 | 不负责 | 独占 |

接口名称表达职责，具体方法签名与内部类型由实施票决定；实现不得借此改变表中的所有权。

promote 是同一 provider family 内部的 commit bridge，而不是 application 可以解释的跨层路径协议。它可以由同一基础设施组件同时实现该 family 的 `ITusStore` 与 `IStorageProvider`，也可以由两个适配器共享私有 staging/final locator 与 commit primitive；两种形态必须具有相同所有权和结果语义。application 只传递 opaque staging identity、canonical `ObjectKey` 与冻结的 size/checksum 意图，不得拼接或解释 filesystem path、bucket、prefix 或 object key。

该 bridge 不取得 staging 生命周期所有权。offset、expiry、abort 与受治理的 staging cleanup 仍归 `ITusStore` 上传面；除 Local atomic rename 本身消费 staging 名称外，promote 不得提前删除 staging。任何补充 cleanup 都必须晚于 final 回读复验和 Tx2 completed/available 事实提交，并遵守“不删除唯一恢复副本”。

### 2. LocalFileSystem 与 S3-compatible 部署期严格二选一

通用文件面只有 LocalFileSystem 与 S3-compatible 两个 `IStorageProvider` 实现。每个部署、每个 FileStorage 运行实例只能显式选择一个 active provider，进程生命周期内不可热切换。不得按文件、组织、用途、大小、客户端或网络条件动态路由，也不得同时启用 Local 与 S3、同步 dual-write、read fallback、多 placement 或多副本。

同一运行实例的 tus staging 和 final 必须属于同一 provider family：

- Local staging/final 位于同一持久 root、同一 filesystem/mount；
- S3-compatible staging/final 位于同一受控 endpoint/bucket，可以使用相互隔离的 prefix。

非 Development 环境缺 provider、值未知、同时配置多个 active provider、必需配置缺失、preflight 失败或已初始化存储的配置指纹不匹配时，服务必须 startup fail-fast；不得静默回退到 `server-proxy`、系统临时目录或另一 provider。

离线工具在 FileStorage 停止后显式打开 source 与 target 不属于运行实例的双 provider；其授权、流程和切换规则由后续迁移层定义。本 ADR 不据此允许服务运行时共存。

### 3. 两种 provider 共享强制能力与结果语义

LocalFileSystem 与 S3-compatible 都必须提供 ADR 0023 所需的统一强制语义，不建立可选能力矩阵：

1. 查询 final 的存在性与实际 size；
2. 完整读取 final，并支持受控 Range 读取；
3. 从同 provider staging 幂等 promote 到 `ObjectKey`；
4. 回读 final，使服务端能够计算并复验 canonical SHA-256；
5. 删除 final，并把不存在或已经删除视为可辨识的幂等结果；
6. 为后续受治理离线工具提供 provider-neutral 的读取、写入/建立、Head 与复验证据，但不暴露成公开业务 API；
7. 对不可用、超时、容量不足、冲突、永久配置错误和安全拒绝给出可脱敏诊断。

promote、Head、写入和 Delete 的结果必须能够区分：目标不存在；目标已存在且实际 size/canonical checksum 一致；目标已存在但内容冲突；暂时不可用或可重试失败；永久配置或安全失败。不得用模糊布尔值吞掉冲突与重试语义。

`ObjectKey` 对 provider 是 opaque、相对、provider 无关的 final locator。绝对路径、bucket、endpoint、credential、presigned URL 和 provider 私有标识不得进入公开 DTO 或业务长期事实。每个 provider 在触碰字节前仍须独立验证 locator。

ETag、客户端 checksum、HTTP 成功响应或对象 metadata 不能替代服务端对实际物理字节计算的 canonical SHA-256。实现可以选择在 provider 内部或由 application 通过流式读取计算，但对外证明强度不得降低。

### 4. `ObjectKey` 使用跨 provider、跨 OS 的唯一 canonical contract

新建通用文件的 `ObjectKey` 必须且只能使用以下 v1 grammar：

```text
v1/{organizationDigest}/{fileDigest}
```

- `organizationDigest = lowerhex(SHA-256(UTF-8("org" + U+0000 + organizationId)))`；
- `fileDigest = lowerhex(SHA-256(UTF-8("file" + U+0000 + fileId)))`；
- `organizationId` 与 `fileId` 必须是有效 Unicode scalar sequence；编码前不做 trim、大小写折叠或 Unicode normalization，无效序列失败关闭；
- key 必须匹配 `\Av1/[0-9a-f]{64}/[0-9a-f]{64}\z`：恰有三个非空 segment，长度依次为 2、64、64，总长固定 132 个 ASCII byte/character，只允许 `/` 作为 segment separator；
- 比较完整 key 时必须使用 ordinal、case-sensitive byte equality。不得接受大写 hex、反斜杠、重复/尾随分隔符、`.`、`..`、percent decode、Unicode 等价折叠或 provider/OS 自动 normalization；
- 两个输入 identity 即使只在大小写或 Unicode normalization 形式上不同，也必须生成不同 key。若已存在 key 绑定到不同 identity 或内容，必须报告冲突并失败关闭，不能覆盖或合并。

规范测试向量：`organizationId = "acme"`、`fileId = "file_00000000000000000000000000000000"` 时，key 必须是 `v1/f960a43e09fd76bfdb8631a7a6e4b93f6dfe13801b8dd680462a8bfaba529f57/ff2b6fa4c01004f5f8dafa637ccb7d9f929d5dba8a09c4bb5bd3fc3e67c19724`。

该固定 ASCII grammar 在 Local、S3-compatible、Linux、Windows 与 macOS 上只有一个字面表示；provider 只能把完整 canonical key 映射到自己的私有 root/prefix，不能从 digest 反推出业务 identity，也不能为历史拼写建立大小写、分隔符或 normalization alias。application 持久化并传递完整 key，但仍不解释物理路径或 bucket key。

所有不严格匹配 v1 grammar 的既有 key——包括当前 `{organizationId}/{fileId}` 候选——均为 legacy/non-compliant，不因能被某个后端读取就视为 canonical。#994 必须在生成新 key 前审计 upload session、`StoredFile` 与现有字节引用，输出无别名、无冲突的显式迁移/修复映射；冲突、缺失或无法证明的条目失败关闭。不得在读取时静默 lower-case、normalize、重写或同时接受新旧 alias。

### 5. Local 使用显式持久 root、分区布局和稳定身份

Local Production、PoC 与私有化部署必须显式配置绝对、持久 root；不得使用系统临时目录、进程 cwd、隐式用户目录或容器可写层。root 下必须分离 staging 与 final 命名空间：tus 只能按 upload-session staging 身份写 staging；final 只能按 `ObjectKey` 访问；PATCH 不得打开 final。

staging 与 final 必须处于同一 filesystem/mount。跨 volume 或跨 mount 的配置在 startup/preflight 阶段失败，不得在运行时退化为 copy + delete 并声称 atomic promote。

首次部署必须通过显式初始化建立 storage identity 与布局版本标记。Production 服务不得仅因看到一个空目录就自动认领为新存储。后续启动至少验证：逻辑 storage identity、canonical root、filesystem/mount identity、布局版本和关键配置指纹。出现以下任一情况时必须 startup fail-fast 或进入 runtime critical/unready，不能初始化成空库继续服务：

- 持久 root 或 mount 被替换；
- 预期 volume 未挂载而宿主创建了同名空目录；
- root 挂到另一 filesystem/mount；
- 已有数据后关键配置被原地改变；
- identity marker 缺失、冲突或无法可信读取。

marker 的文件名、序列化格式、配置键名与安装入口留给实施票；“显式初始化、稳定身份、替换时失败关闭”是不可弱化的结果。#1012 是 initialization/identity 的实施 owner，负责显式初始化入口、marker 生命周期、与数据库/部署身份的交叉校验及故障证明。

### 6. Local 路径解析必须在实际操作时保持 confinement

root、staging 与 final 路径必须 canonicalize，并证明实际解析结果严格位于预期受控树内；简单字符串前缀比较不构成证明。provider 必须拒绝 rooted/absolute 路径、`..`、改变解析结果的空段或分隔符混用，以及 Windows drive、UNC、NTFS ADS、保留设备名等平台逃逸形态。

root、每个中间目录与叶节点都不得通过 symlink、hardlink、junction、mount escape 或 reparse point 跳出模块拥有的存储树。检查必须覆盖 TOCTOU：不得只在打开前检查一次字符串或文件属性，再让普通路径 API 在实际操作时跟随已经替换的链接。

本 ADR 冻结“不跟随链接、操作时保持 confinement、发现不可信路径即失败关闭”的结果，不规定 `openat2`、handle-relative API 或某个跨平台库。路径和权限诊断不得输出文件内容、credential、完整 `ObjectKey` 或不必要的绝对 root。

### 7. Local durable staging 与 atomic no-overwrite promote

tus staging 成功推进 durable offset 前，已确认的字节必须按支持平台的持久化语义落盘；只进入 page cache 不能宣称 durable。

Local promote 的成功含义固定为：已验证 staging 在同一 filesystem 内以 atomic、no-overwrite rename 建立 final；文件内容和必要目录元数据已经按支持平台的 crash-durability 能力持久化；随后按同一 `ObjectKey` 回读 final，并再次验证实际 size 与 canonical SHA-256。

final 已存在且实际 size/canonical checksum 与本次提交一致时，按 ADR 0023 视为同一提交的幂等收敛；不一致时失败关闭，不覆盖、不截断未知对象。provider 只有在 final 回读复验通过后，才能向 application 报告可继续 Tx2；`ITusStore` 或 provider 自身不得标记 session completed。

进程可能在 staging write、file flush、rename、目录元数据持久化或 final 回读复验任一边界崩溃。重启后只能收敛为以下之一：保留可继续的 staging；保留一致 final 等待 Tx2；明确报告冲突并保留恢复证据。不得开放半文件，也不得删除唯一恢复副本。

进程崩溃语义不等于掉电或底层文件系统保证。后续实现必须按受支持 OS/filesystem 组合分别给出 API 级与故障注入证据，不能用一句 `fsync + rename` 代替跨平台证明。该部署支持矩阵不是 provider 可选能力矩阵，由 #1012 与 deployment baseline 共同维护；未列入矩阵或尚未通过 capability/preflight 证明的组合必须 startup blocked，不能从相似平台结果推定支持。

### 8. 启动、运行期与容量健康状态分离

Local provider 必须区分三类状态：

1. **startup blocked**：root 缺失或未初始化、非持久、不可 canonicalize、identity/mount 不符、staging/final 跨 filesystem、不可读写或 provider 配置无效时，非 Development 服务不进入 ready。
2. **runtime critical / unready**：mount 丢失或被替换、root 变成只读、identity 漂移或已有 final 不可读时，停止新写入并明确使依赖健康检查失败；不得把替代空目录当成新存储。
3. **capacity restricted / degraded**：free bytes 或 inode 低于受配置治理的 emergency reserve 时进入 degraded，但 read/control plane 仍 ready；具体动作按下表准入，使系统仍可读取、核对和自救。

capacity restricted 的动作准入固定如下；“允许”只表示容量状态本身不拒绝，仍须通过业务授权、identity/confinement、完整性和并发栅栏等既有检查：

| 动作 | capacity restricted | 约束 |
| --- | --- | --- |
| create upload session | 拒绝 | 不建立新上传意图 |
| tus PATCH / 新增 staging bytes | 拒绝 | 包括已存在 `open` session 的后续写入 |
| 已进入 `committing` 的 Local promote | 允许 | 仅限已冻结 intent、同 filesystem atomic no-overwrite rename；不得退化为内容 copy |
| final verify / download | 允许 | 只读并继续执行授权与完整性检查 |
| 离线迁移 source read / verify | 允许 | 仅 source 读取与证据生成 |
| backup read | 允许 | 不含向当前 provider 回写字节 |
| GC / final delete | 允许 | 仍须满足生命周期与删除授权 |
| abort / expiry staging cleanup | 允许 | 仍归 `ITusStore`，且不得删除唯一恢复副本 |
| 离线迁移 target copy / 新 target bytes | 拒绝 | source 可读不代表 target 可写 |
| restore write | 默认拒绝；受控例外允许 | 仅 FileStorage 停服恢复模式、容量 preflight 通过并有显式 operator override 时允许 |

runtime critical/unready 的优先级高于该表。mount 丢失/替换、root 只读、identity 漂移、confinement 不可信或 final 不可读等 critical 状态必须使服务 unready，并覆盖 capacity restricted 下所有“允许”动作；在后端身份与安全性重新可信前，不得继续 read、rename、delete、cleanup 或 restore write。

健康输出只能暴露 provider 类型、状态类别和容量/身份的脱敏摘要，不得输出完整 root、对象 key、endpoint credential 或文件内容。capacity 阈值与用量计数由 #1018 实施；Local free bytes、inode、read-only 和 mount identity 探测由 #1012 实施。

### 9. `VersionedArchive` 保持独立 MinIO-only 边界

`VersionedArchive` 是既有 MinIO-only 合规归档子系统，拥有 versioning、version id、object lock 和 legal hold 语义；它不参与通用文件 LocalFileSystem / S3-compatible 二选一。通用 `IStorageProvider` 不吸收其 API、bucket、版本或合规能力。

两者可以共用底层 `IMinioClient` 注册、endpoint/credential 配置与 secret 注入机制，但接口、bucket、生命周期和失败语义保持分离。选择 Local 作为通用文件 provider 不能把 `VersionedArchive` 改成 Local，也不能静默关闭其独立 MinIO 配置。

通用文件的 Local/S3 provider selector 与 `VersionedArchive` 的 archive selector/configuration 是两个独立选择轴；共享 client、endpoint 或 credential 不等于共享 selector，也不得让其中一个 selector 的默认值隐式改变另一个。#997 承接通用 selector 的 DI/config 迁移以及它与 archive selector 的组合测试；未显式且无歧义选定通用 active provider 的运行实例仍按本 ADR startup blocked。

本 ADR 不修改 `VersionedArchive` 的 API、代码或行为。归档桶的搬迁与备份恢复分别由 #1013、#1005 和父票后续层治理。

## 健康与失败矩阵

| 场景 | 必须结果 | 禁止结果 |
| --- | --- | --- |
| 非 Development 未选择 provider 或配置无效 | startup blocked，诊断只暴露脱敏配置状态 | 回退 `server-proxy`、temp 或另一 provider |
| Local root 未显式配置或不是持久位置 | startup blocked | 使用系统 temp、cwd 或容器可写层 |
| identity marker 缺失、冲突或布局版本不匹配 | startup blocked；由显式初始化/修复流程处理 | 自动认领目录并继续服务 |
| volume 未挂载而同名空目录出现 | startup blocked 或 runtime critical/unready | 把空目录当成新存储并返回“文件不存在” |
| root/mount 在运行中被替换或变只读 | runtime critical/unready，覆盖 capacity allow 并阻止全部 provider I/O | 在未知后端继续 read、PATCH、promote、cleanup、restore 或删除 |
| staging/final 不在同一 filesystem | startup blocked | copy + delete 冒充 atomic Local promote |
| `ObjectKey` 逃逸、link/reparse 或 TOCTOU 风险 | 在任何字节操作前失败关闭 | 跟随到受控树外读、写、覆盖或删除 |
| durable offset 前进但字节未持久化 | 操作失败或不推进 durable offset | 重启后 offset 大于实际字节长度 |
| rename 前崩溃 | 保留可验证 staging；按 ADR 0023 intent 恢复 | 产生 completed/available final |
| rename 可能成功但 Tx2 未提交 | 保留 `committing` 与一致 final，回读复验后继续 | 重新开放 PATCH、覆盖 final 或删除唯一副本 |
| final 已存在且 size/checksum 一致 | 幂等收敛并继续 final 复验 | 创建第二个 final |
| final 已存在但内容冲突 | 失败关闭并保留脱敏诊断 | 覆盖、截断或接受冲突对象 |
| free bytes/inode 低于 emergency reserve | capacity restricted/degraded；read/control ready，并严格按动作准入矩阵执行 | 全面停服，或继续 create/PATCH/target copy |
| 健康诊断输出 | 只输出 provider 与状态类别、脱敏容量/身份摘要 | 输出 credential、完整 root、`ObjectKey` 或文件内容 |

## 后续层接口

### 第 3 层：离线迁移契约与 runbook

第 3 层可以依赖本 ADR 冻结的前提：FileStorage 运行时只有一个 active provider；Local 与 S3-compatible 使用同一 canonical、opaque relative `ObjectKey` 语义；provider 具有 Head、OpenRead、写入/建立和复验证据；Local 具有稳定 storage identity/config fingerprint 与容量 preflight；`VersionedArchive` 保持独立 MinIO 边界。

本 ADR 不定义停服、manifest、copy、逐对象 checksum、断点/失败清单、配置切换、起服抽样、回退或 source 清理步骤，也不证明这些操作已经可运行。

迁移涉及的 version id/evidence remap 仍属于第 3 层；本 ADR 不决定或展开其映射机制。

### 第 4 层：周边文档同步

第 4 层在前三层裁决稳定后同步 FileStorage baseline、database schema catalog、deployment baseline、implementation readiness 等旧叙述。本 ADR 只记录目标与当前冲突，不修改这些文件，也不把目标写成当前交付状态。

## 已考虑的替代方案

1. **继续把 tus、server-proxy 和 S3 当作同一类 Upload Provider。** 拒绝。ADR 0023 已将协议、入口拓扑和字节后端分轴；继续混用会让 complete 证据按传输分叉。
2. **按文件或用途动态选择 Local/S3。** 拒绝。它会重新引入 provider registry、多 placement、运行期共存和迁移期间读写路由，超出单机二选一目标。
3. **Local 未配置时回落系统 temp，或看到空目录就自动初始化。** 拒绝。temp 和容器可写层不持久；自动认领空目录无法区分首次安装与 volume 丢失。
4. **只做字符串清理或一次性 realpath 检查。** 拒绝。它不能阻止 symlink/junction/reparse 替换带来的 TOCTOU 路径逃逸。
5. **允许 Local 跨 filesystem copy + delete。** 拒绝。它不具备 atomic rename 语义，会扩大半文件、重复字节与崩溃恢复窗口。
6. **用 ETag、客户端 checksum 或单一布尔结果代表完整性。** 拒绝。它们不能提供统一 canonical SHA-256，也不能区分幂等一致、内容冲突与可重试失败。
7. **把 `VersionedArchive` 合并进通用 `IStorageProvider`。** 拒绝。归档接口的 versioning、version id 与 legal hold 会把已从通用文件面排除的合规能力重新引入。

## 后果

1. 通用文件的上传协议与字节后端边界清晰：`ITusStore` 管 staging，`IStorageProvider` 管 final，application/数据库管业务事实。
2. Local 和 S3-compatible 必须满足同一 complete 结果语义；实现代价高于按 provider 特判，但可以让上层状态机、下载和后续离线工具保持 provider 无关。
3. Local 成为目标生产选项后，安装必须显式初始化持久 root，并维护 storage identity、mount 与容量健康；这增加部署前置，但避免静默落 temp 或把丢失挂载误判成空存储。
4. atomic no-overwrite rename、路径 confinement 和跨平台 crash-durability 需要平台特定实现与故障注入，不能由通用路径 API 或单元测试假定已经满足。
5. capacity restricted 保留读与自救操作，运维状态不再等同于简单 healthy/unhealthy 布尔值。
6. `VersionedArchive` 与通用文件只共享最底层 MinIO 接线，防止合规语义泄漏；选择 Local 仍可能需要为归档子系统单独运行 MinIO。
7. 本 ADR 与当前 FileStorage baseline、schema catalog、deployment baseline、implementation readiness 及运行时代码暂时并存冲突；机械同步与实现证明必须由后续独立层完成。
