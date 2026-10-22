## Extracting a downloaded ZIP

Close DeskPilot before changing builds. In File Explorer, right-click the downloaded ZIP, open **Properties**, select **Unblock** and **Apply**, then extract it into a new folder. Windows can otherwise mark the contained DLLs as downloaded and refuse SSH/Web plugin loading with error `0x80131515`; only previously trusted protocols may appear. No PowerShell command is required. DeskPilot does not enable `loadFromRemoteSources`, use an unsafe assembly loader or silently remove Windows' download marks.

Each new ZIP contains a separate `DeskPilot-<build>` folder. Keep that folder intact, including `Plugins` and the native helper. The title bar and About show the build identifier, also recorded in `BUILD.txt`. Avoid mixing files from different previews. **Tools → Open log folder** opens `%LOCALAPPDATA%\DeskPilot\Logs`; `plugins.log` records the running EXE, build, discovered plugins and load errors.

The packaged Windows icon is verified against DeskPilot's icon during CI. File Explorer may cache an icon for a reused executable path; the separate versioned folder gives the new build a fresh path. If the icon still differs there, compare the visible build identifier and the packaged `DeskPilot.ico` before replacing files.

# DeskPilot overlay UI preview

Extract the whole GitHub-built ZIP into a new folder and run **DeskPilot.Preview.exe**. Requires .NET Framework 4.8. No Visual Studio or DLL registration is needed. This is the integrated application, not DeskPilot.OverlayProof.exe.

The application still uses the isolated DeskPilot preview store. Do not replace an installed Terminals folder or copy this build over it. Existing preview connections remain available. The UI does not import or overwrite original Terminals data.

## Security update

- RDP certificate warnings still allow continuing to a self-signed server. The RDP transport, NLA options, scaling and official WireGuard activation/elevation code are unchanged.
- Legacy unauthenticated network sharing is retired in both interfaces; it cannot open its old listener or deserialize binary network objects. Network scanning and ordinary File import/export remain available.
- Newtonsoft.Json is updated to 13.0.4 with bounded, typed parsing. The unused vulnerable SharpZipLib dependency is removed from the application and ZIP.
- Existing local master-password profiles upgrade after a successful sign-in to an authenticated AES/HMAC key envelope using PBKDF2-HMAC-SHA256 at 600,000 iterations. The existing data key is retained: saved credential ciphertext is not bulk rewritten. New master passwords use the same envelope. Profiles without a master password retain Windows user protection.
- The envelope upgrade atomically replaces only the master-password record and creates an exact Windows-user-protected `*.pre-key-upgrade-*.dpapi` recovery copy next to the preview configuration. Wrong passwords, tampering, stale files and failed replacements are tested. If the upgrade cannot be saved, existing access is retained and a warning explains that protection has not been upgraded.
- Use this build or newer after a master-password upgrade: older preview executables do not understand the new envelope. Existing backups and original Terminals files retain their original protection; they are not modified or deleted. Shared SQL database master-key records are not silently migrated; SQL-backed operation still needs separate live validation.
- VPN associations migrate once from the existing names-only file to a profile-scoped store using stable folder IDs. Existing associations are preserved for currently identifiable folders, and the legacy file is retained unchanged. Rename/reparent keeps the tunnel; deleting and recreating a folder does not reuse another customer's association. Both VPN/group and folder Properties editors use the same checked writer.
- Folder defaults refresh at connection/dialog boundaries after changes by another instance. Save conflicts are checked before committing, and an ordinary VPN write failure rolls the folder policy back. This is not a single crash-proof transaction across the two files; a forced termination/power failure between writes still requires reviewing the two settings. Batch connection setup reports failed endpoints and continues with the others.

## DeskPilot branding

The executable, Explorer/taskbar/window/tray icons, dialogs, welcome screens, About page and product metadata use DeskPilot branding. The shared blue mark is supplied as a multi-resolution Windows ICO (16–256 px) and PNG/banner assets. Upstream credits and copyright notices are retained. Internal assembly/resource identifiers and compatible connection-data formats keep their existing names so saved data and plugins continue to work.

