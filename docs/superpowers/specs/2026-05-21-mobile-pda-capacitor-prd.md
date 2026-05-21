# Mobile PDA Capacitor PRD

本文档定义 Nerv-IIP 移动端 PDA 应用的产品需求、范围边界、验收标准和阶段规划。技术选型结论见 [mobile-pda-capacitor-architecture.md](../../architecture/mobile-pda-capacitor-architecture.md)。

## 结论

Capacitor 可以作为 Nerv-IIP 移动端 PDA 开发的推荐选型，但推荐口径是“Android PDA 优先的 Web Native 应用”，不是普通 PWA，也不是单纯把 Console 页面塞进 WebView。

选择 Capacitor 的前提条件：

1. PDA 首批目标是 Android 工业手持终端，业务以扫码、收发货、盘点、报工、质检、点检和维修为主。
2. 前端继续复用 Nerv-IIP 已冻结的 Vue、TypeScript、Pinia、Pinia Colada、Hey API 和 UI token 体系。
3. 硬件能力通过 Capacitor 原生插件或厂商 intent/SDK 桥接，不要求纯 Web API 覆盖扫描头、RFID、打印、蓝牙外设、串口或 MDM 配置。
4. 离线能力按本地数据库、outbox、幂等键和服务端冲突处理设计，不依赖 WebView 的 LocalStorage 或普通 IndexedDB 作为唯一事实源。
5. 团队接受维护少量 Android Kotlin/Java 原生桥接代码，并建立真实 PDA 设备测试矩阵。

不推荐 Capacitor 的场景：

1. 首批需求是重度实时 3D、复杂图像处理、持续后台定位、深度系统管控或高频工业控制。
2. 必须完全无原生代码团队。
3. 必须支持 Android 7 以下设备，或客户 PDA 无法保证 Android System WebView/Chrome 可用。
4. 目标是现场设备控制闭环，而不是人工作业采集与业务执行。

## 产品定位

Mobile PDA 是 Nerv-IIP 面向仓库、产线、质检和维修一线人员的移动执行入口。它承接业务平台的 WMS、MES、Inventory、Quality、BarcodeLabel、Maintenance 等领域能力，通过扫码和离线执行提升现场作业效率。

Mobile PDA 不替代主平台 Console。Console 面向管理员、计划员、主管和平台运维人员；PDA 面向现场执行人员，优先保障单手操作、低干扰、弱网可用和高频扫码。

## 背景与问题

当前业务平台架构已经定义 WMS、MES、Inventory、BarcodeLabel、Quality、Maintenance 等链路。桌面 Console 可以完成管理、配置、查询和审批，但现场执行有不同约束：

1. 作业发生在货架、月台、产线、设备旁，不能依赖桌面终端。
2. 操作高频、重复、短链路，页面必须围绕扫码结果推进，而不是围绕表格浏览。
3. 仓库和车间存在 Wi-Fi 盲区，必须支持离线暂存、恢复同步和失败补偿。
4. PDA 设备通常带硬件扫描头、物理按键、蜂鸣、震动、蓝牙打印、RFID 或厂商管理能力，普通浏览器无法稳定覆盖。
5. 工业现场需要按组织、环境、仓库、工位和设备绑定权限与审计，不能让扫码数据绕过平台授权。

## 目标

1. 提供 Android PDA 应用，覆盖 WMS/MES/Inventory/Quality/Maintenance 首批现场作业。
2. 支持硬件扫描头和摄像头扫描 fallback，并把扫描结果归一化为业务可消费事件。
3. 支持弱网和短时离线，关键作业先写入本地事实源，再通过 outbox 同步到平台。
4. 复用现有 Gateway、IAM、OpenAPI codegen、业务权限矩阵和平台观测能力。
5. 建立可扩展设备适配层，为 Zebra、Honeywell、Urovo/东集、Seuic/销邦、Newland/新大陆等 PDA 厂商保留插拔式适配空间。
6. 用可验证的业务指标评估上线价值，而不是只完成移动壳层。

## 非目标

1. 不在首批实现完整移动 Console 或所有后台管理页面。
2. 不在 PDA 内直接连接数据库、对象存储、消息队列或领域服务内部 URL。
3. 不让 PDA 直接控制 PLC/DCS/SCADA、WCS、AGV、AMR 或设备运动。
4. 不把 PDA 当作 Connector Host；PDA 只代表已登录用户和已登记设备执行现场作业。
5. 不在首批提供 iOS PDA 同等能力；iOS 可以作为手机审批/查看类扩展，但不是工业 PDA MVP。
6. 不承诺所有厂商扫描 SDK 零代码兼容；必须按设备型号做验收。

