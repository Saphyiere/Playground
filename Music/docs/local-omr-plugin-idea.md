# Optional local OMR verification plugin

Recorded: 2026-11-12

Status: idea retained for later investigation. This document does not add an engine, dependency, plugin loader, or app setting.

## Owner's intent

Add an independent automatic check of PDF-to-notation conversion using an existing optical music recognition (OMR) engine.
The owner wants recognition on the tablet, without maintaining a web API or server. A separately built personal companion
app/plugin is acceptable. Engine source, model weights and packaged binaries should stay outside this public repository;
Prima could contain the small integration and configuration needed to use the installed component.

Verification should check the converted notes against the selected PDF edition. It should not replace a simplified
arrangement with notes from a different recording or edition, or treat agreement between two recognizers as certainty.

## Existing work to investigate

| Candidate | What already exists | Remaining question for Prima |
|---|---|---|
| Audiveris | Desktop OMR engine/editor, MusicXML export, and documented intermediate OMR data. | Its supported installers target Windows, macOS and Linux. Copying a desktop installation to Android is not a demonstrated packaging path; an Android port and dependency assessment would be needed. |
| HOMR | An image/PDF recognition pipeline producing MusicXML, with CPU inference available. | Which symbols, engine version and model weights give useful independent verification of our piano scores? |
| Andromr | An existing Android app using HOMR. Its README describes on-device processing and MusicXML export. | Whether its engine can be adapted to an automatic job/result contract, and whether it can run with the screen closed. Its documented usage currently says to leave the app focused. |

Andromr is the most directly relevant packaging example found so far: Kivy/Buildozer builds its APK, while Java inference
uses LiteRT for segmentation/encoding and ONNX Runtime for decoding. This is evidence that HOMR-based recognition has
already been packaged for Android, not proof that it meets Prima's accuracy or background-execution requirements.
[Audiveris](https://github.com/Audiveris/audiveris), [HOMR](https://github.com/liebharc/homr),
[Andromr](https://github.com/aicelen/Andromr).

These projects use AGPL-3.0; HOMR also documents model-training-data licensing uncertainty. Keeping a package outside this
repository is an organizational choice, not an automatic exemption from its licenses. Review the chosen engine and weights
before redistribution or incorporation into a public build. [HOMR license FAQ](https://github.com/liebharc/homr/blob/main/LICENSE_FAQ.md).

## Possible packaging approaches

### Separately installed companion APK

Build the engine in a separate local project outside this checkout, install that APK alongside Prima, and let Prima select
the installed verifier. The integration would use Android app-to-app communication and scoped file access, without a web
server. Android supports passing file content URIs with temporary access grants between applications.
[Android sharing documentation](https://developer.android.com/training/sharing/send).

A future contract would need a request ID, source hash and revision, page/bar ranges, engine/model version, result file,
progress/status, cancellation and explicit unsupported/error outcomes. A returned result must match the requested source
revision before it can participate in comparison. Recognition must not overwrite an accepted reading automatically.

Start with file export/import to prove recognition quality. Only then automate the handoff. Do not assume an existing OMR
app already exposes the necessary background job or result interface merely because it can share a MusicXML file.

### Optional artifact included at build time

Alternatively, a private Android-compatible library plus model assets could be supplied to a personal Prima build from
outside the repository. The normal build would remain usable without it. This requires compatible Android runtimes/native
dependencies and adds package size, memory and update considerations. A Windows executable or desktop JAR installation is
not automatically such an Android library.

The owner would build the private component, point the personal build at its artifact, install the resulting APK, and
enable the verifier in a future setting. Public GitHub releases should not silently acquire private engine/model artifacts.
Neither packaging approach is implemented or selected yet.

## Verification behavior and first feasibility gate

1. Feed the same confirmed PDF source to the model reader and the independent OMR engine.
2. Match source bars before comparing pitch, rhythm, rests and supported notation. Refuse an uncertain bar correspondence
   rather than shifting later bars to make the results appear to agree.
3. Report agreement, disagreement and unsupported/inconclusive content separately. Preserve both original results.
4. Use precise disagreements for a targeted source recheck or correction proposal. Keep accepted bars and undo/history intact.

Use the existing ignored Sweden PDF, downloaded same-edition MIDI and accuracy artifacts as the first experiment: can the
engine identify the old conversion's twelve incorrect pitches without flagging the corrected notes? Follow with other
independently checked piano excerpts; one correct piece is not a general accuracy guarantee.

Before integration, measure recognition quality, false alarms, supported symbols, CPU/RAM use, package size, cancellation,
and behavior with the activity hidden and screen off. No paid model run or OMR installation is part of recording this idea.

Related: [conversion implementation](pdf-conversion-redesign-plan.md) and [Sweden baseline](sweden-accuracy-baseline.md).
