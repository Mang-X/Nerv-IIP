# MasterData Local Scripts

This directory contains template-era infrastructure scripts retained only as historical scaffolding. They are not the supported Nerv-IIP development or deployment entrypoint.

Use the repository-root governed commands instead:

```powershell
.\nerv.ps1 bootstrap
.\nerv.ps1 dev
.\nerv.ps1 dev -InfraOnly
pwsh scripts/verify-business-master-data-realignment.ps1
```

Do not copy credentials, direct Docker Compose commands, MySQL/Kafka defaults, or per-service infrastructure topology from this folder into new work. Platform infrastructure is owned by the root AppHost, root `nerv.ps1` commands, and governed scripts under `scripts/`.