## 角色

| 角色 | 主要场景 | 权限特征 |
| --- | --- | --- |
| 仓库收货员 | 到货扫码、收货、质检送检、入库上架 | WMS 收货、库存查看、扫码写入。 |
| 仓库拣货员 | 出库拣货、复核、包装、发运交接 | WMS 出库、库存占用查看、扫码写入。 |
| 仓库盘点员 | 盘点任务、库位扫描、差异记录 | 库存盘点、扫码写入、照片附件。 |
| 产线操作员 | 工单开工、物料领用、工序报工、完工入库申请 | MES 工单、报工写入、条码校验。 |
| 质检员 | 来料检验、过程检验、成品检验、不合格记录 | Quality 检验读写、附件上传。 |
| 维修技师 | 点检、故障上报、维修工单、备件领用 | Maintenance 工单、库存领料、设备查看。 |
| 班组长/仓库主管 | 任务分派、异常处理、离线同步状态查看 | 现场任务管理、异常审批入口。 |
| 平台管理员 | 设备登记、应用分发、版本灰度、远程配置 | IAM/设备管理/MDM 配置。 |

## 典型设备

首批以 Android rugged PDA 为目标，设备能力按“必须、推荐、可选”分层。

| 能力 | 等级 | 说明 |
| --- | --- | --- |
| Android 8+，推荐 Android 10+ | 必须 | Capacitor v8 最低支持 Android 7/API 24，但工业部署应留出 WebView、MDM 和安全补丁余量。 |
| Chrome 或 Android System WebView 可更新 | 必须 | 避免老旧 WebView 导致空白页、兼容性或安全风险。 |
| 1D/2D 硬件扫描头 | 必须 | 支持 Code128、QR、DataMatrix、EAN13、PDF417、GS1 等常用码制。 |
| 物理扫描键 | 必须 | 左右侧键或扳机键，支持连续扫描。 |
| 蜂鸣/震动/指示灯 | 推荐 | 扫描成功、失败、重复、离线写入等状态反馈。 |
| Wi-Fi 5/6 和 4G/5G | 推荐 | 仓库/车间弱网切换。 |
| 蓝牙打印机或标签打印接口 | 推荐 | 标签补打、容器标签、库位标签。 |
| RFID/UHF | 可选 | 批量识别或特定行业场景后续扩展。 |
| MDM/OEMConfig/Managed Google Play | 推荐 | 企业分发、远程配置、锁定、版本灰度。 |

## 核心用户旅程

### 1. 设备发放与登录

1. 管理员在平台登记 PDA 设备或通过 MDM 下发设备配置。
2. 员工打开 PDA app，扫描组织/环境绑定码或读取 managed configuration。
3. 员工登录，选择组织、环境、仓库、工位或班组。
4. App 拉取本人待办任务、基础字典、条码规则和离线策略。
5. App 展示当前在线状态、登录用户、设备编号和待同步数量。

### 2. 扫码驱动作业

1. 用户进入作业模式，例如收货、上架、拣货、报工或点检。
2. 用户按物理扫描键扫描单据、物料、容器、库位或设备。
3. App 将扫描事件归一化为 `ScanEvent`，根据当前作业上下文解释其业务含义。
4. App 给出即时反馈：成功、重复、超量、错库位、错工单、无权限、离线暂存。
5. App 生成本地操作草稿或业务提交请求。
6. 在线时立即同步；离线时进入 outbox 并显示待同步状态。

### 3. 弱网离线执行

1. App 检测网络中断或 Gateway 不可达。
2. 可离线执行的任务继续允许录入，但页面明确显示离线状态。
3. 所有离线写操作带幂等键、业务来源、客户端时间、设备 ID 和用户 ID。
4. 网络恢复后自动同步，失败项进入异常队列。
5. 冲突项由页面展示原因，允许用户重试、撤销、改为主管处理或生成异常单。

### 4. 现场异常处理

