# Sweden transcription accuracy baseline

Date: 2026-11-12

Status: the local reference was independently read from the supplied PDF by two agents and reconciled against enlarged source crops. A subsequently supplied MIDI corroborates every pitch and onset. Human review is still pending; this is a provisional baseline, not a certified gold standard.

## Source and provenance

The supplied source is a one-page 792 by 612 point PDF. It embeds a 3454 by 970 pixel raster screenshot, not editable score objects. The screenshot contains two printed systems: bars 1-13 and bars 14-22. A blue playback cursor overlaps the beginning of bar 1.

Source PDF SHA-256:

```text
154d3c97f613151a232acb9a8884f1eae5e0a925b57d84d908256212c246e5c4
```

Local artifacts, relative to the repository root:

| Artifact | Purpose |
|---|---|
| `artifacts/diagnostics/prima-conversion/sweden.pdf` | Supplied printed source. |
| `artifacts/diagnostics/prima-conversion/sweden-baseline-*.png` | Enlarged source crops used for visual reading. |
| `artifacts/diagnostics/prima-conversion/sweden-independent-review.md` | The second agent's independent visual review. |
| `artifacts/accuracy/sweden/reference.json` | Provisional, machine-readable interpretation of the printed notes and rhythms. |
| `artifacts/accuracy/sweden/expected-events.json` | Expanded notes with source bar, staff, onset, duration, and MIDI pitch. |
| `artifacts/accuracy/sweden/downloaded-source-candidate.mid` | Subsequently supplied source candidate, used only after the visual reference was recorded. |
| `artifacts/accuracy/sweden/downloaded-source-report.json` | Strict comparison of that MIDI with the visual reference. |
| `artifacts/diagnostics/prima-conversion/sweden-current-draft.json` | Actual tablet draft saved at 17:10:43 local time on 2026-11-12. |
| `artifacts/diagnostics/prima-conversion/sweden-raw-calls.json` | Four completed provider-call records for that draft. |
| `artifacts/accuracy/sweden/initial-reading.json` | Original reading answers extracted from those calls before comparison corrections. |
| `artifacts/accuracy/sweden/initial-reading-report.json` | Metrics before comparison. |
| `artifacts/accuracy/sweden/current-reading-report.json` | Separate raw, canonical, MEI-roundtrip, and playback metrics for the saved draft. |
| `artifacts/accuracy/sweden/saved-mei-report.json` | Metrics for the MEI actually persisted in the tablet draft. |

The full music reference, source PDF, candidate results, and MIDI remain under ignored `artifacts/`. Their redistribution licence has not been established. The checked-in evaluator and tests contain only generic code and synthetic music examples. Do not move the full local music into tracked fixtures without resolving its licence.

The reference has 22 bars, each four quarter beats on both staves, and 208 pitched notes: 133 on the upper staff and 75 on the lower. Note names include the effect of the two-sharp key signature and any printed accidental. C4 means MIDI 60. The event syntax is `pitch[+pitch]:quarter-beat-duration`, with `r` for a rest. Context offsets are zero-based: offset 1 is the beginning of the second beat.

The upper staff starts in bass clef and changes to treble after the first beat of bar 7. Source details that required careful enlarged inspection included the dotted first chord and sixteenths in bar 10, the higher final upper note in bar 16, the low final chords in bars 20 and 22, and the explicit accidentals in the last four lower-staff bars. These were resolved between the two source readings before comparing the downloaded MIDI.

The reference keeps nominal written durations. The final fermatas specify a hold, not an exact number of extra ticks. Their expressive duration is excluded, as are dynamics, fingering, clef glyph rendering, and engraving geometry. The reference records context for human review, but the evaluator's numerical metrics currently cover notes, timing, staff assignment, and bar coverage; they do not grade all written notation.

## Independent MIDI corroboration

The user subsequently supplied `Sweden_from_Minecraft__C_Easy_Piano_1794533346301.mid`. Its SHA-256 is:

```text
9c93a8fbb76c2bd744c06208d3e929a12e3a0e0e88730ff4da6e7ebf9452d115
```

Despite the filename, all 208 pitches and onsets match the source-derived reference without transposition, time stretching, or automatic alignment. All 22 bars are present. MIDI track/channel values are not treated as reliable staff identity.

