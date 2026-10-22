# DeskPilot beta installation and acceptance testing

Website packages are produced only after the release provenance, installer and
source-only rebuild jobs pass. Keep Setup, its matching source ZIP, license
notices and SHA256SUMS together when publishing the download.

Download the Windows artifact named `DeskPilot-Beta-Website-Package`, extract it, close
DeskPilot and its sessions, then double-click the Setup EXE. No Visual Studio or
PowerShell setup script is needed. The setup installs for your current Windows
user and adds a **DeskPilot Beta** Start Menu entry. The desktop shortcut is
optional. The application continues using its existing DeskPilot saved-data
locations. Keep your normal backups before testing a beta.

The installer is unsigned. It does not establish a trusted publisher or suppress
Windows security warnings. It does not disable downloaded-file protections or
change remote assembly-loading policy.

Please test an existing RDP connection, fitting/fullscreen, authenticated SSH,
a device web login, and WireGuard import/connect/disconnect. The embedded web
browser requires Microsoft's WebView2 Runtime; VPN requires official WireGuard.
Normal WireGuard administrator consent still applies to privileged operations.

For lifecycle testing, install again while the app is closed. Setup must refuse
to replace an in-use application. Uninstall from Windows Settings → Apps; your
DeskPilot connections, credentials, folder settings and VPN associations must
remain. Reinstall and verify they return. Original Terminals data is never a
setup/uninstall target. Installer support for existing data paths does not imply
an older application can read newer authenticated master-password envelopes.

The Actions diagnostics artifact contains `installer-smoke.txt`, setup logs and
installed-app UI regression results. Automated checks cover first install,
same-version reinstall, in-use protection, Start Menu targeting, the installed
session regression suite, removal and hashes of all saved CI profile files.
They do not verify an authenticated live RDP/SSH/VPN server or future-version
upgrade paths.

The artifact also includes `BUILD.txt`, `SHA256SUMS.txt` and the license review.
The build ID identifies the tested PR merge revision, which can differ from the
branch commit. The bundle includes matching source and license notices. Complete the live
connection acceptance checks above before promoting a beta for broad use.