1. 用户扫码发现物料不符、库位不符、数量超差、条码无效、设备不可用。
2. App 在当前作业内创建异常记录，并允许拍照或录入备注。
3. App 触发平台 Notification 或业务审批，不直接绕过领域规则。
4. 主管在 Console 或 PDA 主管视图处理异常。
5. 作业继续、挂起或关闭，均保留审计。

## MVP 范围

MVP 以 WMS + Inventory + BarcodeLabel 为主，补充最小 MES 报工入口。

| 模块 | MVP 能力 | 说明 |
| --- | --- | --- |
| 登录与上下文 | 登录、组织/环境选择、仓库/工位选择、设备标识展示 | 复用 IAM 与 Gateway。 |
| 扫码基础设施 | 硬件扫描、摄像头扫描 fallback、扫码历史、重复扫描识别 | 优先 Zebra DataWedge intent + keyboard wedge。 |
| 任务中心 | 本人待办、最近作业、离线待同步、异常队列 | 统一现场入口。 |
| WMS 收货 | 扫采购单/到货单、扫物料、录数量、提交收货 | 产生 WMS/Inventory 受控动作。 |
| WMS 上架 | 扫入库单、扫容器/物料、扫库位、提交上架 | 校验库位、批次、数量。 |
| WMS 拣货 | 扫出库单、扫库位、扫物料/容器、确认拣货 | 支持差异和短拣。 |
| WMS 复核 | 扫出库单、复核物料、包装确认 | 作为出库前质量闸口。 |
| 库存盘点 | 扫盘点任务、扫库位、扫物料、录实盘数量 | 支持离线草稿和差异提交。 |
| 标签补打 | 扫物料/容器/库位，选择模板，触发打印 | 首批可通过服务端生成 PDF/标签数据。 |
| MES 报工 Lite | 扫工单/工序、录合格数/不良数、提交报工 | 不覆盖完整排产和工艺指导书。 |
| 离线同步 | 本地草稿、outbox、同步状态、失败重试 | 写操作必须可追踪。 |

## 后续范围

| 阶段 | 能力 | 说明 |
| --- | --- | --- |
| Phase 2 | Quality 检验、拍照附件、检验值采集 | 对接 File Storage 和 Quality。 |
| Phase 3 | Maintenance 点检、故障上报、维修工单、备件领用 | 对接 Maintenance、Inventory、IndustrialTelemetry。 |
| Phase 4 | RFID/UHF、蓝牙/网络标签打印、电子签名 | 需要设备型号验证。 |
| Phase 5 | 主管移动视图、异常审批、移动消息中心 | 复用 Notification 和 BusinessApproval。 |
| Phase 6 | 多厂商设备深度适配、MDM managed configuration、OEMConfig | 按客户设备池推进。 |

## 功能需求

### PDA-AUTH

| ID | 需求 | 优先级 | 验收 |
| --- | --- | --- | --- |
| PDA-AUTH-001 | App 必须通过 PlatformGateway 登录，不直连 IAM。 | P0 | 抓包确认所有浏览器请求只访问 Gateway 公开域名。 |
| PDA-AUTH-002 | 登录后必须显示当前组织、环境、仓库/工位和用户。 | P0 | 切换环境后任务和权限随上下文刷新。 |
| PDA-AUTH-003 | App 必须支持 refresh token rotation 和主动退出。 | P0 | 退出后本地敏感 token 清除，受保护接口不可访问。 |
| PDA-AUTH-004 | 设备可通过扫码或 managed configuration 绑定默认 Gateway、组织和环境。 | P1 | 清机重装后可由管理员快速恢复配置。 |
| PDA-AUTH-005 | 长时间离线时可继续打开已授权离线任务，但禁止执行超出离线授权窗口的新任务。 | P1 | 离线授权过期后写入被阻止并提示重新联网。 |

### PDA-SCAN

| ID | 需求 | 优先级 | 验收 |
| --- | --- | --- | --- |
| PDA-SCAN-001 | App 必须接收硬件扫描头输入。 | P0 | Zebra DataWedge intent 模式和 keyboard wedge fallback 至少通过一种。 |
| PDA-SCAN-002 | 扫描结果必须归一化为统一事件模型。 | P0 | 事件包含 rawValue、symbology、source、timestamp、deviceId、operationContext。 |
| PDA-SCAN-003 | App 必须支持摄像头扫码 fallback。 | P1 | 无硬件扫描头的 Android 设备可完成基础扫码。 |
| PDA-SCAN-004 | App 必须对重复扫描、错码制、空值和不可解析条码给出明确反馈。 | P0 | 反馈在 300ms 内出现，且不会产生重复提交。 |
| PDA-SCAN-005 | App 必须支持连续扫描模式。 | P1 | 连续扫描 100 次不丢事件、不乱序、不冻结 UI。 |
| PDA-SCAN-006 | 扫描事件必须可审计。 | P1 | 提交到服务端的作业包含扫码来源和客户端幂等键。 |

