# Prima background rendering gate

This separate diagnostic APK has no Activity and makes no network or model calls. Its foreground service owns AndroidX
JavaScriptEngine, runs the app's existing Verovio 6.3 JS/Wasm bundle, and rasterizes its SVG using AndroidSVG. Android's native
`PdfRenderer` renders and segments an original synthetic PDF. It never opens Prima's database or secure storage.

The service is exported only to callers holding Android's `DUMP` permission, such as the ADB shell. The Release manifest has
no Internet permission. The .NET SDK adds Internet permission to Debug builds for debugging support.

Dependencies are pinned in the project. No .NET JavaScriptEngine binding was available, so the proof uses Java source within
the .NET Android project and the published Google Maven artifact directly (`Bind=false`). The managed regression service
also compiles the production renderer's C# and Java source through automatic Java-source binding. Its separate run verifies
that bridge after the original rendering and service-ownership gate.

## Build

```powershell
dotnet build Music/tools/Prima.RenderingProof/Prima.RenderingProof.csproj -c Release --nologo
```

Output: `artifacts/bin/Prima.RenderingProof/release/com.saphyiere.prima.renderingproof-Signed.apk`.

## Device experiments

Install only this package. Do not reinstall, stop, clear, or change the data of `com.saphyiere.prima`.

```powershell
adb -s DEVICE install -r artifacts/bin/Prima.RenderingProof/release/com.saphyiere.prima.renderingproof-Signed.apk
adb -s DEVICE shell am start-foreground-service -n com.saphyiere.prima.renderingproof/.RenderingProofService
adb -s DEVICE logcat -d -s PrimaRenderingProof:I
adb -s DEVICE pull /sdcard/Android/data/com.saphyiere.prima.renderingproof/files/proof artifacts/rendering-proof/normal
```

`result.json` must say `passed`, declare zero Activities, contain two PDF regions, and report two nonblank notation images
with identical PNG hashes after recreating the isolate. Visually inspect `notation-1.png` and the two PDF crops; the metrics
alone do not prove that glyphs or cropping are correct.

To prove cancellation during JavaScript execution, start the deliberate infinite-loop experiment, wait for
`CANCEL_PROBE_READY`, then send the cancellation action. The loop runs only inside the disposable isolate.

```powershell
adb -s DEVICE shell am start-foreground-service -n com.saphyiere.prima.renderingproof/.RenderingProofService --ez cancellation_probe true
adb -s DEVICE logcat -d -s PrimaRenderingProof:I
adb -s DEVICE shell am startservice -n com.saphyiere.prima.renderingproof/.RenderingProofService -a com.saphyiere.prima.renderingproof.CANCEL
adb -s DEVICE pull /sdcard/Android/data/com.saphyiere.prima.renderingproof/files/proof artifacts/rendering-proof/cancelled
```

The result must say `cancelled`, with `wakeLockHeldAfterCleanup=false`, `activeIsolateAfterCleanup=false`, and
`serviceStopAccepted=true`. Wait for that final field before starting another run, and check `dumpsys activity services` to
confirm the proof Service is absent. Run the normal
experiment again and verify it passes, proving that cancellation did not leave the engine unusable.

For the screen-off experiment, the user locks the tablet manually. This command waits up to two minutes for screen-off and
does not inject keys or manipulate the user's foreground app. Leave the screen off until the service logs `PROOF_PASS`.

```powershell
adb -s DEVICE shell am start-foreground-service -n com.saphyiere.prima.renderingproof/.RenderingProofService --ez require_screen_off true
adb -s DEVICE pull /sdcard/Android/data/com.saphyiere.prima.renderingproof/files/proof artifacts/rendering-proof/screen-off
```

All recorded screen-off flags must be true. The service holds a bounded five-minute partial CPU wake lock; it never turns on
the display. Resource cleanup runs after success, error, cancellation, and `onDestroy`.

An optional user-owned PDF can be copied into this diagnostic app's external files as `input.pdf` after its first run. It
must contain two systems for the current gate assertion. Its content is not added to source control:

```powershell
adb -s DEVICE push USER_PDF /sdcard/Android/data/com.saphyiere.prima.renderingproof/files/input.pdf
adb -s DEVICE push USER_MEI /sdcard/Android/data/com.saphyiere.prima.renderingproof/files/input.mei
```

`input.mei` likewise replaces the synthetic notation fixture. The result records the source mode and SHA-256 for both inputs.

Every run replaces the output directory's files, so collect a run before starting the next one. The synthetic PDF can be
regenerated with `python Music/tools/Prima.RenderingProof/Fixtures/make_synthetic.py`. The notation fixture is the existing
synthetic MEI from `Prima.Core.Tests`.

