# Notebook: research for a pen notebook with Excalidraw diagrams

Research for a personal notebook app (working name **Notebook**): handwritten notebooks on shelves, paper templates, real pens,
Excalidraw-style shapes while writing, full Excalidraw diagrams, and sync between the owner's Lenovo tablet and Windows PC. It
covers what's out there, what can be reused and under what license, what has to be built, and in what order. Researched on
8 November 2026, against Excalidraw's `master` at commit `701b901` (5 November 2026). Nothing is built yet.

> **Since then:** the owner has narrowed the first build to the canvas alone: a blank, pen-first canvas like excalidraw.com's,
> with no sync, recognition, notebooks or templates yet. That research is in [canvas.md](canvas.md). This document stays as
> the wider picture, for later.

> **Personal project.** Notebook is built for the owner's own devices. It isn't published to an app store or offered to anyone
> else, and it isn't affiliated with or endorsed by Excalidraw, Noteshelf, GoodNotes, StarNote, Lenovo or Google. Excalidraw is
> a trademark of Excalidraw s.r.o.; the diagram editor inside the app is the open-source Excalidraw package, used under its MIT
> license. Other product names belong to their owners. The source code is in the public Playground repository under its MIT
> license; the app itself isn't distributed. (This text is ready for the app's README and its About screen.)

**How sure we can be.** Each finding says where it comes from:

- **Source**: read in the code, license file or published package itself (Excalidraw's repo and npm builds, NuGet packages,
  MDN's browser compatibility data, GitHub's license detection).
- **Official**: the maker's own documentation or pages, some read through search summaries.
- **Weak source**: forums, blogs, retailer listings, comparison sites.
- **Estimate**: our own reasoning or number.
- **Unverified**: likely but not confirmed. Each one is a test in the first spikes ([section 8](#8-what-it-would-take)).

---

## Summary: the answers

1. **Excalidraw is MIT, so we can build on it.** The code is MIT ("Copyright (c) 2020 Excalidraw"), and its fonts are SIL OFL
   1.1 or MIT. We can bundle it, change it and ship it inside our app; the one duty is keeping its copyright and license text.
   What isn't ours to use: **Excalidraw+**, the paid hosted service, the servers behind excalidraw.com, and the **name and
   logo**, a registered trademark. The owner's read ("free to use, just not their thing") is right. (Source; the trademark:
   Weak source.)
2. **Nothing out there is this app, so it's a build, but not from scratch.** The closest free apps are Saber (handwriting with
   sync), Stylus Labs Write, Xournal++, and Obsidian with its Excalidraw plugin. None puts shelves, paper templates and Excalidraw
   diagrams in one app. The hard parts have permissively licensed building blocks: Excalidraw (diagrams, a shape recognizer,
   merging), perfect-freehand (pressure ink), roughjs (hand-drawn shapes), pdf.js (already in Prima), ML Kit (handwriting
   recognition) and the Google Drive API.
3. **This repo is public, so only MIT, Apache or BSD code goes into it.** Saber, Rnote and Xournal++ are GPL, Write and the
   Obsidian Excalidraw plugin are AGPL, and Obsidian Ink is CC BY-NC-ND. Using those apps is fine; pasting their code into this
   MIT-licensed public repo isn't. Learn from them, don't copy. tldraw, the other great canvas, needs a license key outside
   development. (Source.)
4. **Excalidraw's newest builds already do the "two circles and a line" idea.** Its `autoshape` tool (Shift+X) turns a sketch
   into a rectangle, ellipse, diamond, line or arrow, and a line drawn from one shape to another becomes an arrow bound to both.
   New host controls let our app hide Excalidraw's toolbar, drive its tools and lock the view to a box. All of this is only in
   the `next` npm build (`0.18.0-3c916c5`, 4 November 2026), not in `latest` (`0.18.1`, April 2026), and some of the changes
   are marked breaking. Pin an exact `next` build. (Source.)
5. **Excalidraw shouldn't be the handwriting engine.** Open issues report freezes past about 1,000 freehand strokes, pen lag
   next to Goodnotes, and missing tablet basics. In the code, pointer input reads neither coalesced nor predicted events, the
   eraser removes whole strokes, and there's no paper. A dense handwritten page can reach 1,000–2,000 strokes (Estimate).
   (Source.)
6. **Recommended: two kinds of page in one app.** **Notebook pages** run our own ink engine on paper templates, with
   Excalidraw-style shapes built in: a shape tool that hands back the pen, rough shapes that snap clean when the pen holds still,
   lines that become arrows between shapes, and writing that moves with the shape it's in. The recognizer is ported from
   Excalidraw's (MIT). **Diagram pages** are the real Excalidraw, with the same pen-first behaviours. Later, a diagram can sit
   inside a notebook page, and a lasso selection can become a diagram.
7. **The pen is well supported in the app's web view.** The Tab Pen Plus has 4,096 pressure levels, tilt and palm rejection
   (Weak source). Android's WebView gives pressure, tilt, every pen sample, predicted points and a low-latency canvas (Source:
   MDN's data). Android's native ink library, Jetpack Ink, is stable (1.0.0, December 2025) but has no .NET bindings. It's plan
   B, if the first spike finds the web too slow.