### PDA-TASK

| ID | 需求 | 优先级 | 验收 |
| --- | --- | --- | --- |
| PDA-TASK-001 | 任务中心展示本人可执行任务、最近任务和异常任务。 | P0 | 用户只看到授权范围内任务。 |
| PDA-TASK-002 | 任务必须支持按单据号、物料、库位、容器码搜索。 | P1 | 弱网下优先搜索本地缓存。 |
| PDA-TASK-003 | 每个任务页面必须展示当前步骤、已扫数量、目标数量和错误状态。 | P0 | 不依赖说明文字即可完成主流程。 |
| PDA-TASK-004 | 用户可挂起任务并返回任务中心。 | P1 | 草稿保留，恢复后上下文正确。 |

### PDA-WMS

| ID | 需求 | 优先级 | 验收 |
| --- | --- | --- | --- |
| PDA-WMS-001 | 收货流程支持扫单据、扫物料、录数量、提交收货。 | P0 | 服务端生成收货事实，不由 PDA 直接改库存。 |
| PDA-WMS-002 | 上架流程支持扫入库单、扫容器/物料、扫库位、提交上架。 | P0 | 错库位、错物料、超量必须阻止提交。 |
| PDA-WMS-003 | 拣货流程支持扫出库单、扫库位、扫物料/容器、确认拣货。 | P0 | 短拣必须记录原因。 |
| PDA-WMS-004 | 复核流程支持按出库单复核物料、数量和包装。 | P1 | 不一致项进入异常队列。 |
| PDA-WMS-005 | 盘点流程支持库位盘、任务盘和差异提交。 | P0 | 离线盘点恢复联网后可同步，冲突明确展示。 |
| PDA-WMS-006 | WMS 写入必须带幂等键。 | P0 | 同一幂等键重复提交不会重复入账。 |

### PDA-MES

| ID | 需求 | 优先级 | 验收 |
| --- | --- | --- | --- |
| PDA-MES-001 | 支持扫工单和工序进入报工页面。 | P1 | 工单未释放、无权限、状态不符时阻止进入。 |
| PDA-MES-002 | 支持录入合格数、不良数、工时和备注。 | P1 | 数量规则由 MES 服务端校验。 |
| PDA-MES-003 | 支持扫物料执行工序投料确认。 | P2 | 错料和超量必须提示。 |

### PDA-QUALITY

| ID | 需求 | 优先级 | 验收 |
| --- | --- | --- | --- |
| PDA-QUALITY-001 | 支持按检验任务录入检验值、结论和不合格原因。 | P2 | 检验结果写入 Quality，不直接改库存。 |
| PDA-QUALITY-002 | 支持拍照附件。 | P2 | 附件通过 File Storage 上传并关联检验记录。 |

### PDA-MAINT

| ID | 需求 | 优先级 | 验收 |
| --- | --- | --- | --- |
| PDA-MAINT-001 | 支持扫设备码查看点检/维修任务。 | P2 | 设备资产事实来自 MasterData。 |
| PDA-MAINT-002 | 支持故障上报、维修记录和备件领用。 | P2 | 备件领用通过 Inventory 受控移动。 |

### PDA-OFFLINE

| ID | 需求 | 优先级 | 验收 |
| --- | --- | --- | --- |
| PDA-OFFLINE-001 | App 必须提供本地数据库保存任务快照、字典、条码规则、草稿和 outbox。 | P0 | 杀进程重启后数据仍可恢复。 |
| PDA-OFFLINE-002 | 离线写入必须进入 outbox，并展示待同步数量。 | P0 | 飞行模式下可创建离线草稿，恢复网络后自动同步。 |
| PDA-OFFLINE-003 | 同步必须支持失败重试、指数退避和人工重试。 | P0 | 服务端 5xx 不丢数据，4xx 进入用户可见异常。 |
| PDA-OFFLINE-004 | 冲突必须可解释。 | P1 | 超量、状态过期、任务已关闭、权限变化等原因可见。 |
| PDA-OFFLINE-005 | 离线任务必须有有效期和最大缓存范围。 | P1 | 超过策略后阻止继续执行并要求联网刷新。 |

