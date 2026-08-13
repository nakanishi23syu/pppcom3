---
name: dicom-communication-testing
description: Reference for testing DICOM network communication (C-ECHO, C-STORE, C-FIND, C-MOVE) and Orthanc's RESTful API against this project's dicom-pacs-vm / DicomTool.DicomScp, using the locally installed DCMTK, Orthanc, and DVTk Storage SCU/SCP Emulator tools. Use this skill whenever the user asks to test, verify, or troubleshoot DICOM communication, wants DCMTK commands (echoscu/storescu/findscu/movescu/dcmdump), wants to query or drive Orthanc's REST API, mentions AE titles/ports for this project's PACS, or asks "how do I test C-FIND/C-MOVE/C-STORE" in this repo — even if they don't name the tools explicitly. Also use it before re-deriving DICOM CLI commands from documentation or the web, since verified commands and known gotchas are already recorded here.
---

# DICOM communication testing (dicom-tool-3)

This project keeps a hand-tested command reference under
[`docs/dicom-testing-tools/`](../../docs/dicom-testing-tools/README.md) precisely so that DICOM
CLI/API commands don't need to be rediscovered or looked up online every time someone wants to
test C-ECHO/C-STORE/C-FIND/C-MOVE or Orthanc's REST API against this project. **Read the relevant
file(s) there before writing any DCMTK or Orthanc command from scratch.**

| File | Contents |
| --- | --- |
| [`README.md`](../../docs/dicom-testing-tools/README.md) | Overview, AE-title/port defaults table, what dicom-tool-3 does and doesn't support, the VM↔host-PC firewall gotcha |
| [`dcmtk.md`](../../docs/dicom-testing-tools/dcmtk.md) | `echoscu`/`storescu`/`findscu`/`movescu`/`dcmdump` — verified commands and results |
| [`orthanc.md`](../../docs/dicom-testing-tools/orthanc.md) | Orthanc REST API (`/modalities`, `/instances`, `/queries`) — verified commands and results |
| [`dvtk.md`](../../docs/dicom-testing-tools/dvtk.md) | DVTk Storage SCU/SCP Emulator — locations, defaults, and why it needs manual GUI operation |

## The one fact that saves the most re-derivation

**`dicom-tool-3`'s own `DicomTool.DicomScp` implements all four DIMSE services**
(C-ECHO/C-STORE/C-FIND/C-MOVE — C-FIND/C-MOVE support was added 2026-08-13; see
`services/DicomTool.DicomScp/Services/DicomScpService.cs`, which implements
`IDicomCEchoProvider`, `IDicomCStoreProvider`, `IDicomCFindProvider`, and `IDicomCMoveProvider`).
Older notes or memory of this project saying C-FIND/C-MOVE "aren't supported" are stale —
don't repeat that claim; the commands below against `DICOMTOOL3` directly are verified working.

Consequences that follow directly from the current implementation:

