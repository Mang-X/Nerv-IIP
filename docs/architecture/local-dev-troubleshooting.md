# Local Dev & Aspire Troubleshooting

Operational lessons for running the platform locally. Extracted from the root
`AGENTS.md` "Common Mistakes" list so the agent instruction file stays lean;
consult this file when local startup, Aspire, infra containers, or deployment
artifacts misbehave. The hard invariants (use `nerv.ps1`/Aspire CLI, AppHost is
the topology source, pinned infra images) remain in `AGENTS.md`.

## Startup & lifecycle

1. **Never start AppHost with `dotnet run`.** The platform AppHost must be
   managed by Aspire CLI: `.\nerv.ps1 dev` / `aspire start`, `.\nerv.ps1 stop` /
   `aspire stop`, `.\nerv.ps1 wait <resource>`, `.\nerv.ps1 logs <resource>`.
   In linked worktrees, startup must use Aspire isolated mode; `scripts/dev.ps1`
   handles this. Direct `dotnet run` leaves stale DCP/backchannel state and makes
   later `aspire add`, deploy, and diagnostics unreliable.

2. **Aspire `Finished` is not a dashboard problem.** A project resource shown as
   `Finished` usually means the process exited during startup. Inspect the latest
   DCP stderr log under `%TEMP%\aspire-dcp*` before changing code or restarting
   blindly. The real error is usually in the resource process log, not Aspire
   itself.

3. **Blank machines go through bootstrap first.** For a fresh online Windows
   machine, run `.\nerv.ps1 bootstrap -InstallMissing`, then `.\nerv.ps1 dev`.
   The bootstrap entry owns prerequisite checks, optional tool installation,
   local AppHost user-secrets initialization, package restore and AppHost build.
   Do not debug broad request failures until this path has passed and Docker
   Desktop is actually running.

4. **Local HTTPS certificates.** Aspire Dashboard/DCP and local HTTPS endpoints
   require a trusted developer certificate. On blank machines or after Aspire
   certificate cache changes, run `.\nerv.ps1 bootstrap -InstallMissing` or
   verify with `dotnet dev-certs https --check --trust`. If AppHost logs show a
   certificate name mismatch, reset with `aspire certs clean`,
   `aspire certs trust`, and `dotnet dev-certs https --trust`.

5. **Startup/stop scripts need bounded feedback.** `.\nerv.ps1 dev` and
   `.\nerv.ps1 stop` must show phase diagnostics and use bounded helper calls.
   A failed certificate check, exited container, Aspire/DCP hang, or successful
   startup must not all look like "still waiting". Stop must run fallback cleanup
   for current-repo AppHost processes and Aspire usvc-dev containers when Aspire
   CLI stop times out.

## AppHost configuration

6. **New project resources run as Development locally.** Platform AppHost is the
   canonical dev launcher. New project resources must run with
   `ASPNETCORE_ENVIRONMENT=Development` and `DOTNET_ENVIRONMENT=Development`
   unless there is an explicit test/deployment reason not to. Otherwise services
   may select production-like persistence or messaging branches and fail
   differently from local expectations.

7. **PostgreSQL services need local migration enablement.** If a local
   Development service relies on PostgreSQL migrations, verify whether AppHost
   must pass `Persistence__AutoMigrate=true` for that resource. Missing migration
   enablement can surface as broad Console request failures, downstream 500s, or
   gateway circuit breakers; the root cause may be a missing table such as
   `relation "...table..." does not exist`. Observed local failures include
   AppHub `apphub.registration_idempotency`, MES execution tables, Maintenance
   readiness tables, and Notification `notification_messages` /
   `notification_tasks`.

8. **Pinned infrastructure image tags.** Persistent local resources must be
   explicitly pinned in AppHost. PostgreSQL is currently `18` and Redis is
   currently `8`; do not use `latest` or unpinned Aspire provider defaults.
   PostgreSQL 18+ uses a different major-version data directory than the old
   pre-18 `/var/lib/postgresql/data` layout, so local dev uses
   `nerv-iip-postgres-18` and must not point PostgreSQL 18 back at the old
   `nerv-iip-postgres` volume without an explicit `pg_upgrade` or dump/restore.
   Do not switch major versions without a tracked upgrade plan, clean-volume
   test, preserved-volume migration test where applicable, AppHost build,
   Compose publish verification, and smoke startup. If Redis reports an RDB/AOF
   format error, stop Aspire and remove only the local `nerv-iip-redis` cache
   volume.

9. **Bootstrap seed passwords are never hardcoded.** Connected-machine bootstrap
   may create local Development user-secrets, but it must not keep a fixed IAM
   admin password in source. Generate a random local value by default, or
   require the operator to pass a value explicitly through a non-logged path.
   Secret-setting commands must mark sensitive arguments for script log
   redaction.

## Service startup failure patterns

10. **CAP PostgreSQL profile without integration event publisher registration.**
    Services with domain-event-to-integration-event converters must register the
    NetCorePal integration event publisher in the active CAP profile, including
    PostgreSQL. If startup fails with unresolved
    `NetCorePal.Extensions.DistributedTransactions.IIntegrationEventPublisher`,
    compare the service's CAP registration with a known working service before
    changing handlers.

11. **Redis-backed services aborting startup on first connect attempt.** Local
    Aspire startup can race Redis readiness. When a service constructs a
    `ConnectionMultiplexer`, parse options with `AbortOnConnectFail=false` so
    the service can start and reconnect instead of turning one transient Redis
    race into a failed resource.

## Deployment artifacts

12. **Aspire AppHost is the only topology source.** For container deployment,
    add/maintain Aspire deployment targets and generate Docker Compose artifacts
    with `.\nerv.ps1 publish-compose` or deploy with `.\nerv.ps1 deploy-compose`.
    Existing hand-written Compose files may remain for dependencies, smoke
    tests, or legacy overlay validation, but must not become a competing service
    graph.

13. **Vite dev proxy is not production routing.** `AddViteApp` works for local
    dev, but publish/deploy needs an explicit JavaScript production serving
    model. Console can use `PublishAsStaticWebsite("/api", gateway)`. Business
    Console needs two production API routes (`/api/console` to PlatformGateway
    and `/api/business-console` to BusinessGateway) or an equivalent
    BusinessGateway auth facade before Compose output can be called a complete
    Business Console deployment.

14. **Offline deployment is a separate track.** Offline packaging is a
    deployment architecture track, not the first local-development fix. Keep the
    immediate startup path focused on connected machines and Aspire CLI/AppHost.
    Future offline scripts should consume Aspire-generated artifacts instead of
    inventing a parallel topology.