### PDA-DEVICE

| ID | 需求 | 优先级 | 验收 |
| --- | --- | --- | --- |
| PDA-DEVICE-001 | App 必须采集设备型号、系统版本、WebView 版本、App 版本、设备 ID。 | P0 | 诊断页面可查看并上传。 |
| PDA-DEVICE-002 | App 必须支持扫码、震动、蜂鸣或提示音反馈。 | P1 | 扫码成功/失败反馈可配置。 |
| PDA-DEVICE-003 | App 必须支持远程配置默认 Gateway、组织、环境、扫描模式。 | P1 | MDM 或扫码配置均可。 |
| PDA-DEVICE-004 | App 必须提供诊断导出。 | P1 | 可导出最近日志、同步队列摘要和设备信息。 |

## 非功能需求

### 性能

| 指标 | 目标 |
| --- | --- |
| 冷启动到登录页 | Android 中端 PDA 小于 3 秒。 |
| 已登录进入任务中心 | 本地缓存可用时小于 1.5 秒。 |
| 扫码到 UI 反馈 | 目标小于 300ms，离线本地校验小于 150ms。 |
| 单任务连续扫描 | 100 次连续扫描不丢事件、不重复提交。 |
| 本地 outbox 容量 | MVP 支持至少 5000 条待同步操作。 |
| 单次同步批次 | 默认 50-200 条，按网络和服务端限流动态调整。 |

### 可用性

1. 任务页面必须适配 4-6 英寸 PDA 屏幕和横竖屏策略。
2. 高频按钮必须支持拇指区操作，避免误触。
3. 关键状态必须用颜色、图标、声音/震动组合表达，不能只依赖文本。
4. 弱网、离线、同步失败、权限变化必须有明确状态。
5. 条码输入框不应要求用户手动聚焦才能接收硬件扫描事件；keyboard wedge fallback 除外。

### 数据一致性

1. PDA 不拥有库存、工单、质检、维修或财务事实。
2. PDA 写入只表达“用户尝试完成某个现场动作”，最终业务事实由服务端领域模型确认。
3. 所有写请求必须携带 idempotencyKey、clientOperationId、deviceId、userId、organizationId、environmentId、occurredAt。
4. 服务端必须返回 accepted、rejected、conflict、duplicate 等明确结果。
5. PDA 必须保存同步结果，便于审计和追溯。

### 安全

1. App 不嵌入 API secret、数据库连接串、对象存储 key 或不可轮换凭据。
2. token 和设备密钥必须使用 Android Keystore 或安全存储插件，不保存到普通 LocalStorage。
3. 所有请求必须走 HTTPS。
4. OAuth/OIDC 场景必须使用 PKCE；深链回调不得携带敏感 token。
5. App 必须受 CSP、域名 allowlist 和 Gateway CORS 策略约束。
6. 设备丢失时必须支持服务端撤销会话或设备授权。
7. 本地离线数据必须有过期、清理和加密策略；客户有强安全要求时启用加密 SQLite。

### 可观测性

1. App 必须记录扫码事件、页面错误、同步队列、网络变化、接口错误、设备信息。
2. 客户端日志不得包含 token、密码、完整身份证件号或敏感附件内容。
3. 关键写入链路必须带 correlationId，贯穿 PDA、Gateway、业务服务和 IntegrationEvent。
4. 管理员可查看每台 PDA 的 App 版本、最近同步时间、失败计数和最近错误摘要。

## 数据模型草案

### ScanEvent

| 字段 | 说明 |
| --- | --- |
| eventId | 客户端事件 ID。 |
| rawValue | 原始扫描文本。 |
| symbology | 码制，例如 CODE128、QRCODE、DATAMATRIX。 |
| source | scanner、camera、keyboard、rfid。 |
| vendor | zebra、honeywell、urovo、unknown。 |
| rawBytesRef | 可选，原始字节或本地引用。 |
| scannedAt | 设备时间。 |
| deviceId | 平台登记设备 ID。 |
| userId | 当前用户。 |
| organizationId | 组织。 |
| environmentId | 环境。 |
| operationContext | 当前业务页面和任务上下文。 |