## Controls

- Connect opens the existing quick-connect dialog. Customers contains the existing favorites tree, search, context menus, group/credential workflows and Recent history. Double-click a connection to open its embedded tab. Add customer creates a root group through the existing group-name validation; Add connection opens the existing favorite editor.
- Customers is a stationary sidebar. The RDP canvas starts at its right edge. The top menu bar has labeled Disconnect, Fullscreen and Fit/100% controls beside a compact search field. Disconnect is disabled unless the selected session is connected. Details remains an overlay: × or Escape hides it, and its right-edge tab reopens it. Layout → Focus canvas hides floating panels while retaining the stationary sidebar and top controls. Details visibility never resizes the remote desktop.
- The full File/View/Favorites/Terminal/Tools/Groups/Help menu bar is on the main window. Tools contains only Options, Credentials and Open log folder. Original File commands retain their handlers. The floating legacy utility window and toolbars are removed from the DeskPilot interface; screen capture and the legacy command shell are not exposed. The classic fallback retains its original interface and utilities. The preview does not save hidden toolbar visibility over the classic layout. F4 opens Quick Connect.
- Details shows saved connection properties and whether an attached session is connected. It does not invent OS, IP-resolution, host-online or VPN-connectivity facts.
- Notes edits saved connection notes only after Save notes. Selection/close prompts protect unsaved edits.
- History shows the existing chronological connection-attempt history across customers, with double-click reconnect. It is not a server audit log.
- VPN opens existing group associations and confirmed controls; the inspector also has an associated-tunnel selector and Connect VPN / Disconnect VPN buttons. Dots show associated service state: green running, blue stopped/absent, gray unknown/unavailable. Service running does not establish network connectivity. Associations are read from the migrated, profile-scoped names-only store; no protected WireGuard profile files are read.
- The session toolbar’s Fit ✓ / 100% control switches local RDP image scaling. Fit is the DeskPilot default for each new RDP session; 100% uses native pixels.
- Fullscreen hides the header/status strip and panes to expose the full client area. Its top edge handle exits fullscreen and restores the previous pane choices.
- Theme switches dark/light chrome. Layout → Classic interface next launch retains the old interface as a fallback without disturbing current sessions. The classic menu contains DeskPilot interface next launch to return.

## Window resizing and RDP scaling

The working Microsoft RDP ActiveX version is retained. DeskPilot now enables its supported SmartSizing presentation setting by default after creating a connection. Enlarging, shrinking, maximizing or entering fullscreen gives the docked RDP control the actual available client area, with Fit enabled. The Fit / 100% switch can be changed while connected. This scales the existing image locally: it does not reconnect, renegotiate the server desktop, change remote DPI or edit the saved display resolution. It is distinct from dynamically increasing the remote desktop's working area.

Overlay visibility continues to leave the RDP host/control dimensions and negotiated dimensions unchanged. The client area changes only when the application itself is resized or fullscreen is explicitly changed. Microsoft documents SmartSizing here: https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpclientadvancedsettings-smartsizing . The user confirmed connected scaling works in the preceding Windows build. This pass leaves that scaling implementation unchanged; disconnected CI continues to verify state and geometry.

## VPN activation and administrator prompts

When the selected tunnel has no Windows service, Connect requests activation of its existing encrypted `.conf.dpapi` profile using the installed, Windows-signature-verified WireGuard LLC executable and its documented `/installtunnelservice` command. The normal Windows UAC administrator prompt is required. DeskPilot itself stays unprivileged and never opens or decrypts the protected profile. Existing services use standard start/stop; denied control can request the normal UAC prompt for Windows service control. No service ACL, user group, firewall, certificate policy or security registry setting is weakened.

