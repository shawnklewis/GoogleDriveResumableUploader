# gdup — Google Drive resumable uploader (design)

Date: 2026-09-25

## Goal

A Windows command-line tool (C#, .NET 10) that uploads a single large file to Google Drive
as fast as one stream allows, using the Drive v3 resumable upload protocol. It stores
credentials in an rclone-style config file and can pin the upload to a specific network
interface.

## Non-goals

- Parallel multi-stream upload of a single file. Drive requires a resumable session's chunks
  to be sent strictly in order, and Drive has no server-side concatenation. (Evaluated and
  rejected: split + Cloud Run joiner. Parallel single-file transfers belong on Azure Blob /
  S3-style stores in a future tool.)
- Automatic NIC failover or multi-NIC load balancing.
- Uploading folders or batches, downloads, sync.
- Non-Windows platforms (DPAPI is Windows-only).

## Commands

```
gdup config                          interactive wizard: new / edit / delete / list remotes
gdup config show [<remote>]          print remote(s), secrets masked
gdup config reconnect <remote>       redo browser sign-in for an existing remote
gdup config import-rclone [<name>]   copy a type=drive remote from %APPDATA%\rclone\rclone.conf
gdup nics                            list interfaces that are up: name, description, IPs, link speed
gdup upload <file> <remote>:<path>   upload a file into a Drive folder path
    --via <nic-name|local-ip>        bind the connection to this interface
    --chunk-size <size>              default 256M; multiple of 256 KiB; max 4G (suffixes K/M/G)
    --name <filename>                name in Drive (default: local file name)
    --stall-timeout <seconds>        default 60
gdup resume [--list | <file>]        list or continue interrupted uploads
```

`<path>` is a folder path relative to the remote root (`root_folder_id` if set, else My Drive).
Missing folders are created. An empty path means the root.

Exit codes: 0 success, 1 usage or config error, 2 upload failed, 3 checksum mismatch.

## Configuration and credentials

File: `%APPDATA%\gdup\gdup.conf`, INI format like rclone:

```ini
[gdrive]
type = drive
client_id = 1234-abc.apps.googleusercontent.com
client_secret = dpapi:AQAAANCM...
scope = drive
root_folder_id =
token = dpapi:AQAAANCM...
```

- `client_secret` and `token` are stored as `dpapi:` + base64 of
  `ProtectedData.Protect(utf8, null, DataProtectionScope.CurrentUser)`. Plain values (without
  the prefix) are accepted when reading and are encrypted the next time the file is saved.
- `token` holds the JSON `{"access_token","token_type","refresh_token","expiry"}`, the same
  shape rclone uses.
- Writes are atomic: write `gdup.conf.tmp`, then `File.Replace`/`File.Move(overwrite)`.
- Scopes: `drive` (default) or `drive.file`.

**OAuth flow:** installed-app loopback with PKCE (S256). Listen with a raw `TcpListener` on
`http://127.0.0.1:<free port>/` (no http.sys URL reservation or admin rights needed), open the system browser to
`https://accounts.google.com/o/oauth2/v2/auth` with `access_type=offline&prompt=consent`,
receive `code`, and exchange it at `https://oauth2.googleapis.com/token`. The user supplies
their own Desktop-app OAuth client ID and secret; the wizard prints brief instructions.

**Token refresh:** before each request, refresh if the token expires within 5 minutes; also
refresh once and retry on HTTP 401. A refreshed token is persisted to the config right away.
If the refresh fails with `invalid_grant`, stop and tell the user to run `gdup config reconnect`.

**rclone import:** parse `%APPDATA%\rclone\rclone.conf` (or `--rclone-config <path>`). If the
first line is `RCLONE_ENCRYPT_V0:`, report that the file is encrypted and offer a fresh
sign-in instead. List `type = drive` sections and copy `client_id`, `client_secret`, `token`,
`scope`, `root_folder_id`. If `client_id` is empty (rclone's built-in client), explain that
it can't be reused and require the user's own client ID and secret, followed by a fresh sign-in.

## Network interface pinning

`gdup nics` enumerates `NetworkInterface.GetAllNetworkInterfaces()` where
`OperationalStatus == Up` and the type is not Loopback/Tunnel, and shows name, description,
unicast IPv4/IPv6 addresses, and speed.

`--via` matches in this order: an exact local IP address, then the interface name
(case-insensitive), then the description (case-insensitive). No match, or an interface that
is not Up, is a usage error (exit 1) that prints the `nics` list.

Binding: a `SocketsHttpHandler.ConnectCallback` resolves the host, keeps only addresses of
the bound IP's address family, creates the `Socket`, `Bind(new IPEndPoint(localIp, 0))`,
then connects. Windows then sends the traffic out of that interface (strong host model).
Without `--via`, a normal connect is used.

## Upload algorithm

1. **Resolve destination:** walk `<path>` segment by segment with `files.list`
   (`q = name='<seg>' and '<parent>' in parents and mimeType='application/vnd.google-apps.folder' and trashed=false`,
   `supportsAllDrives=true`, `includeItemsFromAllDrives=true`), creating missing folders with
   `files.create`.
2. **Look for saved session state:** `%LOCALAPPDATA%\gdup\sessions\<sha256(fullpath|size|mtimeTicks|remote|folderId|name)>.json`
   holds `{sessionUri, filePath, size, mtime, remote, folderId, name, chunkSize, created}`.
   If it exists, query its status (step 4). Otherwise:
3. **Start a session:** `POST https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&supportsAllDrives=true`
   with JSON metadata `{name, parents:[folderId]}` and headers `X-Upload-Content-Length`,
   `X-Upload-Content-Type` (from the extension, or `application/octet-stream`). Save the
   `Location` header as the session URI in the state file straight away.
4. **Status query:** `PUT <session>` with `Content-Length: 0`, `Content-Range: bytes */<size>`.
   `308` → committed offset = `Range` upper bound + 1 (0 if there is no `Range`).
   `200/201` → already finished. `404/410` → session expired: delete the state, log a warning,
   and go back to step 3.
5. **Send chunks:** from the committed offset, `PUT <session>` with
   `Content-Range: bytes <start>-<end>/<size>` and a body streamed from the file (custom
   `HttpContent`, `FileStream` opened with `FileOptions.SequentialScan | Asynchronous`, 4 MiB
   reads, a progress callback). On `308`, the next offset comes from the `Range` header, not
   from what was sent. On `200/201`, parse the file resource (`id,name,size,md5Checksum`).
   The last chunk may be smaller than the chunk size.
6. **Verify:** a background task computes the file's MD5 with `IncrementalHash`, starting when
   the upload starts. Compare it with `md5Checksum` from the final response. A mismatch
   prints both values and the file ID and exits 3; the Drive file is left in place. The state
   file is deleted on success or on a mismatch.

**Transport settings:** one `HttpClient`/`SocketsHttpHandler` per run, HTTP/1.1 only,
`PooledConnectionLifetime = infinite`, per-request timeout = infinite (the stall watchdog
covers hangs), socket `SendBufferSize = 8 MiB`, `NoDelay = true`.

## Error handling

| Condition | Action |
|---|---|
| Network exception, IOException, 5xx, 429, 403 `rateLimitExceeded`/`userRateLimitExceeded` | Back off exponentially with full jitter (1s → 64s cap), do a status query, and resume. Retry for as long as the session is valid. |
| No bytes sent for `--stall-timeout` seconds | Cancel the request and treat it as a network error |
| 401 | Refresh the token once and retry; if it still fails, the auth error is fatal (exit 2) |
| 403 `storageQuotaExceeded`, `uploadLimitExceeded` / daily 750 GB limit, other 4xx | Fatal, with a readable message; the state file is kept (exit 2) |
| 404/410 on the session | Start a new session (see step 4) |
| Ctrl+C | Cancel, print "resume with: gdup upload ... or gdup resume", exit 2; state is kept |
| Local file changed (size/mtime differ from the state) | Start a new session; the old state is deleted |

## Progress output

When interactive (stdout not redirected), one line redrawn about 4 times per second:

`big.mkv  42.3%  211.6/500.0 GB  87.4 MB/s (avg 85.1)  ETA 00:56:12  via Ethernet 2 (192.168.2.10)`

The current rate is a 5-second sliding window. When redirected, a plain log line every 10 s,
plus a line for each retry or resume event. The final summary gives the file ID, size, elapsed
time, average rate, and the MD5 result.

## Project layout

```
gdup.sln
src/Gdup/Gdup.csproj              net10.0-windows exe, AssemblyName gdup, System.CommandLine
  Program.cs
  Cli/          command definitions and handlers
  Config/       IniFile, ConfigStore, Dpapi, RcloneImporter
  Auth/         OAuthLoopback, TokenProvider (refresh + persist)
  Drive/        DriveClient (folders), ResumableUploader, SessionStore, FileRangeContent
  Net/          NicSelector, BoundHandlerFactory
  Output/       ProgressReporter
tests/Gdup.Tests/Gdup.Tests.csproj  xUnit; fake Drive server implemented as an HttpMessageHandler
```

Publish: `dotnet publish src/Gdup -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`.

## Testing

- **Unit:** INI parse and write round-trip, keeping unknown keys; DPAPI protect and unprotect;
  rclone import (normal, missing client_id, encrypted header); `Range` header parsing;
  chunk-size parsing and validation (256 KiB multiples, suffixes, max); `--via` matching;
  session-state key and staleness.
- **Integration (fake resumable server as an in-process `HttpMessageHandler`):** a clean upload; a server that
  commits only part of a chunk; the connection dropped mid-chunk; 503 and 429 bursts; 410 on the
  session (restart); a 401 that triggers a token refresh; a stall (server stops reading) that
  trips the watchdog; resuming from a state file after a simulated process restart. Every case
  asserts that the bytes the server assembled equal the source file and the MD5 matches.
- **Manual smoke test:** a real Drive remote, a file of several GB, `--via` on each NIC, kill and
  resume mid-upload.
