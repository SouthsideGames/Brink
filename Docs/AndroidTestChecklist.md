# Brink — Android test checklist

First device build. Work top to bottom; the order is deliberate, because a
failure high up makes everything below it meaningless.

Build: **Brink → Build Android APK** from the editor menu. Output lands in
`Builds/Android/Brink-0.1.0.apk`. Install with
`adb install -r Builds/Android/Brink-0.1.0.apk`.

Before building, run **Window → General → Test Runner → EditMode → Run All**.
It should be all green. The layout and map changes compile clean but were
written while the editor held the project lock, so that run is the first real
execution of their tests.

Keep a log open while you test — `adb logcat -s Unity` — and note anything that
appears in red. Most of what you'll want to report back is in there.

---

## 0. It runs at all

- [ ] APK installs without a "package appears invalid" or architecture error.
- [ ] App launches to the **assessment screen**, not a black screen or a Unity
      splash that never leaves.
- [ ] No `[BOOT]` **error** in logcat — that means the terminal UI resources
      didn't load and the game aborted before starting.
- [ ] There *is* a `[BOOT]` info line reporting the layout it chose, e.g.
      `screen=2316x904 dpi=402 scale=3.62 font=47px targetCols=82`.
      **Send me this line for each fold state.** If `font=` is under about 20px,
      text will be unreadably small and that single number tells me why.
- [ ] Force-quit and relaunch: it resumes where it was.

> If the screen is black but logcat is clean, the UI Toolkit panel built but the
> theme didn't. That's the `BrinkTheme` / `TerminalShell` pair in `Resources/UI/`.

## 1a. Foldable — the reported problems

Both of these were real bugs, now fixed. This section is the retest.

**Text running off the right edge.** The terminal is a character grid under
`white-space: pre`, so nothing wraps — and every view hardcoded 64–78 columns,
which fits a tablet and overflows a phone. The width is now *measured* from the
real panel at runtime and every view builds to it.

**Second fix, after the first attempt was incomplete.** Sizing each view's boxes
to the panel was not enough: the box rules fitted, but prose written *inside*
them — notification bodies especially — is arbitrary length and had nothing
constraining it. Long lines are now hard-wrapped to the terminal width with a
hanging indent, applied centrally so no view can forget.

- [ ] **BRIEFING → PRIORITY TRAFFIC** is the line to check first. FLASH and
      PRIORITY entries were the visible offenders. They should now wrap onto a
      second, indented line instead of running off the right.
- [ ] **Unfolded:** no line of any view is cut off on the right. Check the
      widest screens first — MAP, CHRONICLE, STRATEGIC, STRATEGIST.
- [ ] Box rules (`────`) end on screen, not past it.
- [ ] **Folded (cover screen):** same check. Content should be *narrower*, not
      clipped.
- [ ] Unfold mid-session: the layout should re-flow to the wider screen without
      restarting.

**Cover screen looked bad.** It's wide and very short, so height is the scarce
resource. The shell now detects a short panel and puts the nav rail back to a
narrow *vertical* strip (spending abundant width) instead of a horizontal band
across the top (spending scarce height), and tightens fonts and padding.

- [ ] Folded: nav rail is a narrow vertical strip on the left, not a bar on top.
- [ ] Folded: you can see a useful number of content lines, not two.
- [ ] Folded: the world map is shorter but still readable.
- [ ] Unfolded: rail returns to its normal form.

## 1b. Display preferences — new, and the answer to most layout complaints

There is now a **DSP** button in the status bar (ships in every build, unlike
SYSTEM). It sets text size, palette and line spacing, and persists per device.

