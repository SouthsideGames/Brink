# 09 — UI/UX Specification

Source: `Scripts/UI/` (`TerminalScale`, `TerminalMetrics`, `Breakpoints`,
`DisplaySettings`, `DisplaySettingsPanel`, `TerminalShellController`,
`AsciiChart`, `AsciiWorldMap`, `AsciiCountryMap`, `IntelReadout`),
`Scripts/UI/Views/`, `Resources/UI/TerminalShell.uxml|uss`,
`Core/GameBootstrap.cs`. GDD §4, §28.

Enforced by `Tests/EditMode/ReadabilityTests.cs` and `MapAndLayoutTests.cs`.
Those two files are the executable half of this document: they parse the real
stylesheet and call the real layout code, so a regression fails the build rather
than a phone. The notification-class rules in §11 are asserted by
`Tests/EditMode/PartialSystemsTests.cs`.

---

## 1. The terminal is a character grid

`.terminal-text` sets `white-space: pre`. That is not a stylistic choice — it is
the only way ASCII bars, box rules, dotted rows and the world map stay aligned,
and alignment is what makes the screen read as a printout rather than a webpage.

Everything else in this spec follows from the two consequences:

1. **Nothing wraps.** A line longer than the panel does not fold; it runs off the
   right edge and is simply gone.
2. **The panel width has to be known in characters**, because every box, table
   and figure is composed to an explicit column count before it is handed to a
   `Label`.

The shell therefore has to answer two questions on every device: *how big is a
character* (§2) and *how many of them fit* (§3). Getting either wrong is not a
cosmetic bug — it is content leaving the screen.

Fonts are JetBrains Mono Regular and Bold (SIL OFL), shipped under
`Resources/UI/Fonts` and bound on `.terminal-root` so every descendant inherits
them. A proportional face anywhere in a `terminal-text` block breaks the grid.

---

## 2. Panel scale — derived from pixel width, never from DPI

`UI/TerminalScale.cs`. `GameBootstrap` creates the `PanelSettings` with
`PanelScaleMode.ConstantPixelSize` and sets `scale` from `TerminalScale.ScaleFor`.
`TerminalShellController.ApplyPanelScale` recomputes it on every geometry change,
so folding a phone open or closed mid-session re-scales the shell rather than
leaving a scale computed once at boot.

### The bug this replaced

The panel previously used `ConstantPhysicalSize` at a 96 reference DPI. That mode
requires the platform to report an honest `Screen.dpi`. When it does not — the
Device Simulator, and some real handsets — Unity falls back to 96, the scale
collapses to **1**, and the whole terminal renders 13-pixel text on a 2300-pixel
screen. Every readout was technically present and physically illegible.

Deriving the scale from pixel width removes the platform dependency entirely and
makes the result a pure function of the resolution, which is why
`MapAndLayoutTests.TextIsNeverRenderedAtOnePixelPerPoint` can assert it for six
resolutions with no device attached. `Screen.dpi` is still *logged* at boot so a
layout complaint can be diagnosed from logcat, and is used for nothing.

**Rule: never reintroduce `ConstantPhysicalSize`, and never branch on `Screen.dpi`.**

### Constants

| Constant | Value | Meaning |
|---|---|---|
| `BaseFontPx` | `13` | Font size in the stylesheet, before scaling |
| `BaseCharPx` | `7.8` | `13 × 0.6` — JetBrains Mono advances 0.6em |
| `MinScale` | `1.0` | |
| `MaxScale` | `6.0` | |

### Column targets

`TargetColumns(widthPx, heightPx)` buckets on pixel width alone:

| Screen width (px) | Target columns |
|---|---|
| ≤ 900 | 42 |
| ≤ 1400 | 50 |
| ≤ 2000 | 58 |
| ≤ 2600 | 66 |
| > 2600 | 76 |

Plus a **wide-short bonus**: if `width / height ≥ 2.2`, add 4. A folding phone's
cover display (2316×904, ratio 2.56) and a landscape phone (2400×1080, ratio
2.22) both qualify. The bonus is deliberately small. On a short screen it is
tempting to shrink the type until more rows fit, and that is exactly the trade
that makes a text game tiring; four columns buys back a couple of lines without
turning the readout into fine print.

The targets sit inside the typographic comfortable measure of 45–75 characters
for prose, with tables allowed to run wider.
`ReadabilityTests.TheLineLength_StaysInAComfortableRange` holds every authored
screen between 40 and 80.

### The scale itself

```
columns = TargetColumns(w, h)
scale   = clamp(MinScale, MaxScale, w / (columns × BaseCharPx) × sizeMultiplier)
```

`sizeMultiplier` comes from `DisplaySettings.ScaleMultiplier` (§7). A multiplier
above 1 enlarges the type and therefore *costs columns* — the screen does not
grow, so something has to give, and which way that trade goes is the reader's
call. `ReadabilityTests.LargerTextMeansFewerColumns_TheScreenDoesNotGrow` asserts
both halves of that sentence.

At `sizeMultiplier = 1` the resulting scale reproduces the target exactly:

| Screen | Target | Scale | Rendered font |
|---|---|---|---|
| fold cover 2316×904 | 70 | 4.24 | 55 px |
| fold open 2176×1812 | 66 | 4.23 | 55 px |
| phone landscape 2400×1080 | 70 | 4.40 | 57 px |
| small phone 1280×720 | 50 | 3.28 | 43 px |
| tablet 2560×1600 | 66 | 4.97 | 65 px |
| desktop 1920×1080 | 58 | 4.24 | 55 px |

The shipped default is `TextSize.Small` (×0.82), which trades about 18% smaller
type for about 22% more columns than the table above.

`ScaleIsAlwaysSane_EvenForAbsurdInput` covers 0×0, 1×1, 100×40 and 20000×8000 —
the clamps mean a degenerate resolution produces a usable terminal rather than a
NaN or a one-character screen.

This is the GDD §4 promise, mechanised: **a bigger display buys more columns,
never bigger text.** `ABiggerScreen_BuysMoreColumnsNotBiggerText` asserts it.

---

## 3. Measured columns — `TerminalMetrics`

The scale says how big a character is on the *screen*. Views need to know how
many fit in the *content area*, which is smaller: the nav rail, the safe-area
padding and the content host's own margins all take a bite, and the bite changes
with the breakpoint.