Strict pitch/onset/duration matches are zero because this MIDI shortens every note release. At 480 ticks per quarter, the observed duration is exactly 95 percent of the printed duration minus one tick:

| Printed ticks | MIDI ticks | Notes |
|---:|---:|---:|
| 120 | 113 | 32 |
| 240 | 227 | 17 |
| 480 | 455 | 66 |
| 720 | 683 | 32 |
| 960 | 911 | 61 |

That regular gate pattern is evidence of playback articulation, not a reason to overwrite the printed reference. The evaluator reports the strict mismatch and pitch-at-onset agreement separately. It applies no hidden tolerance or duration normalization.

## Actual tablet baseline

The captured conversion made four completed requests, with 54,472 input tokens, 50,774 output tokens, and a locally recorded total cost of $1.233368. These are recorded usage/cost values for this run, not a new pricing estimate.

| Call | Input tokens | Output tokens | Recorded cost |
|---|---:|---:|---:|
| Read first system | 6,305 | 22,431 | $0.473840 |
| Compare first system | 23,536 | 4,601 | $0.186164 |
| Read second system | 6,235 | 18,828 | $0.401500 |
| Compare second system | 18,396 | 4,914 | $0.171864 |

All measured stages contain 22 bars and 208 notes, with no extra, missing, or duplicate source bars. Every note's staff/onset/duration slot matches the reference. The mismatches are pitches.

| Stage | Exact pitch/onset/duration matches | Exact precision and recall | Affected bars |
|---|---:|---:|---:|
| Original model reading, before comparison | 198 / 208 | 95.19% | 7 / 22 |
| Saved model reading, after comparison | 196 / 208 | 94.23% | 7 / 22 |
| Canonical `ReadingScore.Build` result | 196 / 208 | 94.23% | 7 / 22 |
| Regenerated MEI parsed back | 196 / 208 | 94.23% | 7 / 22 |
| Actual saved MEI parsed back | 196 / 208 | 94.23% | 7 / 22 |
| `Score.Expected` playback notes | 196 / 208 | 94.23% | 7 / 22 |

The affected bars are 8, 10, 14, 16, 18, 20, and 22. All twelve final pitch errors are in low ledger-line chords on the upper staff; the wrong notes are three semitones above their source-reference counterparts. This is an observed error pattern, not proof of a particular internal model failure.

The first comparison changed no notes. The second comparison changed bars 20 and 22 and made one additional pitch incorrect in each. Comparison cost $0.358028 and reduced this run's exact matches from 198 to 196. It should not be treated as an accuracy guarantee or automatically repeated without measured evidence of benefit.

These specific pitch errors are already in the model data and survive the measured notation/playback transformations unchanged. This does not clear the rendered review UI: the metrics do not assess its misplaced highlights, wrapping, clef presentation, or other visual defects. The aggregate note percentage also masks that nearly a third of the bars contain an error; it is not a suitable standalone acceptance gate.

## Offline evaluator

### First installed background-job evaluation

On 2026-11-12, the new tablet job pipeline converted the same source in seven complete-bar chunks, with model fallback and
full-response validation rewrites disabled. The downloaded MIDI and expected reference were used only for evaluation, not
sent to the model. All seven application attempts returned usable readings without output-limit splitting or application-level retries in this run.

| Measure | Previous saved conversion | New background job |
|---|---:|---:|
| Exact evaluated note events | 196 / 208 | 208 / 208 |
| Bars containing pitch errors | 7 / 22 | 0 / 22 |
| Missing, extra, or duplicate bars | 0 | 0 |
| Recorded input tokens | 54,472 | 69,847 |
| Recorded output tokens | 50,774 | 36,918 |
| Recorded cost | $1.233368 | $1.017748 |
| Elapsed time | 7:54, from the captured app display | 5:12.48, from job timestamps |
| Provider requests | 4, including two comparisons | 7 readings |

