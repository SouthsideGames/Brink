#!/usr/bin/env bash
# Fast syntax/type check of Assets/Scripts without opening Unity.
#
# Compiles the runtime assembly with Unity's own Roslyn against its NetStandard
# 2.1 reference set and every UnityEngine module. Takes a few seconds against
# the ~20 minutes a partitioned suite run costs, so it belongs before every
# Unity invocation, not instead of one.
#
# **Runtime code only.** Assets/Tests cannot be checked this way: the NUnit
# assembly Unity ships is a net40 build, so every [Test] attribute raises
# CS0012 mscorlib and the resulting flood *suppresses semantic binding in the
# files behind it* — a genuinely broken call goes unreported while the output
# looks merely noisy. Filtering the CS0012 lines out makes it look clean, which
# is worse than not running it at all. For test code, Unity is the authority.
#
# The file list is rebuilt on every run on purpose. A checked-in response file
# goes stale the moment a source file is added and then quietly compiles the
# old set, which is the same class of hazard as a validation harness that does
# not run the game.
#
# Usage: bash Tools/syntax-check.sh   (works with the editor open)

set -u

UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor"
HERE="$(cd "$(dirname "$0")" && pwd)"
SOURCES="$(dirname "$HERE")/Brink/Assets/Scripts"
RSP=/c/Temp/brink_syntax.rsp

if [ ! -f "$UNITY/Data/DotNetSdkRoslyn/csc.dll" ]; then
    echo "Roslyn not found under $UNITY — is the editor version still 6000.3.9f1?"
    exit 2
fi

{
    echo "-target:library"
    echo "-out:C:\\Temp\\brink_syntax.dll"
    echo "-nostdlib+"
    echo "-noconfig"
    echo "-langversion:9.0"
    echo "-r:\"$(cygpath -w "$UNITY/Data/NetStandard/ref/2.1.0/netstandard.dll")\""
    find "$UNITY/Data/Managed/UnityEngine" -maxdepth 1 -name 'UnityEngine*.dll' \
        | while read -r dll; do echo "-r:\"$(cygpath -w "$dll")\""; done
    find "$SOURCES" -name '*.cs' \
        | while read -r src; do echo "\"$(cygpath -w "$src")\""; done
} > "$RSP"

echo "checking $(grep -c '^\"' "$RSP") source files against $(grep -c '^-r:' "$RSP") references"

dotnet "$(cygpath -w "$UNITY/Data/DotNetSdkRoslyn/csc.dll")" "@$(cygpath -w "$RSP")" 2>&1 \
    | grep 'error CS' > /c/Temp/brink_syntax_err.txt

COUNT=$(wc -l < /c/Temp/brink_syntax_err.txt)
if [ "$COUNT" -eq 0 ]; then
    echo "OK — runtime code compiles."
    exit 0
fi

echo "$COUNT compile error(s):"
cat /c/Temp/brink_syntax_err.txt
exit 1
