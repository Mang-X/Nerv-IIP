# Maintenance 报警自动建单配置

Maintenance 使用现有服务 `IConfiguration` 来源（appsettings、环境变量等）加载 `Maintenance:AlarmPolicy:Entries`。服务启动时通过 `IOptions` 固定快照；修改配置后必须重启 Maintenance 服务，不支持热更新。

每项指定 `OrganizationId`、`EnvironmentId` 和 `Mode`。仅需区别报警时填写精确 `AlarmCode`；省略表示该 scope 的全部报警。同 scope 的全报警项不能与精确报警项并存，同一个精确报警项不可重复，没有覆盖优先级。字符串按原值精确比较，不 trim、不改变大小写，不支持设备维度或通配。

```json
{
  "Maintenance": {
    "AlarmPolicy": {
      "Entries": [
        {
          "OrganizationId": "factory-a",
          "EnvironmentId": "production",
          "Mode": "WorkOrderOnly"
        },
        {
          "OrganizationId": "factory-b",
          "EnvironmentId": "production",
          "AlarmCode": "OVER_TEMP",
          "Mode": "WorkOrderAndOccupy",
          "AssetUnavailableReasonCode": "Thermal_Inspection"
        }
      ]
    }
  }
}
```

环境变量示例键：`Maintenance__AlarmPolicy__Entries__0__OrganizationId`、`Maintenance__AlarmPolicy__Entries__0__Mode`；其余字段按相同层级设置。样例目录码不是内置默认，使用前必须在 `factory-b` / `production` 的 `downtime-reason` 目录中建立实际有效码。

`WorkOrderOnly` 不可携带原因码；`WorkOrderAndOccupy` 必须携带目录码。配置冲突或模式错误会阻止启动。未配置、空数组或未匹配条目都普通建单，不产生 AssetUnavailable 事件。

消费时沿 v2 建单路径验证当前同 scope 目录码，目录码不存在时报 `maintenance-asset-unavailable-reason-code-not-found`，不降级建普通单，也不把失败 inbox 固化为成功。停止盲目重放，先核实配置 scope 与目录原值：目录修正后可按既有 CAP 有界重试或重新投递原消息恢复；若修正的是配置，先重启服务再重投。回滚配置也需重启，且只影响后续尚未成功消费的报警，不改变已建工单。当前双发阶段，占用成功同时产生 v1 companion 和 v2 事实。

运行证据通过[真实依赖测试入口](testing/real-dependencies.md)的 `maintenance-alarm-policy-redis-cap` 成员取得；lane runner 拥有数据库与 Redis namespace 的清理，不能清理共享服务资源。
