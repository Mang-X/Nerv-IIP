# MasterData 本地脚本

此目录包含仅为保留历史脚手架而留下的模板时代基础设施脚本。它们不是受支持的 Nerv-IIP 开发或部署入口。

必须改用仓库根目录的受治理命令：

```powershell
.\nerv.ps1 bootstrap
.\nerv.ps1 dev
.\nerv.ps1 dev -InfraOnly
pwsh scripts/verify-business-master-data-realignment.ps1
```

不得将凭据、直接 Docker Compose 命令、MySQL/Kafka 默认值或逐服务基础设施拓扑从此目录复制到新工作中。平台基础设施由根 AppHost、根目录 `nerv.ps1` 命令和 `scripts/` 下的受治理脚本负责。
