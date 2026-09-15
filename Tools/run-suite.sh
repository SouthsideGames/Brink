#!/usr/bin/env bash
# Run the full EditMode suite as three partitioned Unity invocations.
# Usage: bash Tools/run-suite.sh   (Unity editor must be CLOSED)

set -u
UNITY="/c/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Unity.exe"
PROJECT='D:\Southside Games\Brink\Brink'
OUT=/c/Temp
HERE="$(cd "$(dirname "$0")" && pwd)"

PART_A='Brink.Tests.SanctionReliefTests|Brink.Tests.ActionLadderTests|Brink.Tests.AIFiscalDisciplineTests|Brink.Tests.ProductionProvenanceTests|Brink.Tests.StrategyTests|Brink.Tests.OperationPlanningTests|Brink.Tests.StrategicBoardTests|Brink.Tests.ActionFinderTests|Brink.Tests.CommandCenterTests|Brink.Tests.CommandCenterViewTests|Brink.Tests.CabinetMeetingTests|Brink.Tests.CabinetDynamicsTests|Brink.Tests.CabinetChoiceReadingTests|Brink.Tests.StrategicSurpriseTests|Brink.Tests.HistoricalIdentityTests|Brink.Tests.StrategicEraTests|Brink.Tests.PrecedentTests|Brink.Tests.StrategicReversalTests|Brink.Tests.CredibilityMemoryTests|Brink.Tests.RecoveryHistoryTests|Brink.Tests.AllianceCascadeTests|Brink.Tests.MultilateralAllianceTests|Brink.Tests.CorruptionTests|Brink.Tests.RecognitionAndMediationTests|Brink.Tests.FiscalTests|Brink.Tests.IntelProductTests|Brink.Tests.MandateTests|Brink.Tests.StandingDirectiveTests|Brink.Tests.CareerRecordTests|Brink.Tests.ActionIndexTests|Brink.Tests.DossierTests|Brink.Tests.HoldTests|Brink.Tests.HistoryCatalogTests|Brink.Tests.OppositionTests|Brink.Tests.CouncilTests|Brink.Tests.AttentionSystemTests|Brink.Tests.ForceInventoryTests|Brink.Tests.GovernmentSystemTests|Brink.Tests.MapAndLayoutTests|Brink.Tests.OperationCatalogTests|Brink.Tests.MonthlyDebriefTests|Brink.Tests.MonthlyDebriefPresentationTests|Brink.Tests.MonthlyDebriefViewModelTests'
PART_B='Brink.Tests.CausalityTests|Brink.Tests.InsurgencyTests|Brink.Tests.BlocTests|Brink.Tests.DisplacementTests|Brink.Tests.VerticalSliceValidationTests|Brink.Tests.WorldInvariantTests|Brink.Tests.AISystemTests|Brink.Tests.BugRegressionTests|Brink.Tests.PartialSystemsTests|Brink.Tests.WorldHeatTests'
PART_C='Brink.Tests.SettlementFogTests|Brink.Tests.StabilityRepairTests|Brink.Tests.AIInformationTests|Brink.Tests.DiplomacySecondActTests|Brink.Tests.AccessionTests|Brink.Tests.AIStrategyTests|Brink.Tests.AIDomesticTests|Brink.Tests.AgentSystemTests|Brink.Tests.AllianceSystemTests|Brink.Tests.AsciiChartTests|Brink.Tests.AsciiMapModeTests|Brink.Tests.BreakpointTests|Brink.Tests.AsciiWorldMapTests|Brink.Tests.AssessmentSystemTests|Brink.Tests.AudioSystemTests|Brink.Tests.CabinetAdviceTests|Brink.Tests.CabinetLifecycleTests|Brink.Tests.CabinetSystemTests|Brink.Tests.ChronicleTests|Brink.Tests.CommunicationTests|Brink.Tests.CrisisChainTests|Brink.Tests.CrisisEffectTests|Brink.Tests.CrisisSystemTests|Brink.Tests.NotificationTests|Brink.Tests.DiplomacySystemTests|Brink.Tests.RealWorldRosterTests|Brink.Tests.EconomySystemTests|Brink.Tests.MarketChartTests|Brink.Tests.EndgameSystemTests|Brink.Tests.ExerciseSystemTests|Brink.Tests.FoodSecurityTests|Brink.Tests.EventCatalogTests|Brink.Tests.FactionTests|Brink.Tests.ForeignCabinetTests|Brink.Tests.ForeignCrisisTests|Brink.Tests.GeographySystemTests|Brink.Tests.GameDateTests|Brink.Tests.GovernmentVerbTests|Brink.Tests.IndustrialSystemTests|Brink.Tests.IntelligenceSystemTests|Brink.Tests.MilitarySystemTests|Brink.Tests.MilitaryAdviceTests|Brink.Tests.MilitaryVerbsTests|Brink.Tests.OperationVerbTests|Brink.Tests.PeaceSystemTests|Brink.Tests.PipelineWiringTests|Brink.Tests.ProgressionSystemTests|Brink.Tests.ReadabilityTests|Brink.Tests.RegimeSystemTests|Brink.Tests.ReportingSystemTests|Brink.Tests.SaveMigrationTests|Brink.Tests.SaveSystemTests|Brink.Tests.StrategyAndAuthorityTests|Brink.Tests.TechnologySystemTests|Brink.Tests.TelemetryTests|Brink.Tests.TerritorySystemTests|Brink.Tests.OccupationExitTests|Brink.Tests.TextPolicyTests|Brink.Tests.TouchTargetTests|Brink.Tests.TradeAndConquestTests|Brink.Tests.TreatyNegotiationTests|Brink.Tests.TurnManagerTests|Brink.Tests.TutorialSystemTests|Brink.Tests.VeterancyTests|Brink.Tests.WorldSizeTests|Brink.Tests.WorldStructureTests'

