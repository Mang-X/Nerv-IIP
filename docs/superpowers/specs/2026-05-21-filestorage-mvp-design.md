# FileStorage MVP Design

## Goal

Move FileStorage from boundary skeleton to a usable platform capability without making object-storage deployment a prerequisite for the first implementation slice.

## Scope

The first FileStorage MVP implements the platform-owned file metadata and authorization flow:

1. Create upload sessions.
2. Complete upload sessions into stored file metadata.
3. Read file metadata by `fileId`.
4. Create short-lived download grants.
5. Persist FileStorage facts in PostgreSQL under the `filestorage` schema.
6. Enforce the same schema convention tests already used by AppHub, Ops and IAM.

The first slice does not implement real binary transfer. It proves the platform contract, persistence model, authorization-shaped API, and no-leak boundary for internal object keys.

## Provider Order

1. **First: server-proxy metadata stub**
   - Use `server-proxy` as the selected `uploadMode` and provider label.
   - Return platform-controlled upload instructions.
   - Store an internal `objectKey`, but never expose it through public API responses.
   - This allows API, persistence, SDK and Console/API-client work to progress without MinIO deployment.

2. **Second: tus**
   - Add resumable upload semantics after the core FileStorage facts are stable.
   - Treat tus as the complete binary-transfer capability for the FileStorage MVP.
   - Keep tus behind the same Upload Provider abstraction.
   - FileStorage remains the owner of session creation, completion validation, metadata and grants.

3. **Post-MVP: MinIO/S3 multipart**
   - Do not include MinIO/S3 multipart in the FileStorage MVP.
   - Add only when object-storage deployment and integration testing are ready.
   - Treat MinIO/S3 as an infrastructure adapter, not as the FileStorage public contract.
   - Use short-lived instructions or presigned URLs only; no long-lived object storage credentials or object keys leave FileStorage.

## API Contract

The MVP endpoints are:

```text
POST /api/files/v1/upload-sessions
POST /api/files/v1/upload-sessions/{uploadSessionId}/complete
GET  /api/files/v1/files/{fileId}
POST /api/files/v1/files/{fileId}/download-grants
```

`CreateUploadSession` accepts organization/environment context, owner reference, file purpose, file name, content type, expected size and optional checksum. It returns `uploadSessionId`, `fileId`, `uploadMode`, provider name, expiry and upload instructions.

`CompleteUploadSession` marks a pending session as completed and creates stored file metadata. The first slice validates session state, expiry, purpose and caller context. It records an internal object key but does not verify a real object store object yet.

`GetFileMetadata` returns public file facts only: `fileId`, organization/environment, owner reference, purpose, file name, content type, size, checksum, scan status, status and timestamps. It must not return `objectKey`.

`CreateDownloadGrant` returns a short-lived platform download URL or placeholder URL for the first slice. It must not return `objectKey`.

## Persistence

Add FileStorage PostgreSQL persistence in the same release slice:

1. `ApplicationDbContext` in `Nerv.IIP.FileStorage.Infrastructure`.
2. EF Core entity configurations for stored files, upload sessions and download grants.
3. Initial migration under the `filestorage` schema.
4. `__EFMigrationsHistory` configured under the `filestorage` schema.
5. Schema convention tests using existing `Nerv.IIP.Testing` helpers.

The first schema should include at least:

```text
stored_files
upload_sessions
download_grants
```

`object_key` is stored only in FileStorage-owned persistence. Public request/response contracts, SDK DTOs and Gateway facade responses must not expose it.

## Boundaries

FileStorage owns generic file facts and access grants. It does not interpret business meaning beyond `ownerService`, `ownerType`, `ownerId` and `filePurpose`.

IAM-backed authorization can be integrated through Gateway or service auth in later slices. The MVP keeps request shapes compatible with organization/environment and principal context so permission enforcement can be added without changing public contracts.

## Testing

The first implementation must follow TDD:

1. Web tests for each endpoint.
2. Tests proving `objectKey` does not appear in metadata or download grant responses.
3. Domain/application tests for session completion rules.
4. PostgreSQL schema convention tests for `filestorage`.
5. Existing skeleton boundary tests either remain compatible or are replaced by behavior-focused tests.

## Acceptance

The first FileStorage MVP is accepted when:

1. A client can create an upload session.
2. The same session can be completed into stored file metadata.
3. The stored file can be read by `fileId`.
4. A download grant can be created for that file.
5. Public responses do not expose internal object keys.
6. FileStorage PostgreSQL migration and schema convention tests pass.
7. Backend solution tests and AppHost build still pass.