8. **Sync: each device keeps its own copy, and Google Drive is the shared folder.** No server to run. The non-sensitive
   `drive.file` scope needs no verification, but the Google project must be set to "In production" or sign-ins expire every 7
   days. Every stroke and shape carries a version, so two devices' edits merge element by element, by Excalidraw's own rule. One
   unknown: whether the PC can see files the tablet created when each signs in with its own OAuth client. (Official; the
   unknown is Unverified.)
9. **One app for both devices.** A .NET 10 MAUI Blazor Hybrid app like the repo's others, for Android and Windows (the Windows
   target builds on Windows only). The canvas is TypeScript, built once and kept in `wwwroot` the way Prima keeps pdf.js and
   Verovio, so Visual Studio needs no Node. Until the Windows app exists, the PC can open Drive's copies: PDF exports, and
   diagram pages as plain `.excalidraw` files at excalidraw.com.
10. **What it takes.** Three small spikes (pen latency, Excalidraw in the app, Drive sign-in on both devices), then a first
    usable notebook on the tablet: shelves, notebooks, pens, templates, saving, export. That's the largest step. Shapes, diagram
    pages, sync, the PC app, PDFs and handwriting search follow one at a time.

---

## 1. Excalidraw: the repository and its license

[excalidraw/excalidraw](https://github.com/excalidraw/excalidraw) is a monorepo: the editor component (`packages/excalidraw`,
published as `@excalidraw/excalidraw`), its building blocks (`packages/element`, `math`, `common`, `laser-pointer`), and the
excalidraw.com app (`excalidraw-app`). It has about 135,000 stars and is actively developed: the changelog has entries through
late October 2026, and the last push was on 7 November. (Source.)

### What's free and what isn't

| What | Terms | For us |
|---|---|---|
| The code, including the npm package | MIT, "Copyright (c) 2020 Excalidraw" (Source) | Use, change, bundle. Keep the notice. |
| Fonts: Excalifont (the hand-drawn default), Virgil (the old default), Xiaolai (Chinese, Japanese, Korean) | SIL Open Font License 1.1 (Source) | Bundle them. A font can't be sold on its own. |
| Font: Comic Shanns | MIT (Source) | Bundle. |
| Fonts: Cascadia, Nunito, Lilita One, Liberation | Open fonts; the repo carries no license text for them | Bundle; confirm each upstream license first (Unverified). |
| The libraries it builds on: perfect-freehand 1.2.0, roughjs 4.6.4, `@excalidraw/laser-pointer`, mermaid-to-excalidraw | MIT (Source) | Use them directly too. |
| excalidraw.com's servers: scene storage, the live-collaboration room, the AI endpoints | Excalidraw's hosted service | Not used: sync goes through Drive. The room server's code, `excalidraw-room`, is MIT if it's ever wanted. |
| Excalidraw+ | A paid hosted product, from $6 a month per user (Official: pricing page). Adds cloud storage and workspaces, more collaboration (voice and screen sharing, per reviews) and AI features (Weak source) | Not needed. |
| The name and logo | EXCALIDRAW is a registered word mark of Excalidraw s.r.o. (US registration 6,863,755, October 2022) (Weak source: trademark database listing). No published usage policy was found. | Don't name the app or a feature "Excalidraw" or use its logo. "Diagrams by Excalidraw (MIT)" in About is fine. |

MIT asks one thing: the copyright and license text go with "all copies or substantial portions" of the code. So Excalidraw's
`LICENSE` sits beside the bundled build, and the app's licenses screen lists it with the others.

### Which build to use

| npm tag | Version | Published | Has autoshape, `setViewport`, sticky notes? |
|---|---|---|---|
| `latest` | 0.18.1 | 20 April 2026 | No |
| `next` | 0.18.0-3c916c5 | 4 November 2026 | Yes |

(Source: both packages downloaded and searched.) CI publishes `next` from the `release` branch. The changelog's "Unreleased"
section lists breaking changes since 0.18.0 (March 2025): the `excalidrawAPI` prop is now `onExcalidrawAPI`, `scrollToContent`
became `setViewport`, and on 18 October 2026 the three export functions became one `exportScene`. So pin an exact `next`
version, keep every Excalidraw call in one small host module of ours, and read the changelog at each upgrade.

### What it gives a host app

All read in the source at `701b901`:

