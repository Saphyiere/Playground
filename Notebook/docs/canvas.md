# The canvas: research for a pen-first drawing and diagram canvas

The owner has narrowed the first build to one thing: a blank, infinite canvas like excalidraw.com's free one, made to feel
great with the pen on the tablet. This document researches that alone: whether to use Excalidraw, take parts of it, or build
our own; what makes a pen feel good, in numbers; how the canvas would be built; the screen; and the first steps. The wider
research (sync, notebooks, templates, recognition) is in [research.md](research.md), for later. Researched on 8 November 2026,
against Excalidraw's `master` at commit `701b901`. Nothing is built yet.

The labels are the ones in research.md (**Source**, **Official**, **Weak source**, **Estimate**, **Unverified**), plus
**Study**: a peer-reviewed paper, read in its abstract.

## Decided by the owner

- **The first build is the canvas.** An infinite blank canvas for drawing and diagrams, pen first, on the tablet. Nothing
  else yet: no Google Drive or sync, no handwriting recognition, no notebooks or templates.
- **The screen:** pages down the left side, the tools across the top (select and the rest), and a small menu.
- **Good defaults built in,** so it works well without setting anything up.
- **Excalidraw is optional.** It's a guide and a format to export to. Take parts of it if they help; build our own if it
  limits the pen.

---

## Summary

1. **Don't embed the Excalidraw editor; build our own canvas, shaped like Excalidraw.** The editor's pen path is the part we
   can't change from outside. On every pen move, the stroke goes through React and is redrawn whole. Each finished stroke gets
   its own bitmap, and all of them are rebuilt each time the zoom settles. It reads neither coalesced nor predicted pen events.
   Fixing that means forking an editor of about 140,000 lines whose API changes month to month. (Source.)
2. **Take the parts that help, all MIT.** Its file format: our shapes are Excalidraw elements, so a page exports as a
   `.excalidraw` file that opens at excalidraw.com. Its hand-drawn look: roughjs with the same settings. Its pen outlines:
   perfect-freehand and `@excalidraw/laser-pointer`. Its shape recognizer, ported. Its font, Excalifont (OFL). Its element
   package is published on its own, without React, and is there for advanced arrows later. It isn't a stable public API, so
   start without it. (Source.)
3. **What "feels good" means in numbers.** In writing and sketching, people notice lag from about 50 ms. Side by side, they
   can tell 7 ms from 40 ms while scribbling. Commercial pens measured 65–120 ms end to end in 2014. Android's front-buffered
   rendering claims about 4 ms from pen to screen. At 144 Hz one frame is 6.9 ms, so every frame of waiting shows. (Study;
   Official.)
4. **The web view can be fast if the engine is built for it.** That means every pen sample (coalesced events) and predicted
   points. The stroke being drawn goes on a desynchronized canvas with nothing on top of it, drawn straight from the pointer
   event with no framework in the way. Finished strokes are cached in tiles, so a page's stroke count stops mattering. The
   owner's layout (tools on top, pages on the left, nothing floating over the canvas) is exactly what that low-latency canvas
   needs. (Official; the design is an Estimate.)
5. **Plan B is small and contained:** a native layer over the web view that draws only the stroke being made, with Android's
   low-latency library. A fully native canvas isn't the fallback. .NET has nothing like perfect-freehand or roughjs, so it
   would mean porting both and rewriting everything else. (Source.)
6. **The first version** has:
   - an infinite canvas, with pages on the left
   - ballpoint, fountain pen, marker and highlighter; an eraser; select and lasso
   - rectangle, ellipse, diamond, line and arrow tools that hand back the pen
   - rough shapes that snap clean when the pen holds still, arrows that stay attached to shapes, and text
   - undo, autosave, and export to PNG, SVG and `.excalidraw`
7. **Start with a bake-off on the tablet.** Over a day or two, compare our web pen path, Excalidraw's, and a native app,
   filmed in slow motion. It decides whether plan B is needed before anything else is built.

---

## 1. Excalidraw: embed it, take parts, or build our own

### The three ways