The activation confirmation explains that the official tunnel service may remain installed and start with Windows until disabled in WireGuard. Disconnect stops the selected service; DeskPilot does not uninstall someone else's tunnel service or delete any profile. Canceling UAC does not count as successful activation. The service state is checked after an operation; a running service is still not proof of VPN reachability.

### Native WireGuard import and discovery

No PowerShell catalog command is required. In folder **Properties**, use **Refresh tunnels…** to discover profiles already installed in WireGuard, or **Import .conf…** to select a configuration file. The same buttons are available in the VPN/group association window. Both operations use normal Windows administrator approval; the main DeskPilot window stays unprivileged. The official WireGuard application must already be installed.

Import validates a local `.conf` (UTF-8 or BOM-marked UTF-16), refuses command hooks and duplicate profile/service names, and never replaces an existing profile. The approved helper uses WireGuard's documented manager-directory import procedure and waits for the official service to protect the file as `.conf.dpapi`. It does not decrypt existing protected profiles, change their permissions, enable dangerous script execution, start a tunnel or delete your original source configuration. Imported names are added to DeskPilot's names-only catalog. Click **Save** to keep the folder association; use the existing **Connect VPN** control when ready.

Configuration contents/private keys are not sent in command-line arguments or stored in DeskPilot settings/logs. The helper exchanges only its fixed operation, the selected path and tunnel names over a restricted local pipe with process-identity checks. On failed import, unchanged plaintext staging is removed where possible; if Windows prevents cleanup, any remaining file is confined to WireGuard's protected directory. A timeout is reported as an unconfirmed operation, never as success. Existing original `.conf` files remain your responsibility to store securely.

CI tests validation, command-hook rejection, duplicate refusal, source preservation, timeout cleanup, simulated protection acknowledgement and catalog merging without installing WireGuard or activating a VPN. Real UAC, manager encryption and live tunnel behavior require Windows testing with official WireGuard installed. The older scripts remain optional diagnostics, not a setup requirement.

WireGuard source documentation: https://git.zx2c4.com/wireguard-windows/about/docs/enterprise.md . Live activation/UAC require testing on a Windows machine with a real installed profile; CI does not install WireGuard or activate a VPN.

## SSH and web sessions

Use the existing **File → New connection** editor (or Add endpoint under a customer), select **SSH**, **HTTP** or **HTTPS**, and save the endpoint. Keep **Open in New Window** unchecked for the shared tabbed session workspace. The same customer folder, inherited settings, credential references, history and VPN association remain available. Existing RDP transport/scaling and VPN activation are unchanged. The Fit/100% control applies to RDP; SSH and web sessions resize their own content to the available canvas. Details remains a hideable overlay.

**SSH:** the ZIP contains official PuTTY 0.85, with release hashes pinned and Windows signatures checked during packaging. The terminal is embedded in the session tab and owns only its child process. SSH-2 is required; host-key verification is performed by PuTTY, with explicit prompts for first connections or changed keys. Saved user names are passed safely; passwords are entered interactively in the terminal, never exposed in process arguments or written to a temporary password file. MFA and Pageant authentication are supported. SSH options can select a PuTTY saved session for private-key and terminal preferences; no existing PuTTY sessions or host-key records are rewritten by DeskPilot. Closing the tab ends its owned PuTTY process.

**Web:** HTTP and HTTPS use Microsoft Edge WebView2, with Back/Forward, Home, Reload and an address field inside the shared session canvas. The Microsoft Evergreen WebView2 Runtime must be installed; if missing, DeskPilot shows an installation link instead of crashing or falling back to Internet Explorer. A saved URL may include its port, path and query. Pages use normal interactive web sign-in. Basic authentication prompts are restricted to the saved HTTPS origin. Saved DeskPilot passwords are not injected into web pages or Authorization headers. Certificate warnings default to Cancel and can be explicitly continued for that host/certificate in the browser session, with no machine trust-store changes or blanket TLS bypass.