| Feature | What it does | What we'd use it for |
|---|---|---|
| `ui={false}` (July 2026) | Hides Excalidraw's toolbar, menus, sidebars, popups and context menu; the host's own UI still renders | Our pen toolbar, in our look |
| `activeTool` prop (July 2026) | The host sets the tool, and a tool it sets stays active after use | "Circle, then back to the pen", decided by us |
| `setActiveTool(tool, { toggle })` | Hand and eraser switch back to the tool before them | Erase, then straight back to writing |
| `setViewport({ target, fit, lock })` (July 2026) | Fits and locks pan and zoom to a box, with an optional rubber band | Keep a diagram, or a page, in view |
| `autoshape` tool, Shift+X | Recognizes a sketch as a rectangle, ellipse, diamond, line or arrow, previewing the shape while you draw. A line that starts or ends on a shape becomes an arrow bound there; a line inside a single shape stays a line; a stroke under 25 px on screen stays as drawn | The "two circles and a line" flow |
| Pen mode | Turns on at the first pen touch: fingers pan instead of drawing, and freehand strokes follow pressure | Basic palm rejection |
| Freehand strokes `variable` or `constant` | Pressure-shaped (perfect-freehand) or even width | A fountain pen and a ballpoint |
| Lasso, eraser, laser pointer, frames, sticky notes (September 2026), bucket fill, Mermaid paste, flowchart keys | The diagram tools | Diagram pages get them all |
| `onChange`, `onIncrement` (each lasting change, with its delta), `onPointerDown`, `onPointerUp`, `onScrollChange`, a `selection` API | Change and input hooks | Autosave, the back-to-pen switch, syncing changes |
| `serializeAsJSON`, `restore`, `loadFromBlob`, `exportScene` (SVG, PNG, canvas), `convertToExcalidrawElements` | Files in and out; elements from a short description, binding arrows to shapes by id | Saving, thumbnails, turning notebook content into a diagram |
| `reconcileElements` | Merges two copies of a scene, element by element | Sync |
| Element fields `version`, `versionNonce`, `isDeleted`, `index`, `groupIds`, `frameId`, `locked`, `customData` | Versions for merging, a fractional z-order, groups, and room for our own data on any element | Sync, and pen settings kept on each stroke |
| `window.EXCALIDRAW_ASSET_PATH` | Where the fonts load from | Fonts served from the app, offline |

It needs React 17, 18 or 19 beside it.

### Where it falls short for handwriting

