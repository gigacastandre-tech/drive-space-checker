# Drive Space Checker

Drive Space Checker monitors the free space on your hard drives, emails you when a drive
falls below a threshold you choose, and shows a WinDirStat-style treemap so you can see
exactly what is using disk space.

The installer is self-contained: the target PC does **not** need .NET installed.

## Install

1. Run `artifacts\DriveSpaceChecker-Setup-1.0.0.msi`.
2. Accept the UAC prompt (the app installs for all users under `C:\Program Files\Drive Space Checker`).
3. Launch **Drive Space Checker** from the Start Menu.

The MSI is not code-signed, so Windows SmartScreen may show a warning. Choose
**More info → Run anyway** if you trust the source.

To uninstall, use **Settings → Apps → Installed apps → Drive Space Checker → Uninstall**.
Your settings in `%AppData%\DriveSpaceChecker` are intentionally kept; delete that folder to
reset the app completely.

## Configure email alerts

Open the **Alerts & Email** tab.

| Field | Notes |
| --- | --- |
| SMTP host / port | For example `smtp.gmail.com` on port `587` (STARTTLS) or `465` (SSL/TLS). |
| Security | `STARTTLS` is the usual choice for port 587. Use `SSL / TLS` for port 465. |
| Username | Usually your full email address. |
| Password | Use an app password if your provider requires one (Gmail, Microsoft 365, etc.). |
| From address / name | The sender shown in the alert email. |
| Recipients | One or more addresses separated by `;` or `,`. |

Press **Send test email** to confirm the settings before relying on alerts.

The SMTP password is encrypted with Windows DPAPI and can only be decrypted by the same
Windows user on the same PC. It is never stored in plain text.

## Configure thresholds

Alerts are sent when a drive's free space is at or below the configured threshold.

- **Default threshold (%)** – alert when free space drops to this percentage or lower (default 10%).
- **Default threshold (GB)** – optional absolute free-space floor. Set to `0` to disable.
- **Per-drive overrides** – enable a row and set a percentage and/or GB value. A per-drive
  rule replaces the default rule for that drive.
- **Cooldown** – minimum hours between repeat alerts for the same drive (default 12).
- **Check interval** – how often drives are checked (default 15 minutes).
- **Daily report** – optionally email a summary once a day at the chosen hour.

The app also recovers an alert only after free space rises about 2% above the threshold,
which prevents alert spam when a drive hovers around the limit.

## Find what is using disk space

Open the **Disk Usage** tab.

1. Choose a drive or folder in **Scan location** (or use **Browse…**).
2. Press **Scan**. Enable **Include hidden / system** for a complete picture.
3. Click a block in the treemap to drill into a folder; use **↑ Up** to go back.
4. Review **Largest files**, **Largest folders**, and **File types** for quick cleanup targets.
5. Right-click a file or folder in the treemap for actions such as opening it in Explorer or
   moving it to the Recycle Bin.

Directories that Windows protects or that are reparse points (junctions, cloud placeholders)
are skipped safely; the scan continues and reports what it could read. Running the app as
administrator allows it to read more protected locations.

## Tray and startup

- Closing the window minimizes the app to the notification area when **Minimize to tray** is enabled.
- The tray menu can open the window, run a check, or exit.
- **Start with Windows** adds the app to the current user's `HKCU\...\Run` key and starts it
  minimized to the tray.

## Files and logs

| Path | Purpose |
| --- | --- |
| `%AppData%\DriveSpaceChecker\config.json` | Settings, drive rules, and alert history. |
| `%AppData%\DriveSpaceChecker\logs\app.log` | Diagnostic log. |

Set the `DSC_DATA_DIR` environment variable to use a different data folder (useful for
testing or a portable setup).

## Build the MSI yourself

Prerequisites:

- .NET 10 SDK
- WiX Toolset 5 CLI: `dotnet tool install --global wix --version 5.0.2`
- WiX extensions (the script installs them globally if missing):
  `WixToolset.UI.wixext/5.0.2` and `WixToolset.Util.wixext/5.0.2`

Build:

```powershell
powershell -ExecutionPolicy Bypass -File .\Installer\build-msi.ps1
```

The script:

1. Publishes a self-contained, single-file `win-x64` build into `%TEMP%\dsc-msi`.
2. Builds a per-machine MSI with WiX (Program Files, Start Menu shortcut, ARP icon,
   major-upgrade support, and automatic closing of a running app during upgrades).
3. Verifies the MSI with an administrative install.
4. Copies the result to `artifacts\DriveSpaceChecker-Setup-<version>.msi`.

Useful options:

```powershell
# Build a specific version
.\Installer\build-msi.ps1 -Version 1.0.1

# Reuse the existing publish output (skip dotnet publish)
.\Installer\build-msi.ps1 -SkipPublish

# Skip the administrative-install verification
.\Installer\build-msi.ps1 -SkipVerify
```

## Troubleshooting

- **No alert email arrives** – send a test email first, check the provider's SMTP host/port
  and security mode, and make sure you used an app password if required. Check
  `%AppData%\DriveSpaceChecker\logs\app.log` and the spam folder.
- **Alerts are not sent** – confirm **Enable alerts** is checked, the recipients are valid,
  and at least one drive is at or below its threshold.
- **A drive is missing** – only ready fixed, removable, and network drives are listed.
- **Some folders could not be scanned** – Windows permissions or reparse points; this is
  expected. Run as administrator for broader access.
