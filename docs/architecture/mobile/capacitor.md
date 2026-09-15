# PDA / Capacitor 当前架构

本文只描述 `business-pda` 的**当前运行时边界、Web/native 分层、设备桥接、离线同步、安全与配置架构**。产品页面、角色任务和 UX 见 [`../../product/mobile-pda/design.md`](../../product/mobile-pda/design.md)；构建/APK/部署见 [`../../runbooks/mobile-pda-deployment.md`](../../runbooks/mobile-pda-deployment.md)；测试证明边界见 [`../../governance/testing/mobile-pda.md`](../../governance/testing/mobile-pda.md)。M2-L 清理前的混合正文冻结于 [`../../reports/m2-l-mobile-pda-capacitor-architecture-pre-clean-2026-09-07.md`](../../reports/m2-l-mobile-pda-capacitor-architecture-pre-clean-2026-09-07.md)。

精确 Capacitor/Android/Node 版本、插件集合和 package scripts 由 `frontend/apps/business-pda/package.json`、lockfile、`capacitor.config.ts` 和 Android producer 决定，Architecture 不复制版本清单。

## 运行时边界

```text
Vue business-pda app
  + Capacitor native runtime
  + Android device bridge
  + local durable offline state / outbox
  + BusinessGateway facade
```

PDA 是业务执行客户端，不是业务事实源。页面经稳定 API client 访问 BusinessGateway，不直连领域服务数据库、对象存储、消息 broker 或现场控制系统。

Android 是当前工业 PDA 的原生宿主。Web/PWA 只承担开发、演示或浏览器能力兜底；是否增加其它原生平台属于独立产品/架构决策，不由当前页面隐式扩展。

## Workspace 与依赖方向

`frontend/apps/business-pda` 是独立可运行 app，与 `business-console` 并列。它复用：

- `@nerv-iip/api-client`：BusinessGateway 公开客户端；
- `@nerv-iip/ui-mobile`：触摸/PDA 密度 UI；
- `@nerv-iip/business-core`：跨 PC/PDA 的无页面业务类型、SOP、CodeSet 和命令构造；
- 需要时复用 app-agnostic auth/session 能力。

PDA 不复用桌面 `AppShell` 页面 chrome，也不从 `business-console` 页面目录取组件。跨 app 的真实复用才提升到 package；PDA 专属 native/offline 行为保持在 app 侧，除非出现多个移动 app/厂商实现的稳定复用压力。

工作区总边界见 [`../frontend/workspace-structure.md`](../frontend/workspace-structure.md)。

## Device Bridge

页面和业务 composable 只依赖 TypeScript device facade，不直接调用厂商 SDK。桥接层统一处理：

1. 硬件扫码、相机扫码和 Android intent；
2. 设备身份、网络与电量/诊断事实；
3. 蜂鸣/震动/反馈；
4. 打印、RFID 或其它厂商能力；
5. native 生命周期和后台同步触发。

厂商 adapter（例如 DataWedge、Honeywell SDK）实现同一语义接口；页面不得通过厂商品牌分支业务流程。硬件输入需携带来源与时间等必要元数据，但原始控制凭据或密钥不得进入 Web 层。

## 扫码边界

扫描结果先形成受控的客户端 scan intent，再由当前页面/SOP 解释为业务动作。设备桥只负责采集与标准化，不判断库存移动、质量放行、工单报工等领域规则。

同一扫描输入可能来自硬件 intent、摄像头或键盘 wedge；所有来源必须收敛为同一业务解析路径。重复扫描、页面切换和系统 resume 不得绕过幂等/防抖语义。

## 离线与同步

离线能力遵循 local-first execution buffer，而不是“客户端成为主库”：

```text
server task snapshot
  -> local task/cache
  -> user action / draft
  -> durable local outbox
  -> network available
  -> BusinessGateway command
  -> server result
  -> local acknowledgement / projection refresh
```

关键规则：

1. 需要跨进程/重启保留的任务、草稿、outbox 使用 durable local store；浏览器级临时存储不能承担关键离线事实。
2. 每个写操作意图只生成一次稳定 idempotency key / client operation id，网络重试复用同一键。
3. outbox 项必须能区分 queued、sending、acknowledged、business-rejected 和 retryable failure，不以“HTTP 超时”等同于服务端未提交。
4. 服务端领域 owner 决定最终事实；客户端冲突时展示可恢复状态或要求重新同步，不直接覆盖服务端事实。
5. 离线缓存按 organization/environment/user/device 隔离；scope 或身份变化时不得跨上下文复用旧队列。

## Gateway 与认证

PDA 只消费 BusinessGateway facade 和稳定 `api-client` 导出。Gateway 负责终端用户认证、IAM permission/scope enforcement、organization/environment 上下文和内部服务身份转换。

客户端可以根据 permission/scope 裁剪 UI，但不能把本地 role/permission 缓存当授权边界。token、refresh/session、设备注册或 managed configuration 等敏感事实不得写入普通日志；需要设备侧安全存储时使用原生安全能力而不是 bundle 常量。

## 配置

生产地址、设备 profile、组织/环境默认值、同步策略和日志级别来自受控运行配置或企业设备管理；不得硬编码进业务组件。构建时 API base URL 与 Android cleartext/debug 分叉属于部署操作，统一从 Runbook 和当前配置 producer 读取。

## Native 与 Web 的职责

| 层 | 负责 | 不负责 |
| --- | --- | --- |
| Vue 页面/SOP | 作业编排、输入校验、状态展示、业务命令意图 | 厂商 SDK、后台服务、领域最终授权 |
| Device facade | 标准化扫描/设备/反馈/打印等能力 | WMS/MES/Quality 领域规则 |
| Android adapter/plugin | intent、权限、厂商 SDK、原生生命周期 | 页面 IA、业务事实所有权 |
| Offline layer | cache/draft/outbox、同步状态 | 覆盖服务端领域事实 |
| BusinessGateway | authn/authz、上下文、facade/OpenAPI | 本地设备 SDK 与领域持久事实 |

## Fail-closed 不变量

1. native 插件不可用时必须显式降级或阻断需要该能力的动作，不能伪造成功扫描/打印。
2. 客户端断网或请求超时后，不重建幂等键进行写操作重试。
3. organization/environment/user/device 上下文不完整时不提交离线写队列。
4. 设备桥不得嵌入长期服务密钥、数据库连接或对象存储密钥。
5. 模拟器/浏览器证明不能替代要求真实硬件能力的真机证明；证明术语由 testing governance 约束。