| Way | What it means | Pen feel | Effort | Verdict |
|---|---|---|---|---|
| Embed the editor | `@excalidraw/excalidraw` in the web view, with our toolbar through `ui={false}` and `activeTool` | Excalidraw's, as it is (below) | Smallest at the start | No: the pen path is the one part we can't change |
| Fork the editor | Rework its input and rendering | Fixable | A fork of about 140,000 lines (`App.tsx` alone is 11,500) that changes weekly | No |
| **Our own canvas, shaped like Excalidraw** | Our input, rendering, tools and screen; Excalidraw's file format, look and a few of its pieces | Ours to tune | The ink engine and the tools are ours to write | **Yes** |

### Why its pen path is the limit

All read in the source (Source):

- **The stroke goes through React.** The stroke being drawn lives in React state. A React effect redraws the whole stroke
  after every render (`components/canvases/NewElementCanvas.tsx`), so each pen move waits for React.
- **One bitmap per stroke.** Each finished stroke is drawn into its own offscreen canvas. During a pinch the old bitmaps are
  stretched, and once the zoom settles at a new level they are all rebuilt (`element/src/renderElement.ts`). With 2,000
  strokes, that's 2,000 bitmaps rebuilt after every zoom. It matches the freezes reported in
  [#5788](https://github.com/excalidraw/excalidraw/issues/5788).
- **One point per frame.** Pointer handling calls neither `getCoalescedEvents` nor `getPredictedEvents` anywhere in
  `packages/`. tldraw, by comparison, reads coalesced events in its canvas event hook (`useCanvasEvents.ts`).
- **Tablet basics missing:** the pen's button, two-finger undo and consistent palm rejection, per
  [#9705](https://github.com/excalidraw/excalidraw/issues/9705).

### How big Excalidraw is

| Package | Lines of TypeScript (tests and translations left out) |
|---|---|
| `excalidraw`: the editor, in React | 93,700 |
| `element`: elements, geometry, arrow binding, rendering | 38,800 |
| `common` | 3,900 |
| `math` | 2,400 |
| `utils` | 600 |
| `laser-pointer` | 500 |
| `fractional-indexing` | 300 |

(Source.) Inside `element`, arrow binding alone is 3,500 lines, the line and arrow editor 2,900, and elbow arrows 2,400.
That's what "build Excalidraw" means in full. The first version needs a small part of it: our arrows attach to shapes the
simple way (an end sits on the shape's outline, aimed along the arrow, and follows when the shape moves), with no elbow
routing.

### What to take from it

| Part | How | Why |
|---|---|---|
| The `.excalidraw` file format | Our elements use Excalidraw's fields for the shapes both have, and export writes `{ type: "excalidraw", version: 2, elements, appState, files }`. Our pens map onto its two freehand styles (pressure-shaped or even width), with opacity for the highlighter; our own pen settings ride along in `customData`. | Drawings open at excalidraw.com, whose `restore` fills in any field we leave out (Source). |
| The hand-drawn look | roughjs (MIT) with the same options per shape (seed, roughness, stroke style, fill) | Shapes look like Excalidraw's. |
| Pen outlines | perfect-freehand (MIT) for pressure pens; `@excalidraw/laser-pointer` 1.3.2 (MIT, on npm) for even-width strokes and a laser pointer | The same stroke shapes Excalidraw draws. |
| The shape recognizer | Port `recognizeShape` from `element/src/convertToShape.ts`, keeping its MIT notice | Hold-to-snap, and lines that become arrows. |
| Excalifont | Bundle it (OFL 1.1) | Hand-drawn text. |
| Its defaults | Zoom from 10% to 3,000%, stroke smoothing 0.5 (`common/src/constants.ts`) | Starting values to tune from. |
| The element package | Not at first. `@excalidraw/element` is published on npm (`next` build), MIT, with no React in it, and it holds the scene, the renderer, binding and hit testing. But its functions take Excalidraw's own scene and app state, its signatures are long and internal, and the changelog lists breaking changes to it. | Revisit for elbow arrows or richer binding. |

js-draw (MIT, by Henry Heino, about 41,000 lines) is worth reading too, though not using whole. Its rendering cache is a tree
of cached renders, each block split three by three. Its stroke builders cover pressure, stabilization and shape autocorrect.
Like Excalidraw, it reads no coalesced or predicted events. (Source.)

---

## 2. What makes a pen feel good

### The numbers

| Finding | Number | From |
|---|---|---|
| Lag people notice while writing and sketching | About 50 ms. What they judge by is seeing the hand or pen, not the gap between nib and ink | Annett et al., Graphics Interface 2014 (Study) |
| Lag people can tell apart, side by side | About 7 ms against 40 ms while scribbling; 1 ms against 2 ms while dragging | Ng et al., CHI 2014 (Study) |
| Commercial pens, end to end, in 2014 | 65–120 ms | Ng et al., CHI 2014 (Study) |
| Lag that gets in the way of hand-eye coordination | Over 50 ms | Chrome's article on the desynchronized hint (Official) |
| Android's front-buffered rendering, pen to screen | About 4 ms, in a case study | Android developers (Official) |
| One frame at 144 Hz | 6.9 ms | Arithmetic |

So the engine's whole job on the pen path is removing frames of waiting between the pen and the screen.

### What the engine does about it

1. **Every sample.** The pen reports faster than the screen draws. `getCoalescedEvents()` gives every sample, not one per
   frame, so fast curves stay round. (Chrome 58; Source: MDN's data.)
2. **Draw a little ahead.** `getPredictedEvents()` gives where the pen is about to be. Draw it as a short tail and drop it at
   the next real sample. (Chrome 77; Source.)
3. **A canvas that skips the queue.** The stroke being drawn goes on a canvas made with `desynchronized: true`, which reaches
   the screen without waiting for the rest of the page (Chrome 75 on Android). No other element may sit above it, and
   `getContextAttributes()` confirms the browser honoured it. (Official; Source.)
4. **No framework on the pen path.** Each new segment is drawn as the pointer event arrives, straight onto that canvas. Where
   the web view has `pointerrawupdate` (Chrome 77, secure pages only), it fires as input arrives instead of once a frame.
   (Source; Unverified in this web view.)
5. **Smooth without visible lag.** perfect-freehand's smoothing ("streamline") makes the ink trail the nib, and too much of it
   reads as lag. Start at Excalidraw's 0.5 and tune it against the video, with prediction covering the rest. (Estimate.)
6. **Finished strokes in tiles.** When the pen lifts, the stroke is drawn into cached tiles. Panning moves the tiles; zooming
   stretches them and redraws them sharp once the fingers stop. The number of strokes stops mattering to the frame rate.
   (Estimate; js-draw's cache is the reference.)
7. **A spatial index** (rbush, MIT) finds strokes under the eraser, the lasso or a tap without checking every stroke, and
   skips what's off screen.
8. **Only the pen draws, and a palm does nothing.** Ink comes from `pointerType` "pen", and fingers pan and zoom. While the
   pen is down, and briefly after it lifts, touches are ignored, so a resting palm can't pan the page. Touches with a large
   contact area (the event's `width` and `height`) read as palms too. (Estimate.) Android also cancels touches it judges to be
   palms, which would arrive as `pointercancel` (Unverified in this web view).
9. **A pressure curve.** Raw pressure goes through a soft curve, so light writing isn't hair-thin. (Estimate.)
10. **No garbage-collection hiccups.** Points go in typed arrays, and buffers are reused. (Estimate.)

The web view also needs some tidying: `touch-action: none` and `overscroll-behavior: none` on the canvas; no text selection
or long-press callout; and the context menu blocked, since the pen's button may arrive as a right-click (Unverified). If the
pen reports hover, a dot can show the brush size before it touches (Unverified).

### Plan B: native ink for the stroke being drawn

If the bake-off finds the web too slow, a native Android layer sits over the web view and draws only the stroke being made:

- It uses Android's `LowLatencyCanvasView` or front-buffered renderer, from `androidx.graphics:graphics-core`. That library
  is stable at 1.0.4 (Source: Google's Maven) and needs Android 10 or later (Official). Motion prediction comes from
  `androidx.input:input-motionprediction` 1.0.1 (Source).
- Neither has .NET bindings, so it means a small Kotlin wrapper bound into .NET (the approach of CommunityToolkit's
  Maui.NativeLibraryInterop, MIT).
- The layer takes pen events, while finger events fall through to the web view.
- It draws with the same outline math (perfect-freehand is a few hundred lines to port to C#), and hands each finished stroke
  to the web engine. It clears once the web has drawn that stroke.
- The web engine tells it the current zoom and position.

It's real work (M to L), but it touches only the pen path; the rest of the canvas stays as it is.

A fully native canvas isn't the fallback. NuGet has no .NET version of perfect-freehand or roughjs (Source: searched), so it
would mean porting both and rewriting everything else for SkiaSharp (4.155.0 for MAUI).

Later, on the PC: WebView2 supports the desynchronized canvas (Chrome 81 on Windows; Source). On Windows 11, Edge's delegated
ink trail claims up to a 240% improvement in latency (Weak source: coverage of Microsoft's blog).

---

## 3. The screen

```
+--------------------------------------------------------------------------------------------+
| Menu | Select  Pen  Highlighter  Eraser | Shapes  Arrow  Text | Colour  Width | Undo  Redo |
+--------+-----------------------------------------------------------------------------------+
| Page 1 |                                                                                   |
| Page 2 |                                                                                   |
| Page 3 |                                     the canvas                                    |
|        |                          infinite; nothing floats over it                         |
|        |                                                                                   |
|   +    |                                                                                   |
+--------+-----------------------------------------------------------------------------------+
```

- **The top bar:**
  - The menu.
  - Select: tap a thing, or draw around things with the pen to lasso them.
  - The pen: ballpoint by default; long-press for fountain pen, marker and highlighter. Then the eraser.
  - Shapes (rectangle, ellipse, diamond, line), the arrow and text.
  - Five colours and three widths.
  - Undo and redo.
  - Buttons at least 48 dp, Android's usual touch minimum.
- **The left rail:** each page as a thumbnail of its drawing. Add, rename, reorder and delete pages. It folds away to give the
  canvas the full width. Each page is its own infinite canvas.
- **The menu:** export (PNG, SVG, `.excalidraw`), the look, settings, and the licenses.
- **The canvas:** infinite, light or dark, with an optional dot grid. In a dark look, colours are inverted for display, as
  Excalidraw does.
- Nothing floats over the canvas while you draw, which keeps the low-latency canvas low-latency. Pickers (colour, pen) open
  over it only briefly.

### Built-in defaults

| Behaviour | Default |
|---|---|
| What draws | The pen only; fingers never do, so a resting palm is harmless |
| One finger | Pans, except while the pen is down |
| Two fingers | Pan and pinch-zoom; a two-finger tap undoes, a three-finger tap redoes |
| After drawing a shape | Back to the pen you had |
| Holding the pen still at the end of a stroke (about half a second; tune it) | The stroke snaps to a clean shape, as in GoodNotes' "Draw and Hold" |
| A line from one shape to another | Becomes an arrow attached to both, which follows them when they move (Excalidraw's rule) |
| Writing inside a shape | Moves and resizes with the shape |
| The pen's button, held (if the pen has one that the web view sees) | Erases, then the pen is back |
| The eraser | Removes whole strokes |
| Shapes | Hand-drawn, like Excalidraw's default, with a clean option |
| Saving | Automatic, a moment after each change |

---

## 4. How it would be built

- **The app:** a MAUI Blazor Hybrid app for the tablet, like the repo's others. The top bar, the left rail and the menu are
  Blazor, in the Kit's looks. The canvas engine is TypeScript with no framework, built with esbuild. Its output is committed
  into `wwwroot`, the way Prima keeps pdf.js, so Visual Studio needs no Node.
- **Who talks to whom:** Blazor sends tool and style changes to the engine. The engine sends back "page changed" and
  "selection changed". Everything per-pointer stays inside the engine.
- **The engine's parts:**
  - Input: routing pen and fingers, coalesced and predicted samples, gestures.
  - Camera: pan and zoom.
  - Scene: elements shaped like Excalidraw's, the spatial index, z-order.
  - Tools: select and lasso, pens, eraser, shapes, arrow, text.
  - Ink: outlines and the pressure curve.
  - Render: the wet canvas, the tile cache, selection handles, roughjs for shapes.
  - Recognizer and arrow binding.
  - History: undo and redo, as before-and-after records of the elements a change touched.
  - Files: load and save, and the three exports.
- **On disk:** a page list (order and names), one JSON file per page, and a thumbnail per page.
- **Checking it:** the canvas runs in the preview host for cloud screenshots, as the other apps' screens do. The engine's
  geometry (outlines, hit testing, the recognizer, binding) gets its own tests. Feel can only be checked on the tablet.

---

## 5. The first version, and what comes after

| First version | Next | Later (research.md) |
|---|---|---|
| Infinite canvas; ballpoint, fountain pen, marker, highlighter; stroke eraser; select by tap or lasso, then move, resize, recolour, duplicate, delete; rectangle, ellipse, diamond, line and arrow tools that hand back the pen; arrows attached to shapes; hold-to-snap; writing that moves with its shape; text in Excalifont; undo and redo; colours and widths; pages on the left; autosave; export to PNG, SVG and `.excalidraw` | Images; importing `.excalidraw` files; a partial eraser; pencil; dot grid and backgrounds; laser pointer; a zoom box for small writing; a pressure-curve setting; a left-handed layout | The PC; sync; notebooks and templates; handwriting recognition |

---

## 6. What it would take

Sizes are our estimate: **S** is about one session, **M** a few, **L** a week or more of sessions.

### First, the bake-off

| Spike | Size | What it measures | Done when |
|---|---|---|---|
| The pen | S to M | On the tablet, film quick strokes at 240 frames a second (about 4 ms a frame) and count the frames between nib and ink, in four contenders: (a) plain pointer events, (b) our tuned web path, (c) Excalidraw's `next` build, (d) a native app such as Concepts. Also measured: the web view's frame rate (is it 144?), pen samples a second, pressure and tilt values, hover, what the pen's button sends, palm touches. | The numbers are in a table, and the owner has written with each. As a rule of thumb (Estimate), if the tuned web path is more than about two video frames (8 ms) behind the native app, plan B is built. |
| Many strokes | S | Frame times while panning and zooming a page of 3,000 strokes, with tiles and without; how long the page takes to open. | Panning and zooming stay at the screen's frame rate. |

### Then the canvas

| Step | Size | What the owner gets |
|---|---|---|
| 1. The canvas | L | Camera, pens, eraser, select and lasso, undo, autosave, the page rail, the top bar, the menu, export. The main experience. |
| 2. Shapes | M | Shape tools that hand back the pen, the hand-drawn look, hold-to-snap, arrows attached to shapes, writing that moves with its shape, text. |
| 3. The feel pass | S to M | Smoothing, prediction and the pressure curve tuned against the video; the gestures; the pen's button. |
| Plan B, only if needed | M to L | The native layer for the stroke being drawn. |

---

## 7. Risks

- **The web view's pen lag.** That's why the bake-off comes first.
- **The web view's frame rate and the pen's extras.** Whether the web view runs at 144 Hz, and what hover and the button
  send, are unknown until the bake-off.
- **Our arrows are simpler than Excalidraw's.** That's fine for an arrow between two shapes, but not for routed elbow
  arrows. Those would come later, from the element package.
- **The export drifting from Excalidraw.** Its element fields change (arrow binding changed during 2026). Check exports at
  excalidraw.com whenever either side changes.
- **Scope.** The first version is the first column of the table in section 5, and nothing more.

---

## 8. For the owner to decide

1. **Shapes:** hand-drawn by default (Excalidraw's look), or clean?
2. **One finger:** pans (recommended), or does nothing, leaving two fingers to pan?
3. **Pages:** each its own canvas (recommended), or one big canvas split into frames?
4. **The look around the canvas:** the Kit's Logbook and Telemetry, or a plain look of its own?
5. **The native app to beat** in the bake-off: Concepts, Nebo, or another one you have on the tablet?
6. **The name.** "Notebook" was the working name for the wider app; a canvas-first app may want another.

---

## References

### Excalidraw and js-draw

- Excalidraw, read at commit `701b901` (Source): https://github.com/excalidraw/excalidraw. Files read:
  `packages/excalidraw/components/canvases/NewElementCanvas.tsx`, `packages/excalidraw/components/App.tsx`,
  `packages/element/src/renderElement.ts`, `packages/element/src/convertToShape.ts`, `packages/element/src/Scene.ts`,
  `packages/element/package.json`, `packages/common/src/constants.ts`, `packages/excalidraw/data/restore.ts`. The line counts
  are of each package's TypeScript, without tests and translations.
- npm: `@excalidraw/element`, `@excalidraw/math` and `@excalidraw/common` (`next` 0.18.0-3c916c5), and
  `@excalidraw/laser-pointer` 1.3.2 (Source): https://www.npmjs.com/package/@excalidraw/element
- Excalidraw issues [#5788](https://github.com/excalidraw/excalidraw/issues/5788) and
  [#9705](https://github.com/excalidraw/excalidraw/issues/9705) (Source, user reports).
- tldraw's use of coalesced events, found with GitHub's code search (Source):
  https://github.com/tldraw/tldraw/blob/main/packages/editor/src/lib/hooks/useCanvasEvents.ts
- js-draw, read at commit `d11df39` (Source): https://github.com/personalizedrefrigerator/js-draw. Files read:
  `packages/js-draw/src/rendering/caching/`, `packages/js-draw/src/components/builders/`.

### The pen and latency

- Ng, A., Annett, M., Dietz, P., Gupta, A. & Bischof, W. F. (2014). In the blink of an eye: investigating latency perception
  during stylus interaction. *CHI 2014*, 1103–1112. https://doi.org/10.1145/2556288.2557037 ;
  https://webdocs.cs.ualberta.ca/~wfb/publications/C-2014-SIGCHI-Latency.pdf (Study.)
- Annett, M., Ng, A., Dietz, P., Bischof, W. F. & Gupta, A. (2014). How low should we go? Understanding the perception of
  latency while inking. *Graphics Interface 2014*. https://graphicsinterface.org/proceedings/gi2014/gi2014-22/ ;
  https://webdocs.cs.ualberta.ca/~wfb/publications/C-2014-GI-Latency.pdf (Study.)
- Chrome, "Low-latency rendering with the desynchronized hint" (Official): https://developer.chrome.com/blog/desynchronized
- MDN's browser compatibility data for `PointerEvent`, `Element` (`pointerrawupdate`) and `HTMLCanvasElement.getContext`
  (Source): https://github.com/mdn/browser-compat-data
- Android, advanced stylus features and low-latency rendering (Official):
  https://developer.android.com/develop/ui/views/touch-and-input/stylus-input/advanced-stylus-features ;
  https://developer.android.com/reference/androidx/graphics/lowlatency/LowLatencyCanvasView ;
  https://developer.android.com/games/develop/multiplatform/enable-natural-input-on-all-form-factors ;
  https://medium.com/androiddevelopers/stylus-low-latency-d4a140a9c982
- Google's Maven metadata for `androidx.graphics:graphics-core` and `androidx.input:input-motionprediction` (Source):
  https://dl.google.com/android/maven2/androidx/graphics/graphics-core/maven-metadata.xml
- Maui.NativeLibraryInterop (Source): https://github.com/CommunityToolkit/Maui.NativeLibraryInterop
- NuGet searches for perfect-freehand and roughjs ports, and SkiaSharp.Views.Maui.Controls (Source): https://www.nuget.org
- Edge's delegated ink trail, "up to 240%" (Weak source: coverage of Microsoft's blog):
  https://windowscentral.com/microsoft-improved-inking-way-240 ; https://blogs.windows.com/msedgedev/?p=24844
- GoodNotes, "Draw and Hold" (Official): https://www.goodnotes.com/blog/shape-recognition
- rbush (MIT) (Source): https://github.com/mourner/rbush
