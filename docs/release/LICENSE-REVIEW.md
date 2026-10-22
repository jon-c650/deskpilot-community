# DeskPilot beta redistribution review — 2026-10-22

This is an engineering inventory and redistribution-obligations review for the
core beta, not a warranty of third-party rights or a general legal opinion.
`packaging/public-release.json` records the release gate. Website packaging must
also pass the binary provenance audit, installer tests and an independent rebuild
from the exact supplied source archive. A private repository link is insufficient
source delivery.

## Project terms and source

The inherited Terminals license is the **Microsoft Shared Source Community
License (MS-CL), October 18, 2005**. Preserve its exact text, source copyright
headers and attribution. It is not MIT or GPL; this release is described as a
free, source-available beta. New DeskPilot code is offered under the same project
terms. Separate vendor terms remain applicable; no vendor trademark rights are
claimed and no upstream license is silently substituted.

The installer contains a multi-file application with separate dependency
assemblies; no ILMerge or other combination of vendor code into the application
assembly is used. Matching editable application/plugin/helper source, resources,
project files, generated ActiveX wrapper C#, build instructions, license texts and
a SHA-256 source manifest are supplied in a source ZIP alongside Setup and inside
its installed Source folder. Keep that archive with redistributed installers.
The source download excludes private repository history, user data, old vendor
archives and signing keys. It contains all inputs for the reviewed compiled
project graph; a fresh Windows job rebuilds and tests that download without a
repository checkout. This is not a claim of byte-identical builds on arbitrary
Windows/SDK versions.

Original license: https://raw.githubusercontent.com/Terminals-Origin/Terminals/master/LICENSE.md

## Resolved inherited blockers

| Finding | Core-beta resolution and evidence |
| --- | --- |
| Unclear CodeProject tab-control grant | Excluded the inherited project and DLL. Independently implemented `DeskPilot.Tabs` with the consumed session API. Tests cover native handle/geometry retention, selection, cancelled close, overflow, detach and actual popup reattach/close ownership. |
| Copied renderer, icon extractor, encryption, serialization and other utility code | `Terminals.External.csproj` now compiles only an independent compatibility adapter, assembly metadata and the explicitly licensed Microsoft command-line parser. Old utility implementations are excluded. Framework AES/SHA preserve independently recorded legacy/current credential vectors; XML contracts and missing-file behavior are tested. |
| Unclear inherited artwork | Replaced embedded bitmap/icon references, serialized image lists and shipped thumbnails with original DeskPilot assets. `branding/RESOURCE-PROVENANCE.json`, editable generator code and payload checks record and enforce the replacement. Historical images are excluded from the source download. |
| EntityFramework 5 Windows supplemental redistribution conditions | Replaced EF5 with MIT-licensed EF6.5.2 and its SQL Server provider. The existing EDMX/schema is retained; automatic user-database initialization/migration is explicitly disabled. Disposable SQL tests check existing-model translation, group identities, rowversion reads and unchanged opaque credential strings. |
| Unidentified prebuilt RDP interop wrappers | Generate matching managed API bindings using Windows SDK AxImp and the build runner's installed control. Include generation/version/hash provenance, generated ActiveX C# and recipe. No Windows RDP implementation binary is shipped, replaced or registered. |
| Missing corresponding source for website recipients | Every candidate includes a matching manifest-verified source ZIP and BUILD identity. Setup embeds that exact ZIP; installer tests compare its hash. A separate no-checkout job rebuilds the public source and exercises it before website artifacts are uploaded. |
| Incomplete current dependency notices | Archive exact package licenses/notices and source-version evidence. Pin official PuTTY downloads by SHA-256 and signer. Enforce known binary filenames and exact third-party hashes with `audit-release.py`; an unknown payload DLL/EXE fails the build. |

## Removed optional legacy components

The user approved a beta focused on RDP, SSH, web, WireGuard, credentials and
customer data. Metro/GPL scanner UI, TransPort RAS binaries, SharpPcap/WinPcap,
ZedGraph, HexEditor, AWSSDK/FlickrNet, old cloud/screenshot integrations and other
protocol/vendor installers are outside the compiled/package/source graph. Their
historical presence is not a redistribution grant. Retired utility entry points
explain their absence; Tools retains Options, Credentials and Open log folder.
Configuration compatibility fields remain; no customer records are deleted.

## Distributed components

The exact current inventory and included notice filenames are in
`THIRD-PARTY-NOTICES.md`. It covers PuTTY/Pageant/PuTTYgen0.85; Newtonsoft.Json13.0.4;
FluentValidation6.2.1.0 and satellites; EntityFramework6.5.2; log4net3.5.0;
WebView2 SDK1.0.4258.31 and loaders; Michael Chapman's MIT IPAddressControl;
the Microsoft Shared Source Command Line Parser; and Inno Setup6.7.3.
`packaging/package-license-evidence.json` records package/source evidence;
`packaging/redistributables.json` records reviewed binary hashes. Preserve full
LICENSE and NOTICE files, including WebView2's third-party information.

Windows RDP, .NET Framework, the WebView2 browser runtime and official WireGuard
are externally installed prerequisites. They are not bundled. DeskPilot's native
VPN helper uses ordinary UAC and the official manager's protected profile model.
The setup executable embeds the licensed Inno engine; its original notices are
retained. Compiler source/license: https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3

## Release verification and limits

The Windows workflow checks source/binary provenance, legacy credential and XML
compatibility, isolated SQL read/write, disconnected RDP ActiveX geometry, embedded
SSH loopback and WebView2 local-page fixtures, VPN import safeguards, installation,
reinstallation, in-use refusal, installed application behavior and uninstall.
Saved-profile file hashes must remain identical through installer lifecycle tests.
The separate source job must rebuild successfully using only the source download.

These checks do not establish successful authentication to a real RDP/SSH server
or a live WireGuard tunnel after the replacements. Repeat those acceptance checks
on the Windows beta before broad distribution. The installer is unsigned and may
show Windows reputation warnings; packaging does not establish a trusted publisher.
No global remote-assembly-loading exception, automatic security bypass or deletion
of saved DeskPilot/original Terminals data is introduced.