Raw readings, canonical notation, MEI roundtrip, and playback extraction each scored 208 exact pitch/onset/nominal-duration
matches. Cost was about 17.5% lower and output usage about 27.3% lower, while repeated input increased. These are measurements
of one same-edition fixture, not a general accuracy or cost guarantee. The model still reported ten individual doubts; its
confidence is not the evaluation metric. The evaluator's existing limits (including expressive timing, printed marks,
enharmonic spelling, and the provisional reference status) still apply.

The run completed while the tablet was asleep. Android retained the saved-result notification after the foreground service
stopped. Separate installed tests had already demonstrated explicit cancellation and process-death recovery: two completed
chunks survived, and the same job resumed to completion without any provider request in that rendering-only test.

Ignored local evidence:

- `artifacts/diagnostics/prima-conversion/installed-test/live-result-8eee23da1d644163ae4f714e57007913.json`
- The adjacent `.accuracy.json` report and `.reading.json` evaluator input.
- `notification-complete.txt`, `before-interruption.json`, `after-interruption.json`, and `after-resume.json` in that folder.

### Evaluator commands

`Music/tools/Prima.Accuracy` does not contact a provider, read API keys, or use a candidate as its own reference. From the repository root:

```powershell
dotnet run --project Music/tools/Prima.Accuracy -- reference artifacts/accuracy/sweden/reference.json artifacts/accuracy/sweden/expected-events.json
dotnet run --project Music/tools/Prima.Accuracy -- midi artifacts/accuracy/sweden/reference.json artifacts/accuracy/sweden/downloaded-source-candidate.mid artifacts/accuracy/sweden/downloaded-source-report.json
```

To evaluate a saved application reading:

```powershell
dotnet run --project Music/tools/Prima.Accuracy -- reading artifacts/accuracy/sweden/reference.json artifacts/diagnostics/prima-conversion/sweden-current-draft.json artifacts/accuracy/sweden/current-reading-report.json
```

The reading input is a saved draft with a `readings` array, or that array alone. The tool produces separate reports for:

1. Raw model readings, using independent pitch and duration arithmetic rather than `ReadingScore`.
2. The canonical result of `ReadingScore.Build`.
3. MEI written from that canonical score and parsed back by `MeiReader`.
4. Playback notes produced by `Score.Expected`.

An existing saved MEI can also be evaluated directly with `mei` in place of `reading`. The reading command regenerates MEI; it does not substitute the draft's saved MEI for the raw-reading check. This distinction helps separate model errors, assembly changes, and serialization changes. Engraving geometry still needs its separate rendering checks.

Each report includes exact note matches, pitch-at-onset matches without requiring equal duration, rhythm matches without requiring equal pitch, exact precision and recall, missing/extra note records, per-bar counts, and missing/extra/duplicate bars. Matching uses multisets, so a repeated candidate note cannot match the same reference note twice. Missing raw systems retain their source-system positions; the evaluator does not slide later raw notes forward to improve agreement.

An `Error` marks a stage that could not be evaluated; its empty comparison must not be presented as a measured zero-percent musical result. A successful process exit means reports were produced, not that the candidate was accurate. Inspect the metrics and review status.

Current scope is the supplied untied, nongrace, nontuplet 4/4 piece. The raw-reading evaluator rejects unsupported rhythmic constructs rather than claiming general OMR support. MIDI is compared in musical ticks, not wall-clock seconds, and ignores staff assignment. The canonical and playback stages retain the application's numbering; renumbering or omitted bars can therefore appear as alignment errors and must be investigated rather than normalized away.

## Validation and next gate

Focused synthetic tests cover reference duration/chord/rest expansion, invalid reference timing, duplicate-note accounting, separate pitch/rhythm metrics, source-bar coverage, accidental carry within a bar, missing-system source identity, and MIDI division normalization.

```powershell
dotnet build Music/tests/Prima.Core.Tests/Prima.Core.Tests.csproj
artifacts/bin/Prima.Core.Tests/debug/Prima.Core.Tests.exe -class Prima.Core.Tests.AccuracyTests
```

The next acceptance step is to review the measured mismatches at their source bars and have the provisional reference confirmed by the user or another competent human reader. Use this baseline to evaluate smaller source-bar chunks and bounded verification, alongside additional independently reviewed examples. Do not use model self-reported doubt or successful internal validation as evidence that the music matches the PDF.
