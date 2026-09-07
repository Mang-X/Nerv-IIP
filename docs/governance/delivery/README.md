# Delivery Governance

本目录只维护**当前 CI、构建与交付必须遵守的规则**。精确 job、命令、版本、参数、服务清单和运行结果必须回到各自 producer；历史测量与一次性审计进入 Reports。

## 路由

- [`ci-build.md`](ci-build.md)：CI 构建、后端测试分片与 workflow contract 的现态规则。
- [`deployment.md`](deployment.md)：AppHost、Compose、安装/发布制品与部署配置的现态交付规则。

相关入口：

- 当前部署架构：[`../../architecture/platform/deployment.md`](../../architecture/platform/deployment.md)
- 当前部署操作：[`../../runbooks/deployment.md`](../../runbooks/deployment.md)
- MAN-669 后端 CI 实测：[`../../reports/audits/backend-ci-build-strategy-man-669.md`](../../reports/audits/backend-ci-build-strategy-man-669.md)

本目录不得保存 CI run ID、耗时、阶段完成史或“当前通过多少项”之类时点数据。