## Measured results

The Lenovo TB390FU running Android API 36 passed the gate on 2026-11-12, including a run with the screen off throughout
rendering. The production managed bridge subsequently passed its separate regression service on the same device.

| Experiment | Observed result |
| --- | --- |
| Synthetic PDF and MEI, no Activity declared | Passed. Native PDF rendering found 20 staff lines and two nonblank system crops. Both recreated isolates rendered identical 1,600 × 326 PNGs with 40,618 ink pixels. The two notation passes took 911 ms and 410 ms. |
| Cancellation during an infinite JavaScript loop | Passed after correcting service cleanup to use the latest Android start ID. The result was cancelled, the isolate and CPU wake lock were released, `serviceStopAccepted` was true, and a separate Android service inventory showed the service absent. |
| Screen-off prerequisite timeout | Failed its prerequisite because the display stayed on for two minutes. The service released its isolate/wake lock and stopped successfully. This establishes timeout cleanup, not screen-off rendering. |
| User-owned PDF and saved 22-bar MEI, after cancellation | Passed with two nonblank source crops and identical 1,600 × 1,302 notation PNGs across recreated isolates. This also proved that a subsequent normal run recovered after cancellation. |
| User-owned PDF and MEI, screen off required | Passed. Screen-off was true before and after PDF rendering, during both notation passes, and at completion. The two notation passes took 864 ms and 591 ms. Both resource flags were false, service stop was accepted, and Android reported the service absent. |
| Production managed renderer, synthetic fixtures | Passed. Preflight rendered successfully, the normalized crop had zero pixels different from the matching full-page region, a nonexistent page was rejected, cancellation returned in 90 ms, and the next engraving matched the original PNG hash. The display was off at start and finish. The CPU wake lock was released and Android confirmed the service stopped. |

The normal run's notation and PDF crop images were visually inspected. Local evidence is under
`artifacts/rendering-proof/normal`, `artifacts/rendering-proof/cancelled-fixed`, and
`artifacts/rendering-proof/screen-off-timeout.json`. The user-input runs are under `artifacts/rendering-proof/sweden` and
`artifacts/rendering-proof/sweden-screen-off`; both PDF crops and the complete notation were visually inspected. These
diagnostic artifacts and user-owned inputs are not source fixtures.

The normal result used bundled Verovio `6.3.0-425dd7b`. The device advertised Wasm compilation, promise returns, named byte
buffers, file-descriptor evaluation, isolate heap limits, and evaluation without transaction limits. This is evidence for
this device and WebView provider combination only. Production must check Android API 26 or later, sandbox availability,
and every required feature before a paid model request. An unsupported device must receive an explicit conversion-unavailable
reason; using a hidden Activity/WebView is not a fallback. If this engine route is unavailable, native Verovio remains a
separate feasibility exercise, not an implemented alternate renderer.

The production app retains its existing API 24 minimum. A narrow manifest override permits packaging the optional
JavaScriptEngine AAR, whose own minimum is 26; `AndroidConversionRenderer` checks API 26 before constructing its Java bridge.
Constructing the managed renderer itself only creates its .NET serialization gate. On API 24/25 it reports conversion as
unavailable while the rest of Prima remains installable. This lower-API fallback has not been exercised on hardware by this
API 36 proof.

The renderer-only experiments do not establish musical accuracy, production job persistence/recovery, notifications,
long-run background limits, or behavior after Android force-stop. The installed Debug results below provide separate,
scoped evidence for the production job host.

## Production bridge regression

After installing the current proof APK, this separate service exercises the production `AndroidConversionRenderer` with
synthetic fixtures only. It checks the complete preflight path, normalized PDF cropping against the corresponding pixels of
the full page, rejection of a nonexistent page, cancellation while an engraving is pending, and identical rendering after
the cancelled operation. The original infinite-loop test above separately proves termination of active Wasm execution.

```powershell
adb -s DEVICE shell am start-foreground-service -n com.saphyiere.prima.renderingproof/.ManagedRendererService
adb -s DEVICE logcat -d -s PrimaManagedProof:I
adb -s DEVICE pull /sdcard/Android/data/com.saphyiere.prima.renderingproof/files/managed-proof artifacts/rendering-proof/managed
```

Require `status=passed`, matching notation hashes, no held wake lock, an accepted service stop, and absence of the service
from `dumpsys activity services`. This service uses the same production source files; it does not copy their implementation.
Its result does not verify the production foreground host, job persistence, or notification navigation.