WebView2 stores ordinary site cookies/cache under the current Windows user's `%LOCALAPPDATA%\DeskPilot\WebProfiles`, separated by endpoint and origin. It does not share your regular Edge profile. Browser password autosave, general autofill, host-object access, web messaging, arbitrary URL schemes, automatic pop-ups and downloads are disabled. User-clicked HTTP(S) pop-ups open in the current web tab; sites that require a separate pop-up, downloads or device permissions need further compatibility work. Do not treat an open browser/terminal tab as proof that authentication or remote reachability succeeded.

CI checks real PuTTY window embedding, resizing and process cleanup using only a loopback SSH banner fixture (no authenticated SSH session), and real WebView2 HTTP/JavaScript against a local fixture page. Actual SSH host-key prompts, password/key/MFA login, device web login and HTTPS certificate consent still require Windows acceptance testing. Existing disconnected RDP, folder inheritance, credentials and VPN regressions run alongside these checks.

WebView2 distribution: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution . PuTTY release checksums: https://the.earth.li/~sgtatham/putty/0.85/sha256sums . PuTTY's licence is included in the ZIP.

## Actions and current boundaries

PowerShell, File Explorer, Event Viewer, Services and System Info open separate Windows management connections to the selected host using normal Windows authentication and existing permissions. They do not reuse/export saved RDP passwords, install agents, enable remoting, change firewall rules or alter certificate validation. They need their respective remote services/permissions to be available. Copy IP / host copies the saved server address.

The header no longer advertises Clipboard, File transfer, File sharing or a Windows-security action: those buttons only opened properties or shortcut instructions. Use Ctrl+Alt+End with the remote desktop focused for its security screen. Clipboard/drive redirection remains configured through existing Connection properties, and session controls remain in the Terminal menu. No transfer channel or secure-attention injection is implied by the header.

The supplied mockup remains the visual target. This pass integrates the stationary customer sidebar, compact session icons in the main header, smaller search and fully painted theme-colored panel tabs without the native white tab frame. Details uses a flat edge-aligned surface, a blue primary Connect button and compact quick actions. Customer tree commands move from the legacy icon strip to the All Customers … menu and existing context menus. Native input/menu behavior and code-drawn icons remain; this is not a pixel-identical rendering. The remote canvas displays only the real session, never the mockup wallpaper or sample customers. CI screenshots use explicitly labeled CI-only fixtures that are not shipped as user data.

## Fixes following Windows feedback

A tab can be selected before its connection object is assigned. The inspector now recovers that late-bound host so its management actions do not remain disabled. Selecting a customer group also retains the active host when one is available. The customer tree uses native double buffering, cached icons and change-only count/VPN repainting instead of unconditional periodic invalidation.

## Validation

The user verified the preceding build's VPN connection, overlay dismissal, non-flashing tree and enabled inspector actions on Windows. This polish pass keeps the VPN control/association implementation unchanged.

The expanded Windows smoke loads the compiled RDP plugin and binds it to the actual disconnected ActiveX. It checks the real SmartSizing facade and Fit/100% control, host resize/maximize/fullscreen, the fullscreen exit handle, unchanged remote/saved resolution and the inspector/data checks. It cannot establish how a connected server renders the scaled image.


The user verified the standalone ActiveX proof in a connected session: overlays appeared/disappeared without remote resizing. Integrated CI creates the actual disconnected RDP ActiveX control inside the application and checks both inspector visibility states, zero size events, stable control bounds/desktop dimensions, native overlay hit tests, visible dismiss/reopen/hide-all buttons, late-bound inspector actions, tree double buffering/no unchanged-count repaint, light/dark geometry, the existing search pipeline, and explicit notes persistence preserving favorite identity/group/credential association. Pure VPN request tests cover missing/stopped/running services and reject unsafe tunnel names; they do not exercise UAC or real profile activation.