### LocalOperation

| 字段 | 说明 |
| --- | --- |
| clientOperationId | 客户端操作 ID。 |
| idempotencyKey | 服务端幂等键。 |
| operationType | wms.receipt、wms.putaway、inventory.count、mes.report 等。 |
| payload | 业务请求体。 |
| status | draft、pending、syncing、accepted、rejected、conflict、cancelled。 |
| retryCount | 重试次数。 |
| lastError | 最近错误摘要。 |
| createdAt/updatedAt | 客户端时间。 |
| serverResultRef | 服务端回执引用。 |

### DeviceProfile

| 字段 | 说明 |
| --- | --- |
| deviceId | 平台设备 ID。 |
| appInstallationId | 本次安装 ID。 |
| model/manufacturer | 设备型号和厂商。 |
| androidVersion | Android 版本。 |
| webViewVersion | WebView/Chrome 版本。 |
| appVersion/buildNumber | App 版本。 |
| scannerMode | datawedge-intent、keyboard-wedge、vendor-sdk、camera。 |
| defaultOrg/defaultEnv | 默认组织和环境。 |
| lastSeenAt | 最近心跳。 |

## API 需求草案

具体 OpenAPI 以后续业务服务实现为准，PDA 需要 Gateway mobile facade 提供以下聚合能力：

| 能力 | 方向 | 说明 |
| --- | --- | --- |
| `GET /api/mobile/v1/bootstrap` | 读 | 登录后拉取用户、组织、环境、仓库/工位、权限、设备策略。 |
| `GET /api/mobile/v1/tasks` | 读 | 拉取本人任务、最近任务、异常任务。 |
| `POST /api/mobile/v1/scans/interpret` | 写 | 在线解释条码含义，可缓存常用规则。 |
| `POST /api/mobile/v1/operations/batch` | 写 | 批量同步 outbox。 |
| `GET /api/mobile/v1/sync/delta` | 读 | 拉取任务、字典和规则增量。 |
| `POST /api/mobile/v1/devices/register` | 写 | 设备登记或安装实例登记。 |
| `POST /api/mobile/v1/diagnostics` | 写 | 上传诊断摘要。 |

Gateway facade 只做认证、授权、聚合、DTO 映射和错误归一化；领域不变式仍由 WMS、Inventory、MES、Quality、Maintenance 等服务判断。

## 权限需求

PDA 首批复用现有业务权限码：

| 场景 | 权限码 |
| --- | --- |
| 扫码记录 | `business.barcodes.scans.write` |
| 标签打印 | `business.barcodes.print` |
| 库存查看 | `business.inventory.ledger.read` |
| 库存移动 | `business.inventory.movements.create` |
| 盘点 | `business.inventory.counts.manage` |
| WMS 收货/上架 | `business.wms.receipts.read`、`business.wms.receipts.manage` |
| WMS 拣货/复核 | `business.wms.shipments.read`、`business.wms.shipments.manage` |
| MES 工单/报工 | `business.mes.work-orders.read`、`business.mes.reporting.write` |
| 质检 | `business.quality.inspections.read`、`business.quality.inspections.manage` |
| 维修 | `business.maintenance.work-orders.read`、`business.maintenance.work-orders.manage` |

后续如果引入平台级设备台账，应在 [authorization-matrix.md](../../architecture/authorization-matrix.md) 中新增 `mobile.devices.read`、`mobile.devices.manage`、`mobile.diagnostics.write` 等权限码，再进入 IAM seed 和 Gateway enforcement。

## 成功指标

| 指标 | MVP 目标 |
| --- | --- |
| 扫码成功反馈时间 | P95 小于 300ms。 |
| 收货/上架/拣货作业效率 | 相比纸面或桌面录入减少 30% 以上耗时。 |
| 离线数据丢失 | 0。 |
| 重复提交导致重复库存入账 | 0。 |
| 同步失败可解释率 | 95% 以上失败项能显示明确原因。 |
| 首批设备兼容 | 至少 2 个实际 PDA 型号通过完整验收，其中至少 1 个 Zebra 或同等级 DataWedge/intent 型号。 |
| 现场培训时间 | 常规收货/拣货用户 30 分钟内能独立完成主流程。 |

## 验收标准

### 业务验收