- C-FIND/C-MOVE against dicom-tool-3 only cover the **STUDY and SERIES** query/retrieve levels
  (PATIENT/IMAGE return zero matches rather than erroring — that's by design, not a bug to chase).
- C-MOVE's destination AE must be pre-registered in dicom-tool-3's own `RemoteAeTitles` config
  (`appsettings.json` / `appsettings.Development.json` / `appsettings.Production.json`, mirroring
  Orthanc's "modalities" concept). An unregistered destination fails with
  `Refused: MoveDestinationUnknown`. The registered AE title must equal what the destination
  system itself expects as its Called AE Title — for dicom-tool-3 as its own destination
  (self-loop testing) that means the registry key must be `DICOMTOOL3`, not some other alias,
  or the association gets rejected on the inbound leg. DICOM AE titles are capped at **16
  characters**; going over silently truncates and breaks the lookup (hit this exact bug during
  verification — `DICOMTOOL3SELFTEST` at 18 chars failed until shortened).
- Orthanc remains a fully-capable alternative/complementary target (AE `ORTHANC`, DICOM port
  `4242`, REST port `8042`) — useful for testing against a second real PACS, and dicom-tool-3 ↔
  Orthanc C-MOVE now works in **both directions** (verified).
- The DVTk Storage SCU/SCP Emulator is installed as portable executables at
  `D:\Programming\lerning\Storage SCU Emulator.exe` / `...Storage SCP Emulator.exe`. It's a GUI
  (WinForms) tool with no CLI automation surface, so **it cannot be driven by Claude Code in this
  environment** — testing with it requires the user to click through the GUI themselves. `dvtk.md`
  has the exact manual steps and known default AE titles (`DVTK_STR_SCU`/`DVTK_STR_SCP`, port
  `104` by default — change to a non-privileged port before use).

## Other things not to re-derive

- **Orthanc requires the calling AE title to be pre-registered** (`PUT /modalities/{name}`)
  before it will accept an incoming DICOM request from that AE — including from DCMTK tools.
  Skipping this produces `Peer aborted Association` even though the association itself was
  accepted. Register the caller's AE (a dummy host/port is fine if you're not routing traffic
  back to it) before troubleshooting anything else. dicom-tool-3 does not enforce this check.
- **DCMTK's `findscu`/`movescu` need `-S`** (Study Root Query/Retrieve model) when talking to
  either Orthanc or dicom-tool-3, or the presentation context gets rejected outright
  (`No Acceptable Presentation Contexts`).
- **VM↔host-PC communication needs a Windows Firewall rule on the host.** When dicom-tool-3 (on
  the VM) needs to reach a tool running on this host PC (e.g. C-MOVE to Orthanc), the host's
  firewall blocks the inbound connection by default — this surfaces as C-FIND working fine but
  C-MOVE failing with `Peer aborted Association` / a `SocketException (10060)` timeout in the
  VM-side event log. Fix with an inbound rule scoped to the VM's subnet, e.g.:
  `New-NetFirewallRule -DisplayName "..." -Direction Inbound -Protocol TCP -LocalPort <port> -RemoteAddress 192.168.93.0/24 -Action Allow`.
  This requires an elevated PowerShell session — if the current session isn't elevated, ask the
  user to run it rather than trying to work around the elevation requirement. **This same
  VM→host-PC topology is expected to recur in the user's work environment**, so lead with this
  check whenever a DICOM transfer from the VM to something on the host fails only for
  C-MOVE/C-STORE (not C-ECHO/C-FIND).
- Key AE titles/ports (full table in `README.md`): dicom-tool-3 SCP = AE `DICOMTOOL3`, port
  `11112`; this repo's test SCU client AE = `DICOMTOOL3SCU`; Orthanc DICOM = AE `ORTHANC`, port
  `4242`; Orthanc REST = port `8042`. dicom-tool-3 does **not** implement DICOMweb — its HTTP
  side is GraphQL (`docs/CONTRACT.md`), not QIDO-RS/WADO-RS/STOW-RS.
- The target VM (`dicom-pacs-vm`, `192.168.93.128`) needs to be running for anything to reach
  dicom-tool-3's SCP — use `start-all.bat` and confirm SSH/port reachability before assuming a
  DICOM failure is about DICOM at all.
- Deploying a `DicomTool.DicomScp` code change to the VM: `dotnet publish` locally, stop the
  `DicomToolScp` NSSM service over SSH first (the VM locks the `.dll` while running), sync only
  that service's `publish/` folder (a full `deploy.bat` run re-syncs all 6 services from
  whatever's locally built, which can clobber other services with stale builds), then restart the
  service. `appsettings.Production.json` on the VM is Git-ignored and hand-maintained — it won't
  exist until you create it, and `deploy.bat`/WinSCP is configured to never overwrite it.

## Keep the docs current

When a new command gets tried and verified (or a new failure mode gets understood), add it to
the relevant `docs/dicom-testing-tools/*.md` file — table + example command + result, matching
the existing style — rather than letting it live only in conversation history. That's the entire
point of this reference existing.
