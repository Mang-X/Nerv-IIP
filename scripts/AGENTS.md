# 脚本目录路由

- **修改脚本：** 修改前必须读 `docs/governance/script-automation.md`；脚本分类、副作用、ownership、helper 与门禁以该 Governance 和当前机器检查为准。
- **执行、兼容验证或排障：** 再读 `docs/runbooks/script-automation.md`，并以 `nerv.ps1 help`、目标脚本 `Get-Help`、源码和测试核实当前命令/参数。
- **历史事故/扫描器形成过程：** 只在任务确需核对历史时读取 `docs/reports/investigations/`、`docs/reports/audits/`；历史结论不得覆盖当前 checker/helper 行为。
- **脚本/CI 清洗：** 删除重复/临时脚本、缩减影子框架或更广 CI routing 属 #2157 owner，不借普通脚本修改扩张范围。