- [ ] DSP opens the panel; CLOSE dismisses it.
- [ ] **TEXT SIZE** — SMALL / MEDIUM / LARGE / LARGEST. Each visibly changes the
      type, and the layout re-flows immediately (fewer columns at larger sizes —
      the screen doesn't grow, so something has to give).
**Defaults are now AMBER + SMALL**, chosen on device. There is a SMALLEST step
below Small for anyone who wants more density.

- [ ] **PALETTE** — GREEN / AMBER / LOW GLARE / **SIGNAL**. All four should be
      comfortably readable; LOW GLARE should look softer, not washed out.
- [ ] **SIGNAL is the one to judge.** Neutral bone base instead of green, which
      frees colour to mean something: on the MAP the country buttons are now
      tinted by standing — red hostile, orange rival, grey neutral, blue
      friendly, teal allied. Compare it against GREEN and tell me which reads
      faster when you're scanning sixteen countries.
- [ ] **LINE SPACING** — COMFORTABLE / COMPACT. Compact removes the gap between
      rows and fits noticeably more on screen.
- [ ] ASCII maps and charts must keep their rows **touching** in both spacing
      modes — if the world map develops horizontal gaps, that's a bug.
- [ ] RESET TO DEFAULTS returns to MEDIUM / GREEN / COMFORTABLE.
- [ ] Force-quit and relaunch: your choices are still there.

On the cover screen the settings span roughly 45px text at 20 rows (SMALL +
COMPACT) to 78px at 9 rows (LARGEST). **Tell me which setting you actually
settle on** — that's the one the default should probably be.

## 1. Screen and orientation

The whole UI is text on a grid, so this is where phone reality bites hardest.

- [ ] Starts in **landscape** and stays there. Rotating to portrait does nothing.
- [ ] Rotating 180° (landscape-left ↔ landscape-right) works and re-lays out.
- [ ] **Nothing is under the notch/cutout or the gesture bar.** The shell pads for
      safe area itself — if text is clipped, that logic is wrong on your device.
- [ ] ASCII boxes, bars and the world map **line up** — no ragged right edges.
      Misalignment means the monospace font didn't load and it fell back.
- [ ] Text is legible without squinting. It is sized in physical units, so it
      should look the same size as on a tablet, not smaller.

## 2. Touch

Known weak point: buttons are ~32–36pt tall against a ~44pt mobile guideline.
This is the pass where you find out whether that's actually a problem.

- [ ] Every nav-rail button is tappable **first time**, not on the second try.
- [ ] Buttons in dense rows (escalation levels, peace terms, pivot) don't
      mis-fire onto their neighbour.
- [ ] Scrolling a long view (CHRONICLE, STRATEGIST) works with a normal flick
      and doesn't accidentally press a button under your thumb.
- [ ] Note any control you had to aim at. That list is the fix list.

## 2b. Running out of Command Points

Previously, spending your last CP just made every button stop working, which
reads as the game breaking rather than the month being over. Two signals now say
the same thing.

- [ ] Spend down to **CP 0**. The status bar should read `CP 0 — MONTH SPENT`
      and go dim.
- [ ] **END MONTH pulses** — a slow breath, roughly every 0.7s, not a flash. It
      should read as an invitation to move on, not an alarm.
- [ ] Buttons costing CP are dimmed and unpressable; buttons costing nothing
      (cabinet modes, settlement proposals, view switching) still work.
- [ ] End the month. The pulse stops as soon as capacity returns.
- [ ] **With a crisis pending, END MONTH must NOT pulse** even at CP 0 —
      pressing it would be refused, so pointing at it would be a lie. The crisis
      overlay is the signal in that case.
- [ ] The pulse is legible in all four palettes.

## 3. The core loop

- [ ] Complete the **assessment** — 10 questions, then a posting.
- [ ] The posting screen offers **one reassignment**, and accepting starts the game.
- [ ] Briefing shows the current month, CP, and priority traffic.
- [ ] **END MONTH** advances the date and the world reacts (new wire entries).
- [ ] Run 12 months. The year-end **evaluation** appears with a grade and
      component scores.
- [ ] Tutorial steps appear early, can be dismissed, and never block input.

## 4. Each pillar opens and does something

For each of MILITARY, ECONOMY, INTELLIGENCE, DIPLOMACY, GOVERNMENT, CABINET,
TECHNOLOGY, STRATEGIC, STRATEGIST, MAP, CHRONICLE:

- [ ] Opens without an exception in logcat.
- [ ] Numbers are populated (not all zeros / "NO ASSESSMENT" everywhere).
- [ ] At least one action button works and visibly changes something.

## 5. This build's new work — the things most likely to be wrong

These all landed today and have never been touched by a human hand.

**Territory now pays (`TerritorySystem`)**
- [ ] Open MILITARY → strategic locations. Foreign garrisons show as a **band**
      with a confidence grade (`GAR ~40-55 (MOD)`), your own as an exact number.
- [ ] Win a confrontation and capture an energy region or industrial centre.
      Over the following months, your **energy or industrial capacity should
      rise** on the ECONOMY/BRIEFING screens.
- [ ] While holding foreign ground, **treasury drains faster** and stability
      slips. Conquest should feel like a bill, not a windfall.
- [ ] The country you took ground from should get *harder*, not softer — watch
      its war support if you have intel on it.

**Primary Strategy actually decides the campaign**
- [ ] Open a confrontation with Primary Strategy **Economic**. Sanction them
      heavily. `ASSESSMENT` on the peace panel should move toward them signing
      **without you fighting a single battle.**
- [ ] Use **PIVOT** mid-confrontation. It should cost 3 CP and visibly cost you
      momentum.
- [ ] Try an operation while still at Tension/Crisis. It should be **allowed**,
      cost extra CP, and jump the confrontation to Limited Conflict by itself.

**Constitutional authority (`AuthoritySystem`)**
- [ ] In CABINET, try **DIRECT CONTROL** on each of the five officials. Under
      your government type some should work immediately, some should cost
      Political Capital, and some should be refused outright with a message.
- [ ] Declare **emergency powers** in GOVERNMENT, then retry a refused pillar.
      It should now be permitted.
- [ ] Confirm a refusal never silently does nothing — you should always see why.

**The map, and the new country zoom**

- [ ] MAP: the world chart fits the screen at both fold states and every country
      code is still visible after scaling.
- [ ] Select a country, press **OPEN ▸** (or tap any country while zoomed). You
      get that country's own chart.
- [ ] **◄ WORLD MAP** returns you.
- [ ] Open **your own country**: coverage reads "OWN TERRITORY — COMPLETE" and
      every installation is listed with exact garrisons.
- [ ] Open a country you have **no network on**: coverage reads "NO COVERAGE",
      you see a border and a capital, and the readout says our reporting does
      not extend inside. **You should not see its industry or energy sites.**
- [ ] Establish a network on it (INTELLIGENCE → ESTABLISH NETWORK), run a few
      months, reopen: coverage should climb to "PARTIAL — MAJOR SITES ONLY" and
      the sites appear by name.
- [ ] Keep collecting (EXPAND NETWORK) until "GOOD COVERAGE". Now garrisons
      appear as bands, and a **FOREIGN FORCES ON THIS SOIL** panel appears.
- [ ] Sign a treaty including **TRANSIT** with a partner, wait a month, then open
      their country: you should be listed as operating from one of their
      airbases or ports. Break the treaty — it should go away.

> The point of this screen is that the *same country looks different* depending
> on what you've paid for. If a country with no network shows you its interior,
> that's a fog-of-war bug and the most important thing to report.

**Skill-gated verbs explain themselves**
- [ ] With no skills unlocked, MILITARY's **FORWARD** posture is disabled and
      says it needs Forward Basing.
- [ ] ECONOMY's **EXISTENTIAL** sanctions and INTELLIGENCE's **DECEPTION**
      likewise say what they need rather than doing nothing.

## 6. Saving

- [ ] Play ~10 months, force-quit from the app switcher, relaunch. **Same month,
      same state.**
- [ ] Airplane mode on: everything still works (the game is fully offline).
- [ ] SYSTEM view should **not exist** in this build — it's editor/dev only now.
      If you can see it, the release gate isn't working.
- [ ] Settings → **FULL RESET** returns you to the assessment, and relaunching
      does *not* restore the old country.

## 7. Performance and battery

- [ ] END MONTH resolves in well under a second. Note anything that hitches.
- [ ] Scrolling is smooth.
- [ ] Leave it open 10 minutes on the briefing — the device shouldn't get warm.
      It's a static text UI; sustained load means something is spinning.
- [ ] Memory in logcat stays flat across ~24 months.

## 8. Long-session sanity (do this last, it takes a while)

- [ ] Play or fast-forward ~5 in-game years.
- [ ] No stat ever shows as negative or above 100 where it shouldn't.
- [ ] No number displays as `NaN` or `∞`.
- [ ] Manpower on the briefing never goes negative.
- [ ] The world still feels alive — foreign wars, elections, coups in the wire.

## 9. Session review — read this before you write anything down

The build records what you did, what the world did, and a monthly snapshot, and
then tells you what looked wrong. **With one tester this is the highest-value
part of a play session**, so do it before writing up impressions: it catches the
class of fault that is invisible from inside a single evening's play.

Nothing leaves the device. There is no network call, no identifier and no SDK
anywhere in it — the recorder writes a file next to the saves and stops there.

- [ ] SYSTEM → **SESSION REVIEW**. It should say `► RECORDING ON` in a
      development build. If it says OFF, tap it and start a fresh session — a
      recording covers one continuous run and cannot be turned on halfway.
- [ ] Play at least **12 months**; below that it will correctly refuse to draw
      conclusions.
- [ ] Read the findings. Anything in **orange is a SUSPECT** — a value that only
      ever moved one way, or a world doing nothing. Those are the ones worth
      reporting even if the game *felt* fine.
- [ ] Warnings are softer: capacity left unspent, an action you kept being
      refused, crises you kept ignoring. If a warning matches something that
      annoyed you, say so — that pairing is the useful signal.
- [ ] Tap **WRITE SESSION FILE**, then pull it off the device:

```
adb pull /sdcard/Android/data/com.southsidegames.brink/files/sessions/
```

The file name carries the **seed and country**, which is what makes a session
reproducible — a report with the file attached is a report I can replay.

---

## Known gaps — not bugs, don't report these

- **No app icon yet.** You'll get the default Unity icon.
- **Unity splash screen** on launch — removing it needs a Pro licence.
- No sound at all. None is implemented.
- Touch targets are now 44 panel px and test-enforced. What is **not** known is
  the layout cost: every button grew ~30% taller, so MILITARY and GOVERNMENT are
  the screens to check for excessive scrolling.
- The GDD says "fictional countries"; the game ships real ones. That was your
  call and the GDD text is simply stale.

## Signing

This build is signed with Unity's **debug keystore**. That's fine for sideloading
onto your own device and nothing else — Google Play will reject it.

When you want a Play upload, create a keystore in
`Edit → Project Settings → Player → Publishing Settings`, keep the file and its
passwords somewhere safe and backed up, and **never commit them**. Losing that
keystore means never being able to update the app under the same listing. I've
deliberately not created one for you.

## If a build fails

`Brink → Check Android Readiness` reports config problems without building.
For build failures proper, the real error is usually 200+ lines above the final
"Build failed" line in the editor log:
`%LOCALAPPDATA%\Unity\Editor\Editor.log`.
