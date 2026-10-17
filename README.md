# gdup

Uploads one large file to Google Drive as fast as a single stream allows, using Drive's
resumable upload protocol. It can pin the upload to a specific network interface and
stores credentials rclone-style (DPAPI-encrypted) in `%APPDATA%\gdup\gdup.conf`.

## First-time setup

gdup signs in with **your own** Google OAuth client, so you never depend on anyone else's
Google Cloud project or quota. Creating one takes about 5 minutes and is free.

### Recommended: `gdup setup`

```
gdup setup
```

The wizard opens each Google Cloud Console page in your browser (and prints the URL, for RDP
or headless sessions), finds the `client_secret_*.json` you download, signs you in and saves a
remote. If you already have a project or a client JSON, pick that option at the start to skip
ahead. Ctrl+C at any point saves nothing.

### By hand

Replace `<project-id>` with your project's ID (shown under its name, e.g. `gdup-123456`).

1. **Create a project**: https://console.cloud.google.com/projectcreate

   ![Step 1 – Create a project](docs/setup/01-project.png)

2. **Enable the Google Drive API**: https://console.cloud.google.com/apis/library/drive.googleapis.com?project=<project-id>
   → **Enable**.

   ![Step 2 – Enable the Drive API](docs/setup/02-drive-api.png)

3. **Configure the consent screen**: https://console.cloud.google.com/auth/branding?project=<project-id>
   → **Get started**: app name `gdup`, your email as support email, Audience **External**,
   your email as contact, accept the policy, **Create**.

   ![Step 3 – Consent screen](docs/setup/03-branding.png)

4. **Publish the app (do not skip)**: https://console.cloud.google.com/auth/audience?project=<project-id>
   → **Publish app** → **Confirm**.
   > While the app is in **Testing**, Google expires your sign-in every 7 days and gdup stops
   > working until you reconnect. A note about verification can be ignored for personal use.

   ![Step 4 – Publish app](docs/setup/04-publish.png)

5. **Create the OAuth client**: https://console.cloud.google.com/auth/clients/create?project=<project-id>
   → Application type **Desktop app**, any name, **Create**, then **Download JSON**.

   ![Step 5 – Desktop app client](docs/setup/05-client.png)

6. **Create the remote**: run `gdup config` → `n`, and give the path to the downloaded
   `client_secret_*.json` (or paste the client ID and secret).

7. **Sign in**: Google shows **"Google hasn't verified this app"**. That is expected, because
   it is your own app: click **Advanced → Go to gdup (unsafe) → Continue**.

   ![Step 7 – Unverified app screen](docs/setup/07-unverified.png)

Then upload: `gdup upload <file> gdrive:Backups`.

If gdup later reports that Google rejected the saved sign-in, check that step 4 was done, then
run `gdup config reconnect <remote>`.

## Remotes

```
gdup setup                       # guided first-time setup (see above)
gdup config                      # create, edit or delete remotes (needs your own Desktop-app OAuth client)
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
