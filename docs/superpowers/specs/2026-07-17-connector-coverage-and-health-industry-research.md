# 连接器覆盖清单与健康状态：行业标准裁决

日期：2026-07-17

## 结论

1. **“覆盖的 tag 清单”应包含已配置、仍在目标范围内、但从未成功产生样本的 tag。** 这是一项基于标准结构的产品裁决，而不是标准对“coverage”一词的直接定义。清单应来自配置/声明事实，不能由历史样本反推；`firstSampleAtUtc`、`lastSampleAtUtc` 可以为空，并应同时显示启用状态、激活/订阅结果和最近错误。若只展示产生过样本的 tag，应明确命名为“已观测 tag”，不能称为完整覆盖。
2. **连接状态、collector health、sample freshness 必须分开建模。** 三者的证据、生命周期和故障含义不同；可以聚合成概览，但不能互相代替。

## 规范事实

### OPC UA

- Client 通过 `CreateMonitoredItems` 提交一组待监控项；Server 对每一项返回独立 `statusCode`，成功时再返回 `monitoredItemId` 和修订后的采样参数。因此“配置目标”“是否成功建立监控”本身就是独立于后续样本的事实。[OPC UA Part 4 §5.13.2.2](https://reference.opcfoundation.org/specs/OPC-10000-4/5.13.2.2)
- MonitoredItem 可处于 `DISABLED`、`SAMPLING` 或 `REPORTING`；禁用采样不会删除其其他参数。启用后 Server 应尽快报告首个 sample；若数据源尚无值，也可以用状态表达，而不是让该监控目标从清单消失。[OPC UA Part 4 §5.13.1.3](https://reference.opcfoundation.org/specs/OPC-10000-4/5.13.1)；[§7.23](https://reference.opcfoundation.org/specs/OPC-10000-4/7.23)
- Subscription 的 keep-alive 在没有 Notification 时仍证明 Subscription 活跃；DataValue 则另带 `statusCode`、`sourceTimestamp` 和 `serverTimestamp`，并规定无已知值时可返回 `Bad_NoValue`。因此“通道/订阅活着”不等于“某个 tag 有新鲜可用值”。[OPC UA Part 4 §5.14.1.1](https://reference.opcfoundation.org/specs/OPC-10000-4/5.14.1.1)；[§7.11](https://reference.opcfoundation.org/specs/OPC-10000-4/7.11)
- OPC UA 还把重建 SecureChannel/Session、复用 Subscription、补发 Notification 分成不同恢复步骤，进一步证明连接、采集管线和数据连续性不是同一状态。[OPC UA Part 4 §6.7](https://reference.opcfoundation.org/specs/OPC-10000-4/6.7)

### Eclipse Sparkplug 3.0

- NBIRTH/DBIRTH 必须声明该 Node/Device 在本 Sparkplug Session 中将报告的全部 metrics；当前值为 null 时仍保留该 metric，并设置 `is_null=true`。NDATA/DDATA 只报告自上次 BIRTH/DATA 后变化的 metrics。也就是说，metric 清单由 Birth 声明，不能由后续 Data 消息是否出现来推导。[Sparkplug 3.0 §7.1、§7.2（规范 PDF 第 61–62 页；规范断言汇总第 131 页）](https://sparkplug.eclipse.org/specification/version/3.0/documents/sparkplug-specification-3.0.0.pdf)
- NBIRTH 表示 Edge Node 在线且处于 MQTT Session，但关联 Device metrics 在收到新的 DBIRTH 前仍为 STALE；NDEATH/DDEATH 又要求分别标记 Node/Device offline，并把相关 metrics 标成 STALE。标准显式区分实体在线状态和 metric 数据质量/新鲜度。[Sparkplug 3.0 §4.2.1、§5.5、§5.7](https://sparkplug.eclipse.org/specification/version/3.0/documents/sparkplug-specification-3.0.0.pdf)

### OASIS MQTT 5.0

- MQTT 将 Network Connection 与 Session State 分开：Session 可以跨多个 Network Connections 延续，`Session Present` 表示是否复用了既有 Session State。[MQTT 5.0 §3.2.2.1.1、§4.1](https://docs.oasis-open.org/mqtt/mqtt/v5.0/os/mqtt-v5.0-os.html)
- Keep Alive/PINGREQ/PINGRESP 用于判断网络与 Server 是否可用；Will 在异常断开或 Session 结束等条件下发布。它们描述连接生命周期，不证明应用 payload 已到达，更不证明某个 tag 的值新鲜。[MQTT 5.0 §3.1.2.5、§3.1.2.10、§3.12–3.13](https://docs.oasis-open.org/mqtt/mqtt/v5.0/os/mqtt-v5.0-os.html)

## 对 Nerv-IIP 的推论与裁决

建议保留三组互不覆盖的事实：

| 维度 | 事实来源 | 最小字段 | 不能代表 |
|---|---|---|---|
| Connection status | OPC UA SecureChannel/Session、MQTT connection/Will、设备链路探测 | `unknown/alive/lost`、`observedAtUtc`、`connectedSinceUtc`、`disconnectedSinceUtc`、原因 | collector 正常运行；tag 有新样本 |
| Collector health | 进程/采集循环、配置加载、订阅/轮询、写入下游的运行结果 | `stopped/healthy/degraded/unhealthy`、最近成功/错误、received/dropped/error counters、counter epoch | 现场连接一定在线；每个 tag 都新鲜 |
| Sample freshness | 每个 tag 的成功样本和质量时间戳 | `never/current/stale/bad`、`firstSampleAtUtc?`、`lastSampleAtUtc?`、source/server/ingest timestamp、quality/status | connector 或 collector 整体健康 |

“覆盖 tag”还应有独立的配置/激活投影，至少包含 connector identity、tag identity、enabled、配置版本、协议地址、激活/订阅结果、最近错误，以及可空的首末样本时间。聚合卡片可显示 `configuredCount / activatedCount / everSampledCount / freshCount / errorCount`，但不能把其中任一计数直接命名为另一个。

最终裁决：**Nerv-IIP 的连接器覆盖关系应以显式配置/声明为权威；从未采样的 active tag 仍属于覆盖清单，并标记 `never sampled` 或激活错误。连接、collector 和 freshness 分轴持久化与展示，只在读模型层生成综合状态。**