verify_coverage() {
    local missing=0
    for f in "$(dirname "$HERE")"/Brink/Assets/Tests/EditMode/*.cs; do
        while IFS= read -r cls; do
            [ "$cls" = "TestProgressLogger" ] && continue
            if ! echo "$PART_A|$PART_B|$PART_C" | grep -q "Brink.Tests.$cls"; then
                echo "PARTITION GAP: Brink.Tests.$cls is in no partition — it would never run."
                missing=1
            fi
        done < <(grep -o 'public class [A-Za-z0-9_]*' "$f" | awk '{print $3}')
    done
    return $missing
}

run_part() {
    local name=$1 filter=$2
    rm -f "$OUT/brink_suite_$name.xml"
    echo "--- partition $name ---"
    "$UNITY" -batchmode -projectPath "$PROJECT" -runTests -testPlatform EditMode \
        -testFilter "$filter" \
        -testResults "C:\\Temp\\brink_suite_$name.xml" \
        -logFile "C:\\Temp\\brink_suite_${name}_log.txt"
    if [ ! -f "$OUT/brink_suite_$name.xml" ]; then
        echo "PARTITION $name PRODUCED NO RESULTS — check C:\\Temp\\brink_suite_${name}_log.txt"
        echo "(last test started: $(grep '\[TEST\]' "$OUT/brink_suite_${name}_log.txt" 2>/dev/null | tail -1))"
        return 1
    fi
    bash "$HERE/extract-failures.sh" "$OUT/brink_suite_$name.xml"
}

verify_coverage || exit 1
run_part A "$PART_A" || exit 1
run_part B "$PART_B" || exit 1
run_part C "$PART_C" || exit 1

echo "=== COMBINED ==="
total=0; passed=0; failed=0
for name in A B C; do
    line=$(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$OUT/brink_suite_$name.xml" | head -1)
    t=$(echo "$line" | grep -o 'total="[0-9]*"' | grep -o '[0-9]*')
    p=$(echo "$line" | grep -o 'passed="[0-9]*"' | grep -o '[0-9]*')
    f=$(echo "$line" | grep -o 'failed="[0-9]*"' | grep -o '[0-9]*')
    total=$((total + t)); passed=$((passed + p)); failed=$((failed + f))
done
echo "total=$total passed=$passed failed=$failed"
[ "$failed" -eq 0 ] && [ "$total" -gt 0 ]