The passing result is saved locally under `artifacts/rendering-proof/managed-fixed`. The initial managed run caught a
connection-lifetime race: interrupting a sandbox connection before it resolved allowed the next request to race the still
bound connection. Cancellation now records intent during connection establishment, waits for the connection to become
closeable, and closes it before releasing the managed renderer gate. Once connected, cancellation closes the owned sandbox
immediately. The separate connection timeout remains bounded at 30 seconds.

## Installed Debug integration

The main Prima **Debug** build includes `com.saphyiere.prima.ConversionDiagnosticService`, exported only to callers holding
`android.permission.DUMP`. The class and its manifest registration are absent from Release. This runs the real installed
job store, coordinator, renderer and foreground host; it is separate from the renderer-only proof APK.

The caller stages three fixed files in the main app's external `files/diagnostics` folder: `source.pdf`, `source.json`
(`ConversionSource`), and, for offline runs, `reading.json` (`SystemReading`, with every bar indexed globally from one).
Only the fixed library ID `conversion-diagnostic-sweden` is used. The original imported piece is never updated.

```powershell
adb -s DEVICE shell am start-foreground-service -n com.saphyiere.prima/.ConversionDiagnosticService --es command prepare
adb -s DEVICE shell am start-foreground-service -n com.saphyiere.prima/.ConversionDiagnosticService --es command offline
adb -s DEVICE shell am start-foreground-service -n com.saphyiere.prima/.ConversionDiagnosticService --es command status
```

`prepare` imports or reuses the test copy and its confirmed source map. `offline` seeds all chunks at the rendering stage,
so no inference request is needed; it can also resume an interrupted rendering-only test checkpoint. `cancel` calls the
coordinator's explicit cancellation operation on the test piece's latest job. `status` writes only that piece's latest job
to `files/diagnostics/status.json`. The complete initial job is written to `start.json` before host execution starts.
Each command writes a separate `command.json` completion/error record and stops its temporary diagnostic service after
handing off to the production host.

The additional command `live` deliberately starts real, potentially charged inference using the configured provider and
the explicit `ReadSystem` model/effort defaults, with model fallback disabled and bounded chunks. It must only be invoked
after announcing the specific authorized live test. Installing a Debug build, preparing fixtures, or running `offline`
does not invoke it automatically. This README is not authorization to run a paid request.

Installed Debug integration passed on the same API 36 tablet on 2026-11-12. Seven rendering-only chunks reached Ready
while the screen was dozing, with zero inference attempts or charges, and the production service exited. Explicit Cancel
persisted Cancelled. A separate run was interrupted by terminating the verified app process after partial rendering; on
reopening through the diagnostic entry point it recovered as Interrupted with two completed chunks retained. Resuming the
same job finished all seven chunks without inference calls. Evidence is in
`artifacts/diagnostics/prima-conversion/installed-test`, including `before-interruption.json`, `after-interruption.json`,
and `after-resume.json`. This proves checkpoint retention and rendering-stage continuation in the actual installed host;
it does not prove recovery of a paid request whose response was not received.

A separately announced live test also reached Ready while the display remained asleep: all seven chunks completed with
seven recorded inference attempts and a reported cost of $1.017748. Android's notification inventory contained the
job-specific completion notification on `prima.conversion.results`, and the production service had exited. Its saved job
and notification evidence are `installed-test/live-result-8eee23da1d644163ae4f714e57007913.json` and
`installed-test/notification-complete.txt` under the diagnostic folder above.

The owner subsequently authorized direct tablet UI checks. An announced one-bar recheck of bar 7 was started through
the actual two-step review form. Back returned to the PDF toolbar, then Library and Android Home were opened while the
request continued. The completed job posted a native notification; tapping it returned to the correct piece and selected
bar 7. The saved proposal matched the existing written bar exactly and was left unaccepted. Its one request used 12,582
input tokens and 2,359 output tokens, cost $0.097508, and took 20.20 seconds of provider time. Evidence includes
`installed-test/live-recheck-85a2b3ebcef84813a72d2a0dd359693e.json`, `ui-recheck-notification.png`,
`ui-from-notification.png`, and `ui-recheck-proposal.png`. This verifies the actual completion-notification navigation,
not just notification registration.

## Sources

- [Android JavaScript and Wasm engine](https://developer.android.com/develop/ui/views/layout/webapps/jsengine)
- [.NET Android Maven dependencies](https://learn.microsoft.com/dotnet/android/binding-libs/advanced-concepts/android-maven-library)
- [AndroidSVG](https://github.com/BigBadaboom/androidsvg) (Apache 2.0)
- [Verovio](https://github.com/rism-digital/verovio) (LGPL 3 or later; existing repo asset and licence files)
