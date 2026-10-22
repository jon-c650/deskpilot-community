# Building this DeskPilot beta

This archive is the source corresponding to the build identified by BUILD.txt.
SOURCE-MANIFEST.json lists every included source/resource/project and its SHA-256.
The modified Terminals files and new DeskPilot code use the included Microsoft
Shared Source Community License (MS-CL). Separate third-party terms remain in
source headers and docs/licenses; see THIRD-PARTY-NOTICES.md. These terms are not
a grant to relicense vendor code or use upstream trademarks.

## Build prerequisites

Use a licensed Windows 10/11 or Windows Server build machine with .NET Framework
4.8 developer tools, MSBuild, Windows SDK .NET Framework tools (AxImp), MSVC x86 C++
tools, NuGet CLI and Python 3. GitHub's windows-2022 runner supplies these tools.
End users running the installer do not need any of these development tools.

Run scripts/build-from-source.ps1 from the archive's root with these tools on the
build environment's path. It restores only the pinned core dependencies from
NuGet.org, obtains official SHA-256-pinned/signed PuTTY files, generates managed
RDP API wrappers from the build machine's installed Windows control, and builds
the application, three plugins and native WireGuard helper. It does not copy or
distribute Windows' RDP control or a WebView2 browser runtime. It never opens a
user profile, connects to a user server or changes a database schema.

The application is in upstream/Terminals/Build/Output/Release/DeskPilot.Preview.exe.
Keep its full directory, including Plugins and runtimes. BUILD.txt must accompany
the executable. Web sessions require Microsoft's separately installed WebView2
Runtime; VPN requires official WireGuard. The native helper uses ordinary UAC.

The GitHub workflow additionally exercises real disconnected RDP ActiveX, PuTTY
and WebView2 fixtures, tab geometry/lifecycle, old credential/XML compatibility,
SQL in a disposable LocalDB instance, installer lifecycle and saved-data hashes.
Live authenticated sessions remain part of Windows acceptance testing.

The source includes editable C# and the unchanged EDMX database model. A T4 design
regeneration is not part of the build. Old vendor archives, obsolete utility
sources, historical artwork, user files and signing keys are intentionally absent.
Managed RDP wrappers are generated build outputs; their Windows/SDK provenance is
recorded in GENERATION.json. Toolchain/OS differences may affect binary hashes;
this is a verified source-build recipe, not a claim of bit-identical builds across
arbitrary machines.

For the public installer, run scripts/build-beta-installer.ps1 with PayloadDir
pointing to that output directory, SourceArchive pointing to the matching
manifest-verified source ZIP, and PublicRelease enabled. The release gate
checks licensing evidence, matching source delivery and excluded payload files.
The installer includes the same source ZIP it is distributed alongside. Do not
reuse an installer with source from a different build.