- **Lots of strokes.** [#5788](https://github.com/excalidraw/excalidraw/issues/5788), open since October 2022: Obsidian on an
  iPad freezes when loading, or zooming out on, a drawing with about 1,000 or more freehand strokes.
  [#6474](https://github.com/excalidraw/excalidraw/issues/6474), "Excalidraw getting slow in Obsidian", is open too.
- **Pen lag.** [#11323](https://github.com/excalidraw/excalidraw/issues/11323), May 2026, open: strokes feel delayed and choppy
  next to Goodnotes or Apple Notes.
- **Tablet gaps.** [#9705](https://github.com/excalidraw/excalidraw/issues/9705), open: no stylus buttons, inconsistent palm
  rejection, slow with many strokes, no gestures such as two-finger undo.
- **In the code.** Pointer handling calls neither `getCoalescedEvents` nor `getPredictedEvents` (no match anywhere in
  `packages/`), so a fast pen gives one point per frame and nothing to hide the lag. The eraser removes whole elements. The
  background is a colour and an optional grid. With the freehand tool in pen mode, a pinch pans but doesn't zoom (the code pins
  the scale to 1).

A dense page of handwriting, a few hundred words at two to five strokes each, comes to 1,000–2,000 strokes, and a page of
character practice can hold as many (Estimate). That's where #5788 begins. So Excalidraw does the diagrams, and notebook pages
get an engine built for ink ([section 7](#7-recommended-shape-of-the-app)).

---

## 2. What else is out there

### Apps to try, or learn from

| App | License | Runs on | Good at | For us |
|---|---|---|---|---|
| [Saber](https://github.com/saber-notes/saber) | GPL-3.0 | Android, iOS, Windows, macOS, Linux (Flutter) | Handwriting; a highlighter that sits under the ink and doesn't darken where it overlaps; a dark mode that inverts notes, images and PDFs; nested folders; encrypted sync through Nextcloud, its server or yours | The free app to try today. Ideas only. |
| [Stylus Labs Write](https://github.com/styluslabs/Write) | AGPL-3.0 | Windows, macOS, Linux, iOS, Android | Editing and organizing handwritten notes | Ideas only. |
| [Xournal++](https://github.com/xournalpp/xournalpp) | GPL-2.0 | Linux, macOS, Windows | Handwriting and PDF annotation | Ideas only. |
| [Rnote](https://github.com/flxzt/rnote) | GPL-3.0 | Desktop | Sketching and handwritten notes | Ideas only. |
| Obsidian with the [Excalidraw plugin](https://github.com/zsviczian/obsidian-excalidraw-plugin) | Plugin AGPL-3.0; Obsidian is closed source, free for personal use | Android, Windows and more | Excalidraw drawings inside Markdown notes | The no-build route to diagram notes. Ideas only. |
| [Obsidian Ink](https://github.com/daledesilva/obsidian_ink) | CC BY-NC-ND 4.0 | Obsidian | Handwriting inside Markdown notes, on tldraw | Look, don't adapt. |
| [Joplin](https://github.com/laurent22/joplin) | AGPL-3.0-or-later | All | A notes app whose drawing editor is js-draw | Ideas only. |
| [Drawnix](https://github.com/plait-board/drawnix) | MIT | Web | Whiteboard: mind maps, flowcharts, freehand | Ideas. |
| [AFFiNE](https://github.com/toeverything/AFFiNE) | Mixed | Web, desktop | Documents that switch into a whiteboard | The idea of one note in two modes. |
| StarNote, Noteshelf, GoodNotes, Notability, Samsung Notes | Commercial | | The feature bar to aim at | Ideas only. |

StarNote is probably the "PenStar" the owner remembers: an Android tablet app for handwriting and PDFs, with planner templates
and an infinite canvas, sold as a one-time purchase (Weak source: store listings).

### Libraries we could build on

| Library | License | What it is | Verdict |
|---|---|---|---|
| [Excalidraw](https://github.com/excalidraw/excalidraw) | MIT | The diagram editor | Use, for diagram pages. |
| [perfect-freehand](https://github.com/steveruizok/perfect-freehand) | MIT | Pressure-shaped stroke outlines; Excalidraw draws its freehand strokes with it | Use, for the ink engine. |
| [roughjs](https://github.com/rough-stuff/rough) | MIT | Excalidraw's hand-drawn look for shapes | Use, so shapes on pages match diagrams. |
| [pdf.js](https://github.com/mozilla/pdf.js) | Apache-2.0 | PDF rendering; already in Prima | Use, for PDFs to write on. |
| [jsPDF](https://github.com/parallax/jsPDF) | MIT | Writing PDFs | Use, for export. |
| [js-draw](https://github.com/personalizedrefrigerator/js-draw) | MIT | A whole drawing editor: pens with custom types, stabilization and shape autocorrect, backgrounds, a very large zoom range | Considered as the page engine. Its own editor model and toolbar, one maintainer and a quiet 2026 (last push in March) weigh against it. Read it for ideas. |
| [tldraw](https://github.com/tldraw/tldraw) | tldraw license: no production use without a key. A free hobby key is for non-commercial projects, granted case by case, and keeps a "made with tldraw" watermark (Official) | The most flexible canvas SDK, with good pen support | Not chosen: it needs a key, and the owner wants Excalidraw. |
| [Jetpack Ink](https://developer.android.com/jetpack/androidx/releases/ink) (engine: [google/ink](https://github.com/google/ink)) | Apache-2.0 | Android's native low-latency ink | Plan B for pen latency ([section 3](#3-the-pen)). |
| [ML Kit Digital Ink Recognition](https://developers.google.com/ml-kit/vision/digital-ink-recognition) | Free Google SDK, closed source | Handwriting to text, on the device | Later, for search ([section 4](#4-excalidraw-style-shapes-while-writing)). |

### What the licenses mean here

The Playground repo is public on GitHub, under MIT. So:

- **MIT, Apache, BSD:** bundle or copy, and keep the notice.
- **GPL and AGPL:** run the apps as much as you like. Their code can't be pasted into this repo without making the app GPL or
  AGPL too, so read them for ideas and write our own.
- **CC BY-NC-ND:** look only; no changes, no reuse.
- **tldraw:** free in development; anywhere else it needs a key. Its docs define production as HTTPS on a domain other than
  localhost, built for production (Official). An app inside a web view isn't on localhost, so it would count (Unverified).
- **Keep secrets out.** The Google OAuth client ID and secret go in a local, untracked settings file, not in the repo.

---

## 3. The pen

### The hardware

The tablet is a Lenovo Idea Tab Pro Gen 2 (13 inches, 3.5K at 144 Hz, Snapdragon 8s; from Prima's plan). Its pen, the Lenovo
Tab Pen Plus, has 4,096 pressure levels, tilt and palm rejection (Weak source: retailer listings). No latency figure is
published. Still to find out on the device (Unverified): whether the pen reports hover, and what its button sends.

### In the app's web view

The app's screens run in Android's WebView (Chromium), as Prima's do. MDN's compatibility data gives (Source):

| Pointer feature | Since Chrome | What it's for |
|---|---|---|
| `pressure`, `tiltX`, `tiltY` | 55 | Pressure-shaped pens, tilt shading |
| `altitudeAngle`, `azimuthAngle` | 86 | The same tilt, as angles |
| `getCoalescedEvents()` | 58 | Every pen sample, not just one per frame |
| `getPredictedEvents()` | 77 | Points the pen is about to reach, drawn ahead to hide the lag |
| Canvas `desynchronized: true` | 75 on Android | A canvas that reaches the screen without waiting for the rest of the page |
| Delegated ink trail (`navigator.ink`) | 94, experimental | Lets the system draw the newest bit of stroke; any benefit on Android is unknown |

For Android's WebView, MDN mirrors Chrome's data rather than testing it separately, so the pen spike checks each one on the
tablet.

So the ink engine takes every coalesced sample, draws the wet stroke on the low-latency canvas as samples arrive, and draws the
predicted points as a short tail it drops at the next sample. On pen-up, the finished stroke goes into the page's cached ink
layer. Fingers pan and zoom; only the pen draws, unless finger drawing is turned on.

To measure it, film the pen tip at 240 frames a second (most phones' slow motion: about 4 ms a frame) while drawing quick lines,
and count the frames between the tip and the ink. Do the same in a native note app on the same tablet for comparison.

### Native, if the web isn't fast enough

Jetpack Ink reached 1.0.0 on 17 December 2025, with 1.1 alphas through 2026 (Official). Its `InProgressStrokesView` draws wet
ink at the lowest latency Android offers. It's Kotlin, and NuGet has no .NET bindings for `androidx.ink`, `graphics-core` or
`input-motionprediction` (Source: searched). Using it means writing a binding or a thin Kotlin wrapper, then a native wet-ink
layer over the web view that hands each finished stroke to the web engine to draw the same way. That's real work, so it's plan
B.

---

## 4. Excalidraw-style shapes while writing

The owner's flow, step by step:

1. **Tap the circle, drag out its size, lift, and you're writing again.** On notebook pages a shape tool is one-shot: it hands
   back the pen you had. Long-press the button to keep it, like Excalidraw's padlock. In Excalidraw a shape tool returns to
   *selection* unless locked, so on diagram pages our host puts the freehand pen back when the shape is done, through the
   `activeTool` prop.
2. **Write inside the circle, and the writing belongs to it.** Strokes that fall inside a closed shape attach to it, and move
   and resize with it. Excalidraw binds only typed text to a shape, so on diagram pages the strokes are grouped with the shape
   instead (Unverified: whether grouping feels right).
3. **Sketch a rough shape, and it comes out clean.** Excalidraw's recognizer (MIT, `packages/element/src/convertToShape.ts`)
   reads a stroke's shape statistically. A closed stroke (its ends within 15% of its length) becomes a rectangle, diamond or
   ellipse; an open, straight one becomes a line, or an arrow if it has a head. We port it. The trigger is holding the pen still
   at the end of the stroke, as GoodNotes' "Draw and Hold" does (Official: GoodNotes' blog), so writing is never converted by
   accident. About half a second (Estimate; tune it). The other option is a "shape pen" that converts every stroke, like
   Excalidraw's autoshape tool.
4. **Draw a line from one circle to another, and it becomes an arrow between them.** Excalidraw's rule: a recognized line that
   starts or ends on a shape becomes an arrow bound there, and its ends follow the shapes when they move.
5. **Later, handwriting to text.** ML Kit Digital Ink Recognition works on the device once a language's model is downloaded,
   for 300+ languages and 25+ writing systems, and has a shapes model too: rectangle, triangle, arrow, ellipse (Official). Its
   .NET binding, `Xamarin.Google.MLKit.DigitalInk.Recognition` 119.0.0.4, was updated in September 2026 (Source: NuGet). It's
   Android only: the tablet recognizes, and the text syncs to the PC with the page. For language notebooks that means searching
   your own handwriting, and turning it into typed text.

---

## 5. The notebook

### Shelves, notebooks, pages

- **Shelves** the owner names (Work, Languages, a job's own shelf), each a row of notebooks with covers. Recent notebooks on
  top; notebooks move between shelves; old ones get archived.
- **Notebooks** hold pages, optionally in sections, with a page strip of thumbnails: add, duplicate, reorder, delete, bookmark.
- **Pages** come in kinds: a *notebook page* (paper and ink), a *diagram page* (Excalidraw), and later a *PDF page* (an imported
  page to write on).

### Templates

Generated from a few settings (spacing, margin, colour), so they're tiny and sharp at any zoom, and they follow the look:

- Blank, lined (narrow, college, wide; with or without a margin), squared, dotted, Cornell, checklist, meeting notes, day and
  week planners, music staff.
- For language notebooks: 田字格 and 米字格 practice squares (Chinese), genkō yōshi manuscript squares (Japanese), a hangul
  practice grid, four-line handwriting ruling (Latin and Cyrillic), and a two-column vocabulary page.
- The owner's own: any PDF or image as a template.

### Pens and tools

| First version | Next | Later |
|---|---|---|
| Ballpoint (even width), fountain pen (pressure), highlighter, stroke eraser, lasso (move, resize, recolour, copy, delete), undo and redo, pinch zoom and two-finger pan, favourite colours and widths, one-shot shapes, pen-only drawing | Hold-to-snap shapes, arrows between shapes, partial eraser, pencil, brush, text boxes, images, sticky notes, laser pointer, the pen's button as the eraser, two-finger tap to undo, a zoom box for small writing | PDFs to write on (pdf.js), PDF export (jsPDF), handwriting search, a diagram inside a notebook page, a lasso selection turned into a diagram, links between pages |

### The look

The library and the toolbars use the Kit. Logbook (warm paper, serif print, a vermilion pen) suits a notebook as it is, and
Telemetry gives a dark version. Pages stay paper in both, with a dark-paper option that inverts the ink, as Saber does.

---

## 6. Sync between the tablet and the PC

### The options

| Option | How it works | Good | Bad |
|---|---|---|---|
| **A Google Drive folder (recommended)** | Each device keeps its own copy and syncs page files through a Drive folder, with the Drive API | No server; works away from home; on the PC the files also show up through Google Drive for desktop | Google sign-in setup; uses Drive storage |
| The PC as the server | A small sync service on the PC; the tablet syncs on home Wi-Fi | No Google; fast | The PC must be on and reachable; only at home without a VPN |
| The tablet as the server | The PC opens the tablet's app over Wi-Fi | One copy | Android stops background servers; the tablet must stay awake |
| Syncthing | Peer-to-peer folder sync; the app just writes files | No sync code | The official Android app is discontinued and a community fork carries on (Weak source); clashing edits still need merging |
| A web app on the PC | The tablet uses its browser | One install | The PC must be on; no offline use |

"Each is its own server, then they sync" is what the recommended option does: both devices are complete on their own, and Drive
is the mailbox between them. The sync code sits behind one interface, so a PC-server transport could be added later.

### Google Drive in practice

- **Scopes** (Official): `drive.file` (non-sensitive) reaches only files the app created or the user opened with it;
  `drive.appdata` (non-sensitive) is a hidden folder for the app; `drive` (restricted) reaches everything.
- **Testing or production.** While the Google Cloud project's consent screen is in "Testing", refresh tokens expire after 7
  days, so you'd sign in every week. Set it to "In production" (Official, quoted in Google's API forum), and make fresh
  credentials after the switch (Weak source: forum).
- **Verification.** Non-sensitive scopes need none (Official). A personal app (fewer than 100 users, all known to you) may use
  even restricted scopes unverified, with a one-time "Google hasn't verified this app" screen and a lifetime cap of 100 users
  (Official).
- **Signing in.** On Windows: the Google .NET library's browser-and-loopback flow, with a Desktop OAuth client. On Android:
  Play services' Authorization API, which the `Xamarin.GooglePlayServices.Auth` 122.0.0 binding exposes for .NET 10 (Source:
  NuGet, September 2026), with an Android OAuth client tied to the app's package name and signing key. MAUI's `WebAuthenticator`
  with a custom-scheme redirect is the fallback (Weak source). The Drive calls use `Google.Apis.Drive.v3` 1.78 on both devices;
  that library's own sign-in helper doesn't work on Android (Weak source).
- **The unknown.** With `drive.file`, can the PC's OAuth client see the files the tablet's client created? One forum report
  suggests access may be per client (Weak source), and nothing official was found. The Drive spike answers it. If it's no, the
  options are one Desktop client on both devices, the hidden `drive.appdata` folder, or the full `drive` scope as a personal app.
  The full scope also lets the app see PDFs the owner drops into the folder from the PC.

### Merging two devices' edits

- Every stroke, shape, text box and diagram element has an id, a version, a random tiebreaker (`versionNonce`), a deleted flag
  kept as a tombstone, and a fractional z-order `index`, as Excalidraw's elements do.
- A merge keeps the copy with the higher version, and on a tie the lower tiebreaker: `reconcileElements`' rule (Source). A
  delete on one device beats an older copy on the other, because the tombstone is the newer version.
- Page order and shelves use fractional indexes too, so two devices adding or reordering pages don't clash.
- Images and PDFs are named by their hash, so they never clash.
- One file per page: a sync uploads only the pages that changed, and Drive's changes feed lists what changed on the other side.
- Strokes are stored compactly (rounded, delta-encoded, compressed); a dense page in plain JSON runs to megabytes (Estimate).
- Diagram pages are plain `.excalidraw` files, so on the PC they open at excalidraw.com with no account.

The merge goes in `Notebook.Core`, with the repo's usual tests.

---

## 7. Recommended shape of the app

```
Notebook/
  Notebook.slnx
  README.md                    Building, running, putting it on the devices; the personal-project notice.
  docs/                        This research; then concept.md and plan.md.
  src/Notebook.Core/           net10.0: shelves, notebooks, pages and their elements; templates; merging; the sync engine.
                               All of it tested.
  src/Notebook.UI/             Razor class library: the screens, the Kit's looks, the local store, the bridge to the canvas.
    wwwroot/canvas/            The built canvas (the ink engine and the Excalidraw host) with its licenses, committed.
  src/Notebook.App/            MAUI Blazor Hybrid: net10.0-android for the tablet, net10.0-windows for the PC (built on
                               Windows only). Google sign-in, files, and later ML Kit.
  tools/canvas/                The canvas's TypeScript source and its build (esbuild), output to src/Notebook.UI/wwwroot/canvas.
  tools/Notebook.Preview/      The screens as a Blazor Server page with sample notebooks, for checking from the cloud.
  tests/Notebook.Core.Tests/   xUnit v3.
```

- **One MAUI project for both devices.** The repo's other apps have a host per device because their screens differ. Here the
  tablet and the PC run the same screens, so one project with two targets is simpler (split into `Notebook.Tablet` and
  `Notebook.Desktop` if they ever diverge). On Windows, MAUI's web view is WebView2, the same engine, so the canvas behaves alike.
- **The canvas is TypeScript, built once.** The output goes into `wwwroot` and is committed, the way Prima keeps pdf.js and
  Verovio, so Visual Studio builds need no Node. The engine's geometry (outlines, hit testing, the recognizer) gets its own tests
  in `tools/canvas`.
- **Who owns what.** Blazor owns the library, the settings, the files and sync. While a page is open, the canvas owns it, and
  hands back the page (autosaved after a short pause) and its thumbnail.
- **A page's layers**, bottom to top: paper (the template), a PDF or image background, highlighter, cached ink, shapes and text,
  the wet stroke on the low-latency canvas, and selection handles.
- **Diagram pages** mount Excalidraw in a React root inside the same web view, with fonts served from the app, saving through
  `onChange` and `serializeAsJSON`.
- **The PC can wait.** Drive for desktop already shows the PDF exports and `.excalidraw` files on the PC, so the Windows target
  can come after sync.

---

## 8. What it would take

Sizes are our estimate: **S** is about one session, **M** a few, **L** a week or more of sessions.

### First, three spikes (S each)

| Spike | Question | Done when |
|---|---|---|
| Pen | Is ink in the web view fast and clean enough on the tablet? | Latency measured against a native note app; pressure, tilt, hover, the button and palm rejection checked. If it's too slow, plan B ([section 3](#3-the-pen)). |
| Excalidraw in the app | Does the `next` build run offline in the web view, and fast enough? | A diagram page saves and reopens, a shape hands back the pen, autoshape works with the pen, and a 1,500-stroke drawing is timed. |
| Drive | Does sign-in work on both devices, and can each see the other's files? | The tablet writes a file and the PC lists it; both are still signed in after 7 days. |

### Then the app, in order

| Step | Size | What the owner gets |
|---|---|---|
| 1. A first notebook | L | Shelves, notebooks with covers, pages with templates, ballpoint, fountain pen and highlighter, stroke eraser, lasso, undo, zoom, autosave, thumbnails, PDF and PNG export. Tablet only. The largest step, mostly the ink engine. |
| 2. Shapes on pages | M | One-shot shapes, hold-to-snap, arrows between shapes, writing that moves with its shape. |
| 3. Diagram pages | M | Excalidraw with our toolbar, pen first, saved per page. |
| 4. Sync | L | Drive sync with per-element merging, a sync status, and tests for clashing edits. |
| 5. The PC | M | The same app on Windows, comfortable with a mouse and keyboard. |
| 6. PDFs | M | Import a PDF and write on it; export it. |
| 7. Handwriting | M | Recognition on the tablet, search, and conversion to text. |
| 8. Polish | Ongoing | Zoom box, partial eraser, pencil, diagrams inside pages, a selection turned into a diagram, links between pages. |

---

## 9. Risks

- **Pen feel in a web view.** If it lags, plan B (a native wet-ink layer) is a binding project of its own. That's why the pen
  spike comes first.
- **Excalidraw's moving API.** The features we want are unreleased, and some changes are breaking. Pin, wrap, upgrade on
  purpose.
- **Google sign-in on Android from .NET.** The least-trodden path here, so it gets a spike before any sync work.
- **Merge mistakes lose ink.** The merge is small and pure, in Core, with a test for each kind of clash.
- **Scope.** Notebook apps grow without end. The first version is the first column of the table in section 5, nothing more.
- **Big notebooks.** Only the open page is live and the rest are thumbnails, so memory stays flat as notebooks grow.

---

## 10. For the owner to decide

1. **Two kinds of page (recommended), or Excalidraw on every page.** Excalidraw everywhere is quicker to build but carries the
   handwriting limits in section 1; the Excalidraw spike's 1,500-stroke test shows how much they matter on this tablet.
2. **The name.** "Notebook" is a working name.
3. **Sync.** Google Drive (recommended), or the PC as a home server.
4. **Drive access.** The narrow `drive.file`, or the full `drive` scope as a personal app: one warning screen at sign-in, and
   the app sees PDFs dropped in from the PC.
5. **The PC.** The full app once sync works, or Drive's copies for a while.
6. **The look.** The Kit's Logbook and Telemetry, or a look of its own, as Prima has.
7. **The first templates,** and which languages the language notebooks are for. That picks the practice grids and the
   recognition models.
8. **Pens for the first version,** beyond ballpoint, fountain pen and highlighter.

---

## References

### Excalidraw

- Repository, read at commit `701b901` (5 November 2026) (Source): https://github.com/excalidraw/excalidraw. Files read:
  `LICENSE`, `packages/excalidraw/CHANGELOG.md`, `packages/element/src/convertToShape.ts`,
  `packages/excalidraw/components/App.drawshape.ts`, `packages/excalidraw/components/App.tsx`,
  `packages/excalidraw/components/Tools.tsx`, `packages/element/src/shape.ts`, `packages/element/src/types.ts`,
  `packages/element/src/transform.ts`, `packages/element/src/store.ts`, `packages/excalidraw/data/reconcile.ts`,
  `packages/excalidraw/types.ts`, `packages/excalidraw/index.tsx`, `packages/excalidraw/fonts/`.
- npm packages `@excalidraw/excalidraw` 0.18.1 and 0.18.0-3c916c5, downloaded and searched, with their publish dates (Source):
  https://www.npmjs.com/package/@excalidraw/excalidraw
- Issues (Source, user reports): [#5788](https://github.com/excalidraw/excalidraw/issues/5788),
  [#6474](https://github.com/excalidraw/excalidraw/issues/6474), [#9705](https://github.com/excalidraw/excalidraw/issues/9705),
  [#11323](https://github.com/excalidraw/excalidraw/issues/11323).
- Virgil, OFL-1.1 (Source): https://github.com/excalidraw/virgil
- Excalidraw+ pricing (Official): https://plus.excalidraw.com/pricing. What the free plan lacks (Weak source):
  https://costbench.com/software/diagramming/excalidraw/free-plan/
- The EXCALIDRAW trademark (Weak source: trademark database): https://trademarks.justia.com/970/10/excalidraw-97010906.html

### Other apps and libraries

- Saber README (Source): https://github.com/saber-notes/saber
- Stylus Labs Write README (Source): https://github.com/styluslabs/Write
- js-draw README (Source): https://github.com/personalizedrefrigerator/js-draw
- tldraw `LICENSE.md` (Source): https://github.com/tldraw/tldraw/blob/main/LICENSE.md. License keys and the hobby license
  (Official): https://tldraw.dev/sdk-features/license-key, https://tldraw.dev/get-a-license/hobby
- Joplin `LICENSE` (Source): https://github.com/laurent22/joplin/blob/dev/LICENSE
- Obsidian Ink `LICENSE` (Source): https://github.com/daledesilva/obsidian_ink/blob/main/LICENSE
- Licenses of Xournal++, Rnote, OpenBoard, Drawnix, AFFiNE, the Obsidian Excalidraw plugin, perfect-freehand, roughjs, pdf.js,
  jsPDF, google/ink and excalidraw-room: GitHub's license detection, through its search API (Source).
- GoodNotes, "Draw and Hold to Create Perfect Shapes" (Official): https://www.goodnotes.com/blog/shape-recognition
- StarNote listings (Weak source): https://apps.apple.com/us/app/starnote-handwriting-pdf/id6751570915,
  https://www.stork.ai/en/starnote

### The pen

- MDN browser compatibility data for `PointerEvent`, `HTMLCanvasElement.getContext` and `DelegatedInkTrailPresenter`
  (Source): https://github.com/mdn/browser-compat-data
- Jetpack Ink releases (Official): https://developer.android.com/jetpack/androidx/releases/ink,
  https://github.com/google/ink/releases/tag/jetpack-1.0.0
- Lenovo Tab Pen Plus specs (Weak source: retailers): https://www.bestbuy.com/product/lenovo-tab-pen-plus-gray/J3PT7KCH8T,
  https://shop.captech.se/en-GB/Article/129004
- ML Kit Digital Ink Recognition (Official): https://developers.google.com/ml-kit/vision/digital-ink-recognition, base models
  https://developers.google.com/ml-kit/vision/digital-ink-recognition/base-models
- NuGet (Source): `Xamarin.Google.MLKit.DigitalInk.Recognition`, `Xamarin.GooglePlayServices.Auth` (its .NET 10 assembly
  searched for the Authorization API), `Google.Apis.Drive.v3`, and searches for AndroidX Ink bindings. https://www.nuget.org

### Sync and Google

- Choose Google Drive API scopes (Official): https://developers.google.com/workspace/drive/api/guides/api-specific-auth
- Store application-specific data (Official): https://developers.google.com/workspace/drive/api/guides/appdata
- Unverified apps, and when verification isn't needed (Official): https://support.google.com/cloud/answer/7454865,
  https://support.google.com/cloud/answer/13464323
- Refresh tokens expiring in Testing (Official, quoted in Google's API forum): https://groups.google.com/g/adwords-api/c/EXh0Cjg5auw.
  (Weak source) https://newscrewdriver.com/2023/02/04/google-oauth-test-tokens-expire-weekly/
- `drive.file` across client IDs (Weak source: forum):
  https://forum.rclone.org/t/google-drive-changing-client-id-and-scope-drive-file-write-access/6693
- MAUI and Google OAuth (Weak source): https://dev.to/adiamante/net-maui-google-drive-oauth-on-windows-and-android-4lm4,
  https://medium.com/@CORDEA/using-net-google-api-client-library-with-maui-11ae98e34a35
- Syncthing on Android (Weak source): https://f-droid.org/en/packages/com.github.catfriend1.syncthingfork,
  https://forum.syncthing.net/t/does-anyone-know-why-syncthing-fork-is-no-longer-available-on-github/25661/144
