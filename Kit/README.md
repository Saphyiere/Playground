# Kit

What every app shares: two looks, **Logbook** and **Telemetry**, and the pieces of a screen built in them. Each app's
**Settings** switches between them; the switch applies at once and is remembered.

- **Logbook:** a printed log on warm paper, kept like a journal. Serif words and big serif numbers in fixed-width figures, no
  cards or boxes: each section is set apart by space and a heading the pen underlines once, rows sit on hairlines, and a faint
  tint inside a field says it's a place to write. One vermilion pen marks what you did, with ticks, a circle around today and
  tally strokes. The pen marks; it never grades a number.
- **Telemetry:** live instrument channels on a dark panel. Monospace readouts that never jitter (Martian Mono, slashed zero),
  words in Atkinson Hyperlegible Next, tiles, and every number on its own ruler in its channel's color.

Design links:

- Style directions, with both looks' mockups: https://claude.ai/artifact/2Z6jpTXARxNPY9AWAS2hxQ
- The first design system (the mark and app icon): https://claude.ai/artifact/H9wJAoBQE834PEQ9qG4wmq

## What's in it

```
Playground.Kit/
  wwwroot/kit.css           The looks: each theme's tokens, then the shared pieces of a screen (layout, type, mastheads,
                            ledgers, buttons, fields, tick boxes, chips, rulers, motion), then Telemetry's own shapes.
  wwwroot/fonts/            Newsreader and Schibsted Grotesk for Logbook, Martian Mono and Atkinson Hyperlegible Next for
                            Telemetry, each under the SIL Open Font License (OFL-*.txt).
  Themes/Theme.cs           The looks: id, name, whether it's dark, its background, and its date styles.
  Themes/ThemeSetting.cs    The look in use, kept with the app's settings (ISettingStore).
  AppNavigation.cs          The way back, the same for a screen's back link and the phone's back gesture.
  Components/               PageHeader (the masthead, with the way back), Icon, Mark (the pen's marks), RulerTicks,
                            ConfirmDelete, and LookPicker (Settings' Look).
```

## Using it in an app

1. The app's UI project references `Kit/Playground.Kit/Playground.Kit.csproj`, and its `_Imports.razor` has
   `@using Playground.Kit`, `@using Playground.Kit.Components` and `@using Playground.Kit.Themes`.
2. The host page loads `_content/Playground.Kit/kit.css`, then the app's own stylesheet, and sets `data-theme` on `<html>` from
   local storage before the first paint (Workout's `src/AdaptiveWorkout.Phone/wwwroot/index.html` does both).
3. The app registers an `ISettingStore` (where its settings live), `ThemeSetting`, and `AppNavigation` (scoped). Its layout puts
   `ThemeSetting.Current` in `data-theme`, keeps it in local storage, and cascades it to every component (Workout's
   `MainLayout`). A host can derive from `ThemeSetting` and override `ApplyToShell` to dress its own parts, like the phone's
   status bar icons (Workout's `PhoneThemeSetting`).
4. The app's own screens get their rules in its own stylesheet, using the Kit's tokens, so the two looks reach them too.

## How the looks work

- **Colors, fonts and line weights** are tokens in `kit.css`, one block per theme under its `[data-theme]`. Fonts go by role:
  words, figures, labels, controls.
- **Shapes** that differ are rules under the theme's own `[data-theme]` (Telemetry's tiles, boxed fields, chips, filled tick
  boxes), so neither theme's rules touch the other's.
- **Pieces that draw differently** take the cascading `Theme` and branch inside their own component: `Mark` draws the pen's
  ticks in Logbook and checks in Telemetry, and `RulerTicks` the rulers under Telemetry's numbers.

To add a look: add it to `Theme.All`, give it a token block (and any shapes) in `kit.css`, add its fonts to `wwwroot/fonts`, and
branch the components that should draw differently, in the Kit and in each app.
