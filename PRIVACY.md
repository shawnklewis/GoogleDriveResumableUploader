# Privacy Policy — gdup (Google Drive Resumable Uploader)

_Last updated: 17 October 2026_

gdup is a command-line tool that runs on your own Windows computer and uploads files
you choose to **your own** Google Drive. It is published by Shawn K. Lewis
(shawnklewis@gmail.com). There is no gdup server: the developer does not receive,
see, store or share any of your data.

## What gdup accesses

- **Your Google account, through Google OAuth.** When you run `gdup config`, Google asks
  you to grant access with one of these scopes:
  - `https://www.googleapis.com/auth/drive.file`: only files and folders that gdup
    itself creates (or that you explicitly open with it), or
  - `https://www.googleapis.com/auth/drive`: your Drive, if you choose this scope.
- **The local files you name on the command line**, which are read and uploaded to the
  Drive folder you specify.

gdup uses this access **only** to create folders, upload files you select, resume
interrupted uploads, and verify the uploaded file's checksum. It does not read,
download, modify, or delete any other Drive content, and it does not access any other
Google service.

## Where your data goes

gdup talks only to Google:

- `accounts.google.com` and `oauth2.googleapis.com` to sign in and refresh access, and
- `www.googleapis.com` (Google Drive API) to upload your files.

During sign-in, Google redirects to a temporary local address (`http://127.0.0.1`) on
your own computer. gdup has **no analytics, telemetry, advertising, or third-party
services**, and nothing is sent to the developer or anyone else.

## What is stored on your computer

- `%APPDATA%\gdup\gdup.conf`: your OAuth client ID, plus the client secret and Google
  access/refresh token **encrypted with Windows DPAPI**. Only your Windows user account
  can decrypt them.
- `%LOCALAPPDATA%\gdup\sessions\`: small files used to resume interrupted uploads. They
  contain the local file path, the Drive destination, and Google's temporary upload
  session URL. They are deleted when the upload finishes.

## Google API Services User Data Policy

gdup's use and transfer of information received from Google APIs adheres to the
[Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy),
including the Limited Use requirements. Data obtained from Google APIs is used only to
provide gdup's upload feature to you, is never transferred to others, and is never used
for advertising or to train AI/ML models.

## Removing access and data

- Revoke gdup's access at any time at <https://myaccount.google.com/permissions>.
- Delete `%APPDATA%\gdup` and `%LOCALAPPDATA%\gdup` to remove all locally stored
  credentials and upload sessions.

## Changes and contact

Changes to this policy are published in this file in the repository. Questions:
shawnklewis@gmail.com.
