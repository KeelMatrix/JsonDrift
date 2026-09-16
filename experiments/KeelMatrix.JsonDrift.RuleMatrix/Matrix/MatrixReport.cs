using System.Text.Json;
using KeelMatrix.JsonDrift.RuleMatrix.Rules;

namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>
/// Runs every rule check and prints a deterministic report.
/// </summary>
internal static class MatrixReport
{
    public static IReadOnlyList<CheckOutcome> Collect()
    {
        var checks = new List<CheckOutcome>();
        checks.AddRange(PropertyRules.Run());
        checks.AddRange(RequirednessRules.Run());
        checks.AddRange(NullabilityRules.Run());
        checks.AddRange(ShapeRules.Run());
        checks.AddRange(EnumRules.Run());
        checks.AddRange(IgnoreRules.Run());
        checks.AddRange(PolymorphismRules.Run());
        checks.AddRange(ExtensionDataRules.Run());
        checks.AddRange(BindingRules.Run());
        checks.AddRange(GenerationRules.Run());
        checks.AddRange(UnsupportedRules.Run());
        checks.AddRange(AdversarialRules.Run());
        checks.AddRange(HarnessRules.Run());
        checks.AddRange(DeterminismRules.Run());
        checks.AddRange(MatrixSummary.Run(checks));
        checks.AddRange(InventoryRules.Run(checks));
        return checks;
    }

    public static int Run(TextWriter writer, bool verbose)
    {
        IReadOnlyList<CheckOutcome> checks = Collect();

        writer.WriteLine("KeelMatrix JsonDrift compatibility rule matrix");
        writer.WriteLine($"runtime: .NET {Environment.Version}");
        writer.WriteLine($"system.text.json: {typeof(JsonSerializer).Assembly.GetName().Version}");
        writer.WriteLine($"checks: {checks.Count}");
        writer.WriteLine();

        foreach (CheckOutcome check in checks)
        {
            writer.WriteLine($"[{(check.Passed ? "PASS" : "FAIL")}] {check.Id}");

            if (verbose)
            {
                writer.WriteLine($"       change  : {check.Change}");
            }

            writer.WriteLine($"       expected: {check.Expected}");
            writer.WriteLine($"       measured: {check.Measured}");
            writer.WriteLine($"       observed: {check.Observation}");
        }

        int passed = checks.Count(static check => check.Passed);
        writer.WriteLine();
        writer.WriteLine($"result: {passed}/{checks.Count} checks agree with the recorded classification");

        return passed == checks.Count ? 0 : 1;
    }
}