Please repeat connected tests in this integrated preview: multiple tabs; close and detach; credential inheritance; customer/group edits; search and Recent; clipboard/redirection; fullscreen and reconnect; VPN association/status/control; notes save; minimize/restore; Alt+Tab; modal dialogs; monitor/DPI changes. Check that toggling each overlay leaves the remote resolution and session unchanged. Real host resize/fullscreen remains a separate explicit action.

CI evidence: ui-smoke.txt, ui-dark.png and ui-light.png in the ZIP. These establish disconnected UI behavior, not live RDP, VPN reachability or external administration-tool success.

The main menu is hidden in fullscreen and restored on exit. Options remains available both in Tools and through the dashboard Settings button. Credentials remains in Tools and the Connection inspector tab. The menu simplification removes incompatible utility entry points; it does not repair the retired capture/command-shell implementations.

## Folder Properties and inheritance

Right-click a customer or subfolder → **Properties…**. The endpoint settings pages are embedded directly in this window. Edit a value to override it at this folder; the inheritance panel shows **override here**, **from [parent path]**, or **application default**. Checked settings override; clear a check to restore the inherited value immediately in the editor. **Reset folder overrides** restores inheritance throughout this folder. The high-contrast dialog groups its override list by category; search spans all categories, and selecting a row shows its complete value and source. Save commits the folder policy; Cancel leaves it unchanged. Unchanged settings retain their existing individual inheritance choices.

Existing and future endpoints inherit automatically on their **next connection**. Active sessions are not reconfigured. Folder settings cover protocol/port, display, resource redirection, gateway, security options, timeouts, connection behavior and execute-before-connect settings exposed by the installed protocol editor. Names, host addresses, endpoint notes, group membership and toolbar icons stay endpoint-specific.

Right-click an endpoint → **Inherited settings…** to see its local and inherited values, choose a source folder and check individual settings that should keep their saved endpoint values. Uncheck a setting to resume inheritance. **Save and edit endpoint…** opens the original endpoint editor; changes there become explicit overrides. The original endpoint Properties editor shows a notice when folder inheritance applies. Its values remain local values; the Inherited settings dialog shows the effective folder values. Multiple unrelated folder branches require an explicit source choice; no folder wins by alphabetical order.

Use **Saved credentials…** in folder Properties to select host and gateway credential profiles from **Tools → Credentials**. Inline username/password fields are disabled in the folder template editor: credential contents are not copied into folder metadata. The existing protected credential store remains authoritative, including future credential updates/master-password changes. Inherited authentication is materialized only in temporary connection copies for existing RDP transport paths. A missing referenced credential blocks inheritance rather than falling back to unrelated credentials.

Defaults and override choices are stored in a separate Windows-user-protected `FolderDefaults-*.dpapi` file beside the isolated preview favorites, with an encrypted `.bak` from the prior save. Stable folder IDs survive rename/reparent; deleted/recreated folders have new IDs. Source choices must still refer to an assigned folder. Optimistic version checks and a file lock prevent overwriting concurrent edits. Corrupt/unreadable metadata produces an error and is not silently discarded. No bulk rewrite of favorites or credential files is performed by a folder-policy save. This metadata belongs to the DeskPilot interface; the classic fallback uses the original saved endpoint settings.

Windows checks cover existing/nested endpoint propagation, child overrides, per-setting endpoint overrides/reset, original endpoint identity/data preservation, credential references and gateway transport copies, multi-folder conflicts, rename/cycle detection, protected persistence/reload, stale-write and corrupt-file rejection, and actual folder context Properties → full editor → Save/Cancel and endpoint override controls. Connected authentication, gateway and redirection still need your live Windows test.

Folder Properties includes a **VPN tunnel** picker using the existing names-only WireGuard catalog and customer associations. Select or enter a tunnel name, then Save. The inherit choice removes this folder’s explicit association and lets the existing parent-folder behavior apply. This selects an association only; the existing VPN controls activate the tunnel with the usual Windows security checks. Cancel does not change associations.