So it is measured, not assumed. `TerminalShellController` keeps an off-screen
probe `Label` in the content host — same class, same inherited styling, opacity
0, `PickingMode.Ignore` — and `UpdateMetrics` measures it:

```
charWidth = MeasureTextSize("M" × 40).x / 40        (fallback: fontSize × 0.6)
columns   = floor((contentWidth − padding) / charWidth) − 1
columns   = clamp(MinColumns, MaxColumns, columns)
```

The **one column of headroom** matters: rounding error and the scroll gutter
should never be the thing that pushes a box rule past the edge.

| Member | Value / meaning |
|---|---|
| `MinColumns` | `34` — narrower than this and content is unreadable anyway |
| `MaxColumns` | `104` — beyond this, rules look absurd and the eye loses the line |
| `Columns` | The measured width. Default `64` before first measurement |
| `ShortScreenHeight` | `320` panel points |
| `ShortScreen` | `true` when the panel height is below that |
| `Size` | The current `SizeClass` |
| `Changed` | Raised only on a *real* change, so views rebuild once, not every frame |
| `Inset(margin = 2)` | `Columns − margin`, floored at `MinColumns`, for boxes that read better inset |
| `MapRows` | `11` when `ShortScreen`, else `23` at `Large`, else `17`. The `Large` branch became reachable when the breakpoints moved to columns (§5) |

`MetricsRaiseChanged_SoOpenViewsRebuild` asserts that an identical `Update` is
silent — a rebuild on every geometry event would thrash the whole view tree.

### The rule

**Never hardcode a column count in a view.** Use `TerminalMetrics.Columns`; the
convention across the view classes is a `static int W => TerminalMetrics.Columns`
at the top. Views used to hardcode 64–78, which fits a tablet, overflows a phone
and badly overflows a foldable's cover screen. That is the bug this whole
subsystem exists to prevent, and `TheWorldMap_NeverExceedsTheColumnsItWasGiven`
and `TheCountryChart_NeverExceedsItsColumns` hold the line at 34, 48, 64, 80, 96
and 100 columns.

ASCII figures take explicit `columns`/`rows` arguments and sample themselves into
whatever grid they are given (§9). Nothing renders at a fixed size.

---

## 4. Text policy — applied centrally, once per refresh

`TerminalShellController.ApplyTextPolicy(element, spacing, columns)` walks the
whole content host after every refresh and after every display-settings change,
and applies two things to each `Label` carrying `terminal-text`:

1. **It fits.** `label.text = AsciiChart.WrapBlock(text, columns)`.
2. **It has leading.** `label.style.unityParagraphSpacing = spacing`.

### Why it is central rather than per-call-site

Sizing each view's *boxes* to the panel was not enough. The bug reported from a
real device was that the box rules fitted and the prose written inside them did
not — a notification body, a hint, an official's remark, all arbitrary length,
none of them passing through any width-aware formatting. Doing this at the shell
means a view added next year cannot forget, and cannot forget in a way that only
shows up on hardware nobody on the project owns.

Views rebuild their labels from scratch on refresh, so the walk has to run again
each time; `RefreshAll` calls it last, after `activeView.Refresh()`.

### Why figures are excluded

Labels created through `TerminalView.AddFigure()` carry `terminal-figure` as
well, and the walk skips both operations for them:

- **No wrapping** — a figure is already built to the exact grid it was handed.
  Wrapping one corrupts it.
- **No leading** — a gap between rows breaks the vertical strokes that make a
  map a map.

`ReadabilityTests.ReadoutTextHasLeading_AndFiguresDoNot` asserts the stylesheet
half of that pair.

### Why leading is set in code, not USS

`.terminal-text` does carry `-unity-paragraph-spacing: 4px` as a baseline, but
the operator's setting is applied through `label.style.unityParagraphSpacing` in
C#. A USS property the parser does not recognise fails **silently** — no error,
no warning, the setting simply never happens — and a silently-ignored
readability setting is worse than none, because it looks configured. The C#
property is a compile-time guarantee.

UI Toolkit has no `line-height`. Paragraph spacing applies after every newline,
and these `pre` blocks are made of nothing but newlines, so it is the correct
lever regardless.

**Related rule:** never add a per-breakpoint `font-size` to `.terminal-text`. The
panel scale already sizes the type; a breakpoint override double-shrinks the
content and silently overrides the reader's choice.

### `AsciiChart.WrapBlock(block, width)`

Hard-wraps a whole multi-line block. Returns the input unchanged when
`width < 12` or the block is empty.

- **Wrapped, not truncated.** A briefing that silently loses its second half is
  worse than one that takes two lines.
- **Breaks on the last space that fits**, so words stay whole. A single word
  longer than the line is cut at the boundary rather than allowed to overflow.
- **Hanging indent** on continuation lines: the original leading whitespace plus
  2, bounded at `width − 20` so a deeply indented line still has room to say
  something. The wrap then reads as one entry rather than two.
- **Idempotent.** It runs on every refresh, so a second pass must change nothing
  (`Wrapping_IsIdempotent`).
- **Byte-identical pass-through** for anything already inside the width, or ASCII
  alignment would drift a little on each refresh
  (`Wrapping_LeavesShortLinesAndFiguresUntouched`).
- **Markup does not count toward the width.** `AsciiChart.VisibleLength(line)`
  walks the string and skips everything between `<` and `>`, so a
  `<color=#FF6E64>…</color>` pair — 24 invisible characters — no longer wraps a
  coloured line two dozen characters early, which is exactly what used to happen
  to FLASH notifications. The line is *measured* on visible characters and
  *sliced* on real ones; if a line is genuinely over-long **and** contains
  markup, it is passed through untouched rather than risk cutting a tag in half.
  The right fix at the call site is still one label per coloured line (§11), not
  a wider tolerance here.

---

## 5. Responsive layout

### Breakpoints — measured in **columns**, not points

`UI/Breakpoints.cs`. `FromColumns(int columns)` is the only entry point.

| Class | Columns | Layout |
|---|---|---|
| `bp-compact` | < `MediumMinColumns` (64) | Nav becomes a horizontal strip 44pt tall above the content |
| `bp-medium` | 64 – 81 | Narrow 74pt left rail, centred labels |
| `bp-large` | ≥ `LargeMinColumns` (82) | Full left rail with complete view names |