1. 仓库人员可使用 PDA 完成收货、上架、拣货、复核和盘点主流程。
2. PDA 离线后仍可完成已缓存任务的录入，恢复网络后自动同步。
3. 服务端拒绝的业务操作能在 PDA 上明确展示原因。
4. Console 或服务端能看到 PDA 提交的业务事实、审计信息和异常记录。
5. 现场主管能识别未同步、同步失败和冲突任务。

### 技术验收

1. Android 真机安装包通过签名构建，至少支持测试、预生产、生产环境配置。
2. Zebra DataWedge intent 或同等级硬件扫描模式通过真机测试。
3. 摄像头扫码 fallback 可用。
4. 本地 SQLite/outbox 在杀进程、重启、断网、弱网、重复提交场景下通过测试。
5. Gateway mobile facade 的 OpenAPI 进入 api-client 生成链路。
6. 权限码在 authorization matrix、IAM seed、Endpoint enforcement 和测试中一致。
7. 客户端不包含明文 secret，token 不写入 LocalStorage。
8. App 版本、设备信息和同步错误可观测。

## 发布计划

### Milestone 0. 技术验证

目标：验证 Capacitor + Vue + Android PDA 硬件扫描 + 本地 outbox 的可行性。

交付：

1. `frontend/apps/pda` 技术样机。
2. Zebra DataWedge intent adapter 或目标客户 PDA 的厂商 adapter。
3. 摄像头扫码 fallback。
4. 本地 SQLite/outbox 原型。
5. 与 Gateway mock/mobile facade 的一次端到端提交。

### Milestone 1. WMS MVP

目标：交付收货、上架、拣货、复核、盘点。

交付：

1. PDA 登录、上下文、任务中心。
2. WMS 核心作业页面。
3. 离线同步和异常队列。
4. 真实业务服务 OpenAPI 和权限 enforcement。
5. 2 个 PDA 型号验收。

### Milestone 2. MES/Quality/Maintenance 扩展

目标：把 PDA 扩展到产线报工、质检和维修。

交付：

1. MES 报工 Lite。
2. Quality 检验和附件。
3. Maintenance 点检/维修。
4. 标签打印和设备诊断增强。

### Milestone 3. 企业化交付

目标：形成客户现场可规模部署的移动应用能力。

交付：

1. Android Enterprise/MDM 分发流程。
2. managed configuration 或扫码配置。
3. 版本灰度、强制升级、诊断包上传。
4. 多厂商设备适配矩阵。

## 风险与应对

| 风险 | 影响 | 应对 |
| --- | --- | --- |
| 厂商扫描 SDK/intent 差异大 | 扫码不稳定、适配成本上升 | 设备抽象层、真机矩阵、先支持 DataWedge/keyboard wedge，再按客户设备扩展。 |
| WebView 版本不可控 | 白屏、兼容性、安全风险 | 设备准入要求 WebView 可更新；MDM 锁定更新策略；启动时采集 WebView 版本并告警。 |
| 离线冲突复杂 | 库存或任务状态错乱 | 服务端保留事实源；PDA 只提交意图；所有写入带幂等键和版本。 |
| 原生后台同步受系统限制 | 离线队列不能及时同步 | 前台同步为主，必要时自定义 WorkManager 插件；关键任务要求用户保持 App 活跃直至同步完成。 |
| token 或离线数据泄露 | 安全事故 | Keystore/安全存储、本地加密、远程撤销、离线有效期。 |
| 页面复用桌面 Console 导致操作低效 | 现场体验差 | PDA 独立 app 和交互模型，只复用底层 token、API client 和设计变量。 |
| 过早支持太多业务域 | MVP 失焦 | 首批聚焦 WMS + Inventory + BarcodeLabel，MES/Quality/Maintenance 后续扩展。 |

## 待确认问题

1. 首批客户或内部验证设备型号、Android 版本、扫描输入模式和 MDM 能力。
2. 是否必须支持 UHF/RFID、蓝牙标签打印、串口秤或工业平板横屏。
3. 业务上允许离线执行的动作清单、离线授权时长和最大缓存任务数量。
4. WMS/MES/Quality/Maintenance 的首批 OpenAPI 是否按 mobile facade 聚合，还是先复用已有业务 Gateway。
5. 客户对本地数据加密、设备丢失远程擦除、私有化应用分发的合规要求。
