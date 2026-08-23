#!/usr/bin/env bash
# Print the name and assertion message of every non-passing test in a Unity
# NUnit3 results file. Unity nests suites, so a plain grep for result="Failed"
# also matches the ancestor suites; this walks test-case elements only.
f="${1:-/c/Temp/brink_test_results.xml}"
log="${2:-/c/Temp/brink_test_log.txt}"

# A run that did not happen is NOT a pass.
#
# Two ways this file can report a confident green for a run that never
# executed, and both have actually happened:
#
#   1. A test assembly that fails to compile. Unity keeps the last good DLL and
#      re-runs it, so the totals are the previous run's.
#   2. Unity refusing to start at all — most often because the editor is open on
#      the same project, since batch mode cannot share the project lock. Nothing
#      is written, and the results file simply still holds the last run.
#
# Both leave a *stale* results file that looks perfect. Check the log first.
stale=0

if [ -f "$log" ] && grep -a -q "error CS" "$log"; then
  echo "!!! COMPILE ERRORS — the results below are from a STALE assembly:"
  grep -a -o "Assets[\\/][^:]*([0-9]*,[0-9]*): error CS[0-9]*: .*" "$log" | sort -u
  echo
  stale=1
fi

if [ -f "$log" ] && grep -a -q "Aborting batchmode due to fatal error" "$log"; then
  echo "!!! UNITY DID NOT RUN — the results below are from a PREVIOUS run:"
  grep -a -A 3 "Aborting batchmode due to fatal error" "$log" | sed 's/^/    /'
  echo
  stale=1
fi

# The log should be thousands of lines for a real run. A couple of dozen means
# Unity exited during startup and never reached the tests.
if [ -f "$log" ] && [ "$(grep -ac '' "$log")" -lt 200 ]; then
  echo "!!! LOG IS ONLY $(grep -ac '' "$log") LINES — Unity exited during startup."
  echo "    The results below are from a PREVIOUS run and mean nothing."
  echo
  stale=1
fi

# A run that was killed part-way leaves a complete-looking results file from the
# PREVIOUS run and a log with no compile error and no abort message, so every
# check above passes and the totals read green. This has happened.
#
# Unity shuts down cleanly through the package manager and the memory-leak
# report; neither appears if the process was killed. Their absence in a log
# that is otherwise long and healthy means the run did not finish.
if [ "$stale" = "0" ] && [ -f "$log" ] \
   && ! grep -a -q "Server process was shutdown" "$log"; then
  echo "!!! THE RUN DID NOT FINISH — Unity never shut down cleanly (killed, or still running)."
  echo "    The results below are from a PREVIOUS run."
  echo
  stale=1
fi

# NOTE: do not compare the timestamps of the log and the results file. Unity
# writes the results when the tests finish and keeps appending to the log until
# it exits, so the log is *always* newer on a healthy run. An earlier version of
# this check fired on every successful run — and a guard that cries wolf gets
# ignored, which is worse than not having one.

if [ "$stale" = "1" ]; then
  echo "    Close the Unity editor and re-run, or run the suite in-editor:"
  echo "    Window -> General -> Test Runner -> EditMode -> Run All"
  echo
fi

grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$f" | head -1
awk '
  /<test-case /  { inCase = 1; buf = ""; failed = 0 }
  inCase         { buf = buf $0 "\n" }
  /<test-case /  { if ($0 ~ /result="Failed"/) failed = 1 }
  /<\/test-case>/ {
      if (failed) {
          match(buf, /name="[^"]*"/); n = substr(buf, RSTART+6, RLENGTH-7)
          print "### " n
          if (match(buf, /<message><!\[CDATA\[[^]]*/)) {
              m = substr(buf, RSTART+18, RLENGTH-18); print m
          }
          print ""
      }
      inCase = 0
  }
' "$f"
