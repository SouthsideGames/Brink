# Running the suite without Unity

A second way to compile and run `Assets/Tests/EditMode`, for environments that
have no Unity — which is most of the ones this project's code has been written
in, and the reason several commits shipped with "not verified by a test run" in
their message.

```
sudo apt-get install -y dotnet-sdk-8.0     # in Ubuntu's own archive
bash Tools/dotnet-harness/run.sh           # ~7 minutes, ~1300 tests
bash Tools/dotnet-harness/run.sh --filter "FullyQualifiedName~CausalityTests"
```

## What it is

`shim/` provides the slice of `UnityEngine` the runtime actually uses: element
trees and style properties for UI Toolkit, `PlayerPrefs`, `Mathf`, `Screen`,
`Application`, the audio classes, and a `JsonUtility` written to Unity's rules —
public instance fields only, enums as ints, and **a key absent from the JSON
leaves the constructed default in place**, which is what the save-compatibility
tests depend on.

## What it is NOT

**It does not replace `Tools/run-suite.sh`. Unity remains the authority.**

Nothing here lays anything out, loads a `Resources` asset, or parses a `.uss`
file from the asset database, so about 23 tests fail here that pass in Unity —
every readability, palette, touch-target and audio-library test, plus the three
that scan source directories by Unity-relative path. Those are a property of the
harness, not of the code. A green run here means *the simulation logic is
sound*; it says nothing about stylesheets, assets, layout or the editor.

Because of that, the useful mode is **comparison**: run it on your branch and on
the commit before it, and diff the failing-test names. That is how Phase A of the
causal explainability work (spec 26) was validated — identical failure sets
before and after proved the 30 remaining failures all predated the change.

Both projects write to their own `obj/`; do not put two `.csproj` files in one
directory or they will clobber each other's restore.
