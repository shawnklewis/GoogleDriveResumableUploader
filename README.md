# gdup

Uploads one large file to Google Drive as fast as a single stream allows, using Drive's
resumable upload protocol. It can pin the upload to a specific network interface and
stores credentials rclone-style (DPAPI-encrypted) in `%APPDATA%\gdup\gdup.conf`.

## Setup

```
gdup config                      # create a remote (needs your own Desktop-app OAuth client)
gdup config import-rclone        # or copy a Drive remote from rclone.conf
gdup config show
```

## Upload

```
gdup nics                                        # list interfaces for --via
gdup upload D:\video\big.mkv gdrive:Backups/2026
gdup upload D:\video\big.mkv gdrive:Backups --via "Ethernet 2" --chunk-size 512M
```

If an upload is interrupted (Ctrl+C, crash, network loss), run the same command again or:

```
gdup resume --list
gdup resume D:\video\big.mkv
```

Exit codes: 0 ok, 1 usage/config error, 2 upload failed, 3 checksum mismatch.

Drive accepts only one ordered stream per file, so `--via` chooses the link; it does not
combine links.

With the default `drive.file` OAuth scope, gdup can only see folders it has itself created
(or that were explicitly shared with the app), so an existing folder path it didn't create
will look empty and get duplicated rather than reused.

## Verifying a download

Release builds of `gdup.exe` are built by GitHub Actions from this repository and carry a signed
build-provenance attestation. To confirm that a downloaded exe was built here, from a specific
commit, and not modified since:

```
gh attestation verify gdup.exe --repo kurtnelle/GoogleDriveResumableUploader
```

The exe is not code-signed yet, so Windows SmartScreen may warn on first run (**More info → Run anyway**).

To publish a release, push a version tag (for example `git tag v1.2.0` then `git push origin v1.2.0`).
The Release workflow tests, builds, attests, and uploads `gdup.exe` with its `.sha256` checksum.