**Points were the wrong unit and must not come back.** Once the panel scale
started being *derived to hit a column target* (§2), root width in points became
approximately `targetColumns × BaseCharPx ÷ sizeMultiplier` — roughly 400–850
points for every real device at every text size. The old thresholds (700 / 1050)
therefore made `bp-medium` rare and **`bp-large` unreachable short of an absurd
resolution hitting `MaxScale`**. Both branches were dead code, and they took the
full nav labels and `TerminalMetrics.MapRows`'s 23-row map with them.

Columns are the honest measure of how much a screen can show, which is what GDD
§4 actually asks about: a larger display shows *more information*, never larger
UI. A reader who chooses `Larger` text genuinely has a smaller terminal and
should get the compact layout — under the old scheme they got the same class as
everyone else.

The order of operations in `TerminalShellController` matters and is commented in
place: `UpdateMetrics` measures the probe label, derives
`columns = floor(contentWidth / charWidth) − 1`, calls
`ApplySizeClass(Breakpoints.FromColumns(columns))`, and only then hands the size
class to `TerminalMetrics.Update`. **Columns must be measured before the size
class is chosen**, because the class is now expressed in them.

`ApplySizeClass` swaps the class on `terminal-root` and USS does the rest.
`ApplyNavLabels` shows full `Id` strings only at `Large` and the three-letter
`ShortCode` otherwise.

### `bp-short`

Independent of width, `ApplyShortScreenClass` toggles `bp-short` when the root is
shorter than `TerminalMetrics.ShortScreenHeight` (320 points). This is a folding
phone's cover display, and also most landscape phones at the default text size:
wide, and very short.

There, height is the scarce resource and width is not, so the layout goes back to
spending width:

- `.main-row` returns to `flex-direction: row`
- the nav rail becomes a **52pt vertical strip** with 11px centred labels
- status bar padding drops to 1px, classification and stats to 11px
- command buttons compress to 5×9px padding
- `.view-scroll` padding drops to 0

These rules are placed **after** `.bp-compact` in the stylesheet deliberately, so
they win on specificity ties. `TerminalMetrics.MapRows` drops to 11 in the same
condition (`AShortPanel_IsRecognisedAsShort`).

### Safe area

`ApplySafeArea` converts `Screen.safeArea` into root padding, scaled from screen
pixels to panel points by `root.resolvedStyle.width / Screen.width`, on top of a
6-point base pad. Landscape notches and cutouts eat into the left or right inset,
which is exactly where a fixed-width terminal would otherwise lose columns
without noticing.

**Orientation is landscape-only** (project settings; portrait autorotation
disabled).

### No scrollbar chrome

Every `ScrollView` — the nav rail and every view root — sets both scroller
visibilities to `Hidden`. Wheel and touch drag still work. Explicit user
preference; keep it. `.content-host` additionally sets `overflow: hidden`,
because the grid is composed to the measured column count and a horizontal
scrollbar would mean the measurement was wrong, not that the reader should go
looking.

---

## 6. Readability

This is a game made entirely of text, so legibility is a correctness concern and
is tested like one. `ReadabilityTests` parses `TerminalShell.uss` itself rather
than a copy of its values.

Thresholds are WCAG 2.1: **7:1 (AAA) for every text colour**, 3:1 for borders and
other non-text UI. AA at 4.5:1 is not good enough here — the weak point was
`terminal-text-dim` at 4.74:1, used for roughly 35 secondary readouts, which on a
phone in daylight is precisely where reading actually breaks down.

### 6.1 The four palettes

A palette is a choice about **comfort, never about legibility**. The GDD never
specifies a colour; green was an implementation choice, not a requirement.

| Theme | Class | Background | Body text | Character |
|---|---|---|---|---|
| Green | `theme-green` | `rgb(5,10,7)` | `rgb(72,240,120)` | Phosphor on near-black. The original terminal look |
| Amber | `theme-amber` | `rgb(12,8,2)` | `rgb(255,176,0)` | Less blue light, kinder at night. **Shipped default** |
| Low glare | `theme-soft` | `rgb(18,22,19)` | `rgb(178,224,190)` | Lifted background, softened foreground |
| Signal | `theme-signal` | `rgb(14,16,18)` | `rgb(208,214,206)` | Neutral bone on charcoal |

Measured contrast against each palette's own background:

| | `terminal-text` | `-dim` | `-bright` | `.classification` | `.status-stat` | `.view-scroll` border |
|---|---|---|---|---|---|---|
| Green | 13.31 | 7.59 | 16.66 | 15.78 | 17.75 | 5.72 |
| Amber | 10.90 | 7.12 | 14.76 | 13.07 | 15.69 | 4.39 |
| Low glare | 12.43 | 8.01 | 16.66 | 14.84 | 16.01 | 5.01 |
| Signal | 12.88 | 8.03 | 17.81 | 15.95 | 17.81 | 3.82 |

Every text figure clears AAA; every border clears the 3:1 non-text minimum.
`EveryTheme_KeepsEveryTextColourAtAAA` asserts all of it, per palette, per rule.
`DimTextIsQuieter_ButStillFullyLegible` additionally requires the ordering
dim < body < bright — secondary text must *read* as secondary without being hard
to read.

Two design notes worth preserving:

- **Low glare** exists because bright glyphs on a near-black field halate — the
  glow around the character is what makes a long session tiring for readers prone
  to it. It narrows the luminance gap (12.43 against green's 13.31) without
  dropping below AAA, and `TheLowGlareTheme_ActuallyReducesGlare` asserts the
  narrowing is real.
- **Signal is deliberately not white.** White on near-black is the worst case for
  halation and reads as a modern CLI rather than an old government terminal,
  which is the one thing GDD §4 does insist on. A desaturated bone keeps the CRT
  feel while freeing the entire hue wheel to carry meaning instead of atmosphere.

`EveryThemeClass_IsDefinedInTheStylesheet` cross-checks
`DisplaySettings.AllThemeClasses` against the USS, so an enum value can never
offer a palette that does not exist.

### 6.2 Semantic colour

Diplomatic standing gets **one** colour set, shared across all four palettes,
returned by `AsciiWorldMap.StandingClass`:

| Class | Colour | Standing |
|---|---|---|
| `sig-hostile` | `rgb(255,132,124)` | Hostile |
| `sig-rival` | `rgb(240,168,72)` | Rival |
| `sig-neutral` | `rgb(176,182,178)` | Neutral / everything else |
| `sig-friendly` | `rgb(120,190,255)` | Friendly |
| `sig-ally` | `rgb(96,224,208)` | Ally / Strategic Partner |

The player's own country uses `terminal-text-bright` instead.

One set rather than four, because a palette choice must not silently degrade the
map. Contrast against each base:

| | hostile | rival | neutral | friendly | ally |
|---|---|---|---|---|---|
| Green | 8.40 | 9.88 | 9.67 | 10.07 | 12.41 |
| Amber | 8.41 | 9.89 | 9.68 | 10.09 | 12.43 |
| Low glare | 7.69 | 9.04 | 8.85 | 9.23 | 11.37 |
| Signal | 8.03 | 9.44 | 9.24 | 9.63 | 11.87 |

All twenty clear AAA (`SemanticColours_ClearAAAAgainstEveryPalette`).

**Orange and blue, never red and green.** Red-green deficiency affects roughly
one man in twelve, and hostile-versus-allied is exactly the pair you must not
encode that way. `HostileAndAllied_AreNotDistinguishedByRedAgainstGreen` asserts
two things: that ally sits on the blue side (`b > r`), and that the two differ in
**luminance** by more than 15% — currently 35% — because luminance is the channel
that survives every form of colour vision deficiency.

**Colour is always a second channel.** The world map keeps its glyphs —
`!` hostile, `~` rival, `-` friendly, `+` partner, `[XX]` our own post,
`#` chokepoint — with a legend, and the country selector buttons print the
two-letter code as text. Turn every colour off and the reading is intact. Colour
is there so sixteen identical two-letter codes stop being a wall of text, not so
that meaning depends on it.

### 6.3 Adaptive table columns

**Never use a fixed padding in a table row.** A format string like `{name,-22}`
is what still pushes a row off a narrow screen after the surrounding box has been
sized correctly: the rule fits and the row inside it does not.

| Call | Behaviour |
|---|---|
| `AsciiChart.Cell(text, width)` | Returns **exactly** `width` characters. Pads right if short; truncates to `width − 1` plus `…` if long; `…` alone at width 1 |
| `AsciiChart.NameWidth(terminalWidth, share = 0.34)` | `clamp(10, 30, terminalWidth × share)` |

`NameWidth` scales the name column with the terminal but bounds it, so it neither
collapses to nothing on a phone nor sprawls across a tablet
(`NameColumns_ScaleWithTheTerminalAndStayBounded`).
`TableCells_TruncateRatherThanOverflow` asserts the exact-width contract at 40,
55, 70 and 90 columns — including for `null`.

Usage is `AsciiChart.Cell(name, AsciiChart.NameWidth(W, share))`, with `share`
tuned per table when a row carries more than one variable-width column.

### 6.4 Typography rules

- **Body prose stays sentence case.** Uppercase is for labels, headers and button
  text only; all-caps prose measurably slows reading, and this game asks the
  reader to get through a lot of it.
- **No base `font-size` below 11px.** `NoTextIsSetSmallerThanTheShellCanScaleComfortably`
  scans every `font-size: Npx` in the stylesheet. Below 11 there is too little
  room left once a small screen scales down.
- Log lines and crisis-overlay text use `white-space: normal` — they are prose,
  not grid, and are allowed to reflow.

---

## 7. Display settings

`UI/DisplaySettings.cs` (state) and `UI/DisplaySettingsPanel.cs` (the panel).

Reached from the **DSP** button in the status bar, which **ships in every build**
— unlike the SYSTEM console. For a game made entirely of text, setting the type
size is an accessibility feature, not a nicety: no single default suits a folding
phone's cover screen, a tablet, and every pair of eyes that will read it.

| Setting | Values | Effect |
|---|---|---|
| **TEXT SIZE** | `Smallest` ×0.70, `Small` ×0.82, `Medium` ×1.00, `Large` ×1.20, `Larger` ×1.45 | Multiplies the panel scale |
| **PALETTE** | GREEN / AMBER / LOW GLARE / SIGNAL | Swaps the theme class on `terminal-root` |
| **LINE SPACING** | COMFORTABLE (4px) / COMPACT (0px) | `unityParagraphSpacing` on every non-figure label |

Plus **RESET TO DEFAULTS** and **CLOSE**.

**Defaults are Amber + Small + Comfortable**, chosen on device — amber was
preferred on the Fold, and Small read as the natural size there.
`TheDefaults_AreWhatWasChosenOnDevice` pins both. Because Small is the default,
`Smallest` exists so a reader who wants more density still has somewhere to go
(`ThereIsAStepBelowTheDefaultSize`).

`EveryTextSize_StaysWithinASaneScale` checks all five sizes against four
resolutions and requires ≥ 30 columns and > 18px rendered text in every
combination — the two ways a preference could make the game unusable.

### Stored in `PlayerPrefs`, deliberately not in `GameState`

Keys `brink.display.textSize`, `brink.display.theme`, `brink.display.density`.

How large you like your text is a property of the person and the handset, not of
the save. Consequences, both intended:

- The setting **survives a full reset**, which erases every save slot and returns
  to the assessment. Having to re-find the type size after a reset would be a
  small cruelty.
- A save copied to another device **does not** drag one phone's ergonomics onto
  another.

`Load()` validates with `Enum.IsDefined` on every field, so a preference file
written by a newer build cannot brick the UI with an out-of-range enum.

`Changed` fires on save; `TerminalShellController` responds by re-applying the
theme class, forcing a panel-scale recompute, re-measuring the columns and
refreshing the active view — a size change therefore reflows every view
immediately, not on the next month.

### The panel scrolls; CLOSE and the title are pinned

The panel used to be a plain VisualElement with `flex-shrink: 0`, no height cap
and no scrolling — the tutorial-panel bug (§11), refiled under Settings. On a
phone with large text everything past the fold was unreachable, and the *last*
child was FULL RESET: a shipped game whose only new-game path sits below the
fold of a box that cannot scroll has no new-game path. It was reported from a
device as "we need to add a reset button" — the button existed, invisibly,
which is its own kind of missing.

Now the content lives in a hidden-scroller `ScrollView` capped at 55% of
`PanelHeight` on short screens, 70% otherwise; the DISPLAY title sits above it
and CLOSE below it, both pinned — CLOSE is the one control that must never
leave the screen. The FULL RESET section sits **above** the reference prose,
so on most screens it is visible without scrolling at all. FULL RESET remains
two-step (FULL RESET → CONFIRM — ERASE EVERYTHING / CANCEL) because there is
no undo. `MapAndLayoutTests.SettingsPanel_FullResetIsReachable` guards the
scroller, its height cap, the pinned CLOSE, and the two-step confirm.

---

## 8. View catalogue

Built by `TerminalShellController.BuildPanels(includeDebugConsoles)`, in nav-rail
order:

| `Id` | `ShortCode` | Class |
|---|---|---|
| BRIEFING | BRF | `BriefingView` |
| ACTIONS | ACT | `ActionsView` |
| MAP | MAP | `WorldMapView` |
| CABINET | CAB | `CabinetView` |
| MILITARY | MIL | `MilitaryView` |
| ECONOMY | ECO | `EconomyView` |
| INTELLIGENCE | INT | `IntelligenceView` |
| DIPLOMACY | DIP | `DiplomacyView` |
| GOVERNMENT | GOV | `GovernmentView` |
| RESEARCH | RES | `TechnologyView` |
| ENDGAME | EGM | `EndgameView` |
| OPERATOR | OPR | `StrategistView` |
| CHRONICLE | CHR | `ChronicleView` |
| SYSTEM | SYS | `SystemView` — **editor / development builds only** |
| AUDIO | AUD | `AudioDebugView` — **editor / development builds only** |

### Panel names must be tellable apart

`EndgameView` and `StrategistView` used to be **STRATEGIC/STG** and
**STRATEGIST/STR**, adjacent in the rail, one letter apart, and about entirely
different things — the state's decisive instruments and the operator's own
record. Reported from play as a straight question: *"what is the difference
between STG and STR?"*

On a phone the rail is three characters wide, and the attention marker appends a
fourth (`ECO.`, `RES!`), so a code is all the operator has to go on. The rule is
now enforced:
`AttentionSystemTests.NoTwoPanelsAreConfusableInTheNavRail` fails the build when
two `ShortCode`s are within **Levenshtein distance 1**, or when two `Id`s match.

The panels also state what they are not: OPERATOR opens with "Your own file …
nothing here is national power", ENDGAME with "The state's decisive options —
not your record, which is in OPERATOR."

### View ids are strings, and now they are checked

`AttentionSystem`, `ActionCatalog` and `TutorialSystem` all address a panel by
its `Id` string, with nothing binding those strings to a panel that exists —
which is why the rename had to touch five files. `BuildPanels` is public and
static so tests can walk the **real** rail:
`EverySummaryNamesAPanelThatExists`, `EveryActionNamesAPanelThatExists` and
`EveryTutorialStepNamesAPanelThatExists`. The first of those used to compare
against a list hand-copied into the test file, so it would have kept passing
against panels that no longer existed.

### SYSTEM is gated

```csharp
views.AddRange(BuildPanels(Debug.isDebugBuild || Application.isEditor));
```

The SYSTEM console carries save/load slots, FORCE CRISIS, month skipping and live
difficulty toggles. Shipping it would hand the player exactly the "reload the turn
I disliked" loop **GDD §30 forbids**, so it exists only where a developer is
looking at it. This was one of the four contradictions found by the GDD coverage
audit and is now closed.

### Adding a view

Subclass `Views.TerminalView`, implement `Id`, `ShortCode` (three letters) and
`Refresh()`, then register it in `BuildPanels`. The base class already gives you a
hidden vertical `ScrollView` with both scrollers hidden.

- `AddText(ussClass = "terminal-text")` — a readout label. Gets leading and gets
  wrapped by the shell's text policy.
- `AddFigure(ussClass)` — the same, plus `terminal-figure`: no leading, no
  wrapping. Use it for anything composed on the grid — maps, charts, multi-row
  bars.
- `MakeRow()` — a `button-row`. Build command rows this way: `button-row` is what
  the refusal explainer looks for, and a row assembled by hand will not get its
  refusals printed.
- `AddButton(row, text, extraClass, onClick)` — returns the `Button`, so the
  caller can refuse it. Six views carried a byte-identical private copy of this
  returning `void`, which is exactly why a precondition the caller knew about had
  nowhere to go.
- Take the width from `TerminalMetrics.Columns`, never a literal.
- If the view exposes a player action that spends a resource, call
  `ProgressionSystem.RecordInitiative` after the spend succeeds (spec 07).

### Refusing a command — `TerminalView.Block(button, reason)`

Every way a control is taken away goes through one helper, and three rules make
it work:

1. **A blocked button stays blocked.** The reason is recorded on the button
   (`userData`), and both gates run *after* `Build()`. `GateOnAffordability` used
   to call `SetEnabled(affordable)`, which handed back every button a panel had
   already refused for its own reasons — an exercise inside its cooldown, a
   programme the treasury cannot fund, a peace term that does not apply, a
   patronage payment with no money behind it. The operator was offered a control
   that spent nothing and reported nothing when pressed, which is what
   *"sometimes I press a button to use CP and it does not go down"* actually was.
   **Both gates now only ever disable.** A view rebuilds its buttons from scratch
   every refresh, so there is never anything legitimate to re-enable.
2. **The first reason wins.** Whichever gate refuses first has the most specific
   answer; "requires 2 CP" is a worse thing to be told than "we exercised with
   them last month".
3. **The reason is on screen, not in a tooltip.** There is no hover on a phone.
   `TerminalView.ExplainBlockedCommands` runs last in `Refresh()`, walks every
   `button-row`, and prints one wrapped `UNAVAILABLE: …` line beneath any row
   holding a refused command. Central, so a view added later cannot forget —
   the same reasoning as the text policy.

`GateOnAffordability` and `GateOnAuthority` each take an optional explicit
`GameState` so they can be exercised without a running session; the
no-argument overloads read `GameController`.

Guarded by `MapAndLayoutTests.TheAffordabilityGateNeverHandsBackARefusedCommand`,
`AnUnaffordableCommandIsRefusedWithItsPrice` and
`ARefusedCommandSaysWhyOnScreen`.

Views rebuild their entire content on refresh. With this data volume that is
cheap and it eliminates a whole class of stale-state bugs.

---

### MILITARY — THE WAR (belligerent roster)

Once honouring a pact can open a front and that front can call in the aggressor's
own guarantors (spec 04 §8), the operator is no longer in *a* war — they are in
several, with partners who joined for their own reasons and enemies they never
declared against. The front selector names the opponent of each front and nothing
else, so the one question a coalition war actually raises had no answer anywhere
in the game.

`Core/BelligerentRoster.cs` answers it. Two rules:

- **Derived, never stored.** Sides are read from live confrontations and
  coalitions each time, on the `StatusOf` / `VoteScore` precedent. A cached
  roster would be a second opinion about who is at war and would be wrong within
  a month.
- **Belligerency is public; strength is not.** Who has declared against whom is
  an observable fact and is reported plainly and completely. Nothing in the
  roster returns a capability figure — BALANCE OF FORCES still owns that, through
  `IntelReadout` — so this cannot become a back door around §10's fog rule. A
  state we have never collected on appears here by name and nowhere near a
  number.

The panel prints AGAINST US and WITH US, each row carrying **why** that state is
in the war ("they opened this front (Indo-Pacific)", "stands with China", "we came
to their defence"), a `COMMAND THIS FRONT` button for anyone we face directly, and
a STILL OWED line naming the states we would be obliged to defend — an alliance
earns its price mostly in the war that does not happen, and an operator who cannot
see what they hold cannot judge whether it was worth buying. `EnemiesOf` includes
states in an opponent's coalition even where no confrontation names the pair: they
are shooting at us either way, and an operator reading only their own fronts would
be surprised by exactly the states the cascade brought in.

**`PartnersOf` iterates coalitions, not our own fronts.** This is the one subtlety
in the file and it was wrong first time round: honouring a pact adds us to the
coalition on the *original* war — between our ally and their attacker, which does
not involve us — and then opens a separate front of our own. A roster that walks
only the confrontations we are party to cannot reach that coalition, so the
operator who had just come to a partner's defence read a screen saying they were
fighting alone, which is exactly the reading this panel exists to prevent.

Colour is the second channel as always — `sig-hostile` / `sig-ally` with `-` / `+`
glyphs, so the reading survives any palette and any colour vision. The name column
is `AsciiChart.Cell(..., NameWidth(W, 0.42))`, never a hardcoded width.

## 9. ASCII primitives

`UI/AsciiChart.cs` — plain C#, unit-tested, all exact-width. Use these instead of
hand-formatting; they are what guarantees alignment.

| Call | Output |
|---|---|
| `Bar(value, max, width)` | `██████░░░░` (`█` = `FillChar`, `░` = `EmptyChar`) |
| `LabeledBar(label, v, max, labelWidth, barWidth)` | `MILITARY     ██████░░░░   52.3` |
| `BoxHeader(title, width)` | `┌─ STRATEGIC BRIEFING ─────────┐` |
| `Divider(width)` | `──────────` |
| `Row(label, value, width)` | `TREASURY ............... 1240` |
| `Cell(text, width)` | Exactly `width` characters (§6.3) |
| `NameWidth(width, share)` | Adaptive name-column width (§6.3) |
| `LineChart(values, width, height)` | `height` rows of `/ \ ─` plus a value scale and a `└───` axis |
| `Sparkline(values, max)` | `▁▂▃▅▇█` from a 9-level ramp |
| `WrapBlock(block, width)` | Hard wrap (§4) |

### The two map renderers

Both are authored at one size and **sampled** into whatever grid they are given,
so the same chart reads on a cover screen at roughly half the columns and half
the rows.

**`AsciiWorldMap`** — authored silhouette 78×21, deliberately impressionistic: a
briefing-room chart, not a projection. `Width` is declared
`= GeographySystem.MapWidth` and must never be redeclared as a literal: longitude
wraps at that value in the distance model, and a renderer that disagreed with it
would draw a world the simulation does not measure (spec 01 §3b).
`Render(state, selected)` defaults to
`TerminalMetrics.Columns × TerminalMetrics.MapRows`; the explicit overload floors
at 28 columns and 8 rows. Chokepoints are plotted first as `#` so a country
marker always wins the cell. Each country prints its two-letter `mapCode` framed
by a standing glyph (`[XX]` ours, `>XX<` selected, otherwise `! ~ - +` or space).

This is **countries, routes and chokepoints — not tiles.** It is not a
province-painting board (GDD §16).

**`AsciiCountryMap`** — the country-scale chart, drawn as a rounded box with
installations placed inside it. Site positions are derived from a hash of the
location id, so a site sits in the same place every time you look at it. Markers:
`@` capital (followed by `=XX`), `P` port, `A` airbase, `I` industry, `E` energy,
`#` chokepoint, `^` pass, `*` foreign force present, `!` occupied. Clamped to
30–100 columns and ≥ 7 rows.

---

## 10. Fog discipline in the UI

**Rule: a view must never print a foreign country's true value.** Everything
foreign routes through `IntelligenceSystem.GetEstimate` or the `IntelReadout`
wrapper. This is the same rule as spec 06 §1, restated here because the UI is
where it gets broken.

`IntelReadout.ForPillar` / `ForDomain` render capability the way the operator
actually receives it:

```
EST 40-55    CONF MODERATE    (AS OF MAR 1987)
```

`NO ASSESSMENT` when there is no estimate or confidence is `None`. The as-of date
appears only once the estimate is more than three months stale. Intelligence
itself has no domain — `ForPillar(Pillar.Intelligence)` returns
`OPAQUE — RIVAL SERVICES DO NOT REPORT ON THEMSELVES`.

`AsciiWorldMap.Describe` follows the same split: government type, leader and
market index are public and exact; military and economy for a foreign state go
through `IntelReadout`; occupied territory is visible to everyone, because an
occupation is not a secret. Only `country.isPlayer` prints raw pillar values and
posture.

### `AsciiCountryMap` detail levels

`LevelFor(state, countryId)` — the clearest expression of the fog anywhere in the
game, because the *same* country renders completely differently depending on what
you have collected. Military reporting governs, since that is what tells you
about installations, but any network at all lifts you off the public floor.

| Level | Reached by | Chart | Written readout |
|---|---|---|---|
| `Public` | default | Border and capital only | "Our reporting does not extend inside this country." Nothing else is named |
| `Sites` | confidence ≥ `Low` **or** penetration ≥ 20 | All known sites plotted by type | Site names and types; occupation shown. Closes with a prompt that condition is still unknown |
| `Detailed` | confidence ≥ `High` **or** penetration ≥ 55 | Plus `*` for foreign basing | Garrison as a range with a confidence grade; foreign forces named |
| `Complete` | it is our own country | Everything | Exact garrison and defence values |

A compromised network contributes zero penetration. `DescribeLevel` prints the
level in the view's COVERAGE line, so the operator always knows *why* a screen is
thin: `NO COVERAGE — PUBLIC INFORMATION ONLY` is information.

`MapAndLayoutTests` asserts the whole ladder:
`WithNoCollection_ACountrysInteriorIsNotVisible` (their industry must not be
named to someone who has never looked), `CollectionOpensTheInterior_ByDegrees`,
`OurOwnCountry_IsAlwaysComplete`, and
`AForeignBase_IsOnlyVisibleWithGoodCollection` — knowing who operates from a
rival's soil is what collection is *for*, and the MAP view gives it its own
panel for that reason.

---

## 11. Shell chrome

### Status bar

Left: the classification banner, with a block cursor blinking every 530 ms. This
is the only ambient animation in the game.

Right, in order: **DSP** (§7), a `■ CRISIS` indicator shown only while a blocking
crisis is open, the date (`MAR 1984`), the resource readout
(`CP 5  INF 12  PC 40`), and **END MONTH**.

### Notification classes (GDD §28.2)

Colour-coded on the Briefing's priority traffic, sorted
FLASH → PRIORITY → ADVISORY → WIRE → ARCHIVE.

**One `Label` per entry, carrying a USS class** — never an inline `<color=…>` hex
inside a single rich-text label. `BriefingView.TrafficClass` maps the class to the
palette-aware semantic set from §6.2:

| Class | USS class | Used for |
|---|---|---|
| FLASH | `sig-hostile` | Traffic with a decision on the desk this month |
| PRIORITY | `sig-rival` | Consequences that have already landed: setbacks, operation results, treaties signed, sanctions, settlements, annual evaluation |
| ADVISORY | `terminal-text-bright` | Month start, official successes, de-escalation, exercises, skills |
| WIRE | `terminal-text` | Foreign news the player is not party to |
| ARCHIVE | `terminal-text-dim` | Filed reference material — the year-in-review record |

**FLASH means a decision is required, not that something bad happened**
(GDD §28.2). The complete list of producers is short and should stay that way:

| FLASH traffic | Source |
|---|---|
| A Crisis Turn opening | `CrisisSystem.Trigger` |
| CRISIS PENDING (one is still unanswered) | `TurnManager.StartMonth` |
| An alliance obligation called in | `AllianceSystem.InvokeObligations` |
| CONFRONTATION OPENED AGAINST US | `ConfrontationSystem` |
| CONSPIRACY DETECTED | `RegimeSystem` |
| GOVERNMENT SEIZED BY FORCE | `RegimeSystem` |
| ESCALATION JUSTIFIED | `EndgameSystem` |
| An existential instrument used **against us** | `EndgameSystem.Reach` |

Everything else that used to be FLASH is now PRIORITY or WIRE. Roughly seventeen
sites raised FLASH, most of them reports of things that had already resolved: a
war produced one every month, which is precisely how a klaxon stops being one.
Resolved operation results, enemy operation reports, DEPRESSION, sanctions imposed
on us, NEW ADMINISTRATION, CONTESTED SUCCESSION, NETWORK COMPROMISED, OPERATION
EXPOSED and COUP ATTEMPT DEFEATED are all consequences to read, not choices to
make.

`EndgameSystem.Reach(actor, target)` states the rule in code: something done *to*
us is FLASH, something **we** did is PRIORITY (a confirmation of an order already
given), and two other states doing it to each other is WIRE.

One of the demoted sites was also not player-gated at all: CONTESTED SUCCESSION
fired at FLASH for a leadership fight in *any* country, so the world's ordinary
political churn raised a klaxon on the operator's own briefing. It is now
`isPlayer ? Priority : Wire`, which is the pattern every country-scoped
notification should follow. Tests:
`PartialSystemsTests.ResolvedOperations_AreReportsNotFlashTraffic`,
`ForeignLeadershipStruggle_DoesNotFlashOurBriefing`.

**ARCHIVE has exactly one producer:** `ProgressionSystem.FileYearInReview`, which
files a "YEAR IN REVIEW {year}" entry at each annual evaluation — chronicle counts
per category for the year, crises faced and resolved, and operator decisions
logged (spec 07 §3). The class existed in the enum, in the stylesheet and in the
Briefing filter with nothing producing it, which made it a permanently empty
drawer. A year's record is what belongs there: nothing to act on this month, and
findable later. Test: `PartialSystemsTests.YearEnd_FilesArchiveTraffic`.

This buys two things an inline hex cannot. The colours now **follow whichever
palette the operator chose** — a hardcoded green-family literal was correct only
on `theme-green` and was never contrast-checked against amber, soft or signal.
And the width measurement is honest: a label whose colour comes from a class has
no markup in its text at all, so `WrapBlock` measures exactly what the reader
sees (§4).

**Prefer this pattern for any coloured readout.** It is the reason §6.2's set
exists and the reason the semantic colours are asserted against all four bases.

Capped at 200 entries in `GameState`.

### Modal interrupt

The Crisis Turn overlay is the only modal in the game: a red-bordered panel over
the shell with a FLASH banner, the situation, and 2–3 options each carrying a
consequence hint.

`TurnManager.EndMonth` **refuses** while a blocking crisis is open and logs a
warning; the button itself is not disabled, and the `■ CRISIS` indicator is the
visible cue. Do not add further modals casually — the interrupt has weight
because it is rare.

### First launch

With no save, `UpdateSessionMode` hides the nav rail, the date, the CP readout
and END MONTH, and shows `AssessmentScreen`: ten scenarios, then the posting /
intervention screen. Accepting builds the world, switches the shell to terminal
mode and selects BRIEFING. A full reset returns here. `SelectView` is a no-op
while `AwaitingAssessment`, so nothing can navigate out from underneath it.

`TutorialPanel` sits above the views in the content host, refreshes with the
shell, never blocks and is always dismissable (GDD §5).

---

## 12. Open questions / PLANNED

Honest list. Several of these are things the GDD asks for that do not exist.

Four entries that used to sit here are **fixed** and are documented in place
rather than as open work: the breakpoints are now measured in columns (§5), rich
text no longer counts toward the wrapped line width (§4), the notification
classes use palette-aware USS classes instead of inline hexes (§11), and the
market-index chart is an `AddFigure()` label built to `max(24, Columns − 6)`
instead of a hardcoded 48 (§9). Do not reopen them without reading why they were
closed.

- **Touch targets: decided and enforced at 44 panel px.** `.cmd-button` was ≈31
  and `.nav-button` ≈35, with `bp-short` compressing both to about **23** — which
  is why the terminal felt fiddly on a real handset. All three interactive
  selectors (`.cmd-button`, `.nav-button`, `.unity-base-slider`) now carry
  `min-height: 44px`, the slider dragger is 22×22, and `.bp-compact .nav-rail`
  grew from 44px to 54px so it can hold a full-size button plus margins.

  **Why 44 panel pixels, with no DPI anywhere.** The panel is
  `ConstantPixelSize` with a scale derived from resolution to hit a column target
  (§5), precisely because `Screen.dpi` is not trustworthy. That makes a panel
  pixel proportional to physical size on every device, so the target can be
  stated in panel pixels and verified without hardware. The familiar 44pt
  guideline is roughly 3.4× body text; body text here is `TerminalScale.BaseFontPx`
  = 13, so 44 panel px is the direct analogue and lands near 7 mm on a phone.

  **The `bp-short` trade was decided against shrinking.** A cramped screen may
  buy back rows with smaller *text* and tighter margins — both of which
  `bp-short` still does — but not with a smaller target, because a control you
  cannot reliably press is broken whatever shape the screen is. `min-height` is
  deliberately not overridden there, and `TouchTargetTests` fails the build if any
  breakpoint reduces it.

  `TouchTargetTests` also pins the reasoning: it asserts `BaseFontPx` is still 13,
  so if body text ever moves, the constant has to be re-derived by a person rather
  than silently drifting.
- **Colouring glyphs inside an ASCII figure is still not possible.** `WrapBlock`
  now measures visible characters (§4), which was half of it, but the figure
  labels still lack `-unity-rich-text` and the overflow guards in
  `MapAndLayoutTests` count raw string length — so a `<color=…>` tag inside a map
  row would be misread as content and trip the guard. Both the label flag and the
  test-side measurement need to move to `AsciiChart.VisibleLength` together.
  **PLANNED.** Until then, colour lives on whole labels, never on glyphs inside a
  figure, and the map's `! ~ - +` glyphs remain the primary channel.
- **`terminal-figure` is still under-used.** `WorldMapView` and `EconomyView`'s
  market-index `LineChart` call `AddFigure()`; the remaining `Sparkline` blocks
  and stacked bar groups are still emitted into plain `AddText` labels, so they
  receive paragraph spacing between rows and are subject to wrapping. Sparklines
  are single-row and survive it, which is why this is untidy rather than broken —
  but any new multi-row figure must use `AddFigure()` with a measured width.
- **The three-layer information hierarchy of GDD §28.1 is really one flat
  layer.** The design calls for Briefing → pillar dashboards → deep terminal.
  What exists is thirteen sibling views in one rail; the "deep" material (
  after-action logs, the estimate board, the evaluation archive, exercise
  records) lives inside whichever dashboard owns it rather than behind a distinct
  tier, and CHRONICLE is a peer of BRIEFING rather than a level below it.
- **"Official competence influences what is surfaced or missed" is built, but
  only drops and buries.** GDD §28.1's requirement is met by `ReportingSystem`
  (spec 15 §9–§18): every notification carries a `ReportingDesk`, each pillar desk
  is filtered by the competence and trust of the official who runs it, and the
  CABINET view surfaces the resulting quality as a `REPORTING` bar so the penalty
  is legible rather than mysterious. Command traffic and FLASH are never filtered,
  so incompetence costs awareness and never agency.

  What is still missing is the other half of "surfaced or missed": the filter
  never **delays** an item to a later month and never delivers one **distorted** —
  a figure wrong by a margin, a confidence grade inflated, an attribution
  mistaken. Delay is the cheaper of the two (a `holdUntil` date on `Notification`
  and a release pass at the top of `FilterMonth`) and would produce the
  distinctly governmental experience of learning something true three months after
  it would have been useful. Spec 15 §19.

  *(This entry previously claimed the whole requirement was unbuilt, which
  contradicted spec 15 outright. Two specs disagreeing about whether a shipped
  system exists is the staleness the §36 audit was meant to end — when a system
  lands, sweep the open-questions lists that named it as missing.)*
- **No command prompt.** The UI is entirely button-driven; there is not a single
  `TextField` in the project. A terminal the operator can type into — even with a
  tiny verb set — is the most obvious unexploited affordance of the fantasy, and
  would also give the SYSTEM console somewhere to go.
- **No CRT effects** (GDD §4) — scan lines, restrained flicker, typing effects.
  The blinking classification cursor is the only ambient motion. When added they
  must be optional, and must not interact badly with `theme-soft`, whose entire
  purpose is to reduce visual noise.
- **No ASCII institutional art.** Ministries, headquarters, seals and situation
  rooms are all prose; the two map renderers are the only pictures in the game.
- **`BlinkCursor` clobbers the assessment banner.** `UpdateStatusBar` sets
  `STRATEGIC APTITUDE ASSESSMENT // CLASSIFIED` while awaiting assessment, and
  the 530 ms blink schedule overwrites it with `UNKNOWN GAME // CLASSIFIED`
  within half a second. Cosmetic, one-line fix.
- **Some chrome colours are still not palette-aware.** The notification classes
  are fixed (§11), but `.log-line-warning`, `.log-line-error` and the whole crisis
  overlay are still authored against the green base and are not re-specified per
  theme. They are contrast-checked against the green background only, so they
  clear AAA where they are tested and nothing asserts them against amber, soft or
  signal. The §6.2 semantic set is the natural replacement, exactly as it was for
  the traffic classes.
- **One skill-gated verb has no affordance at all.** Three of the four are
  handled correctly: `MilitaryView`, `EconomyView` and `IntelligenceView` call
  `CanSetPosture` / `CanImposeSanctions` / `CanRunCovertOperation` and dim the
  option with the returned reason, which is the pattern to copy. But
  `MilitaryView` only ever calls `BeginProcurement(…, ProgramScale.Major)` —
  **there is no Transformative button in the UI at all**, so the `ECO_STRATEGIC`
  skill buys something the player cannot reach. It needs a scale selector and a
  `CanBeginProcurement(…, out reason)` to gate it (spec 07 §6).
- **No localization.** No string externalization exists. Note that it interacts
  with everything above: a translated string is a different length, and every
  width in this document is measured in characters.
