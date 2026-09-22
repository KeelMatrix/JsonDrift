using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// The coverage gate. The implemented discovery sources are the sources the traversal recorded at runtime,
/// not a hand-maintained list, so a metadata path the walk never visits is visible as a missing entry. The
/// same gate binds the classification rules, the recorded node kinds, and the allowlists of the
/// deny-by-default classifier to the tables and lists published in <c>docs/compatibility-rules.md</c>.
/// </summary>
internal static class InventoryRules
{
    public static IEnumerable<CheckOutcome> Run(IReadOnlyList<CheckOutcome> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        string[]? lines = DocumentationTables.ReadLines();

        if (lines is null)
        {
            yield return Check.Assert(
                "D06.discovery-paths.inventory",
                "Metadata discovery inventory",
                "the path inventory is compared with the discovery sources the traversal recorded",
                "inventory=readable",
                "inventory=missing",
                $"searched from {AppContext.BaseDirectory} for docs/compatibility-rules.md",
                false);

            yield break;
        }

        foreach (CheckOutcome check in DiscoveryPathChecks(checks, lines))
        {
            yield return check;
        }

        foreach (CheckOutcome check in ClassificationRuleChecks(lines))
        {
            yield return check;
        }

        yield return ComparisonFactRuleCheck(checks, lines);
        yield return ComparisonFactInteractionCheck(checks, lines);

        foreach (CheckOutcome check in AllowlistChecks(lines))
        {
            yield return check;
        }

        yield return OptionAllowlistCheck(lines);
        yield return AttributeAllowlistCheck(lines);
        yield return AttributeDeclarationCoverageCheck(lines);
        yield return ConverterConfigurationAllowlistCheck(lines);
    }

    private static CheckOutcome ComparisonFactRuleCheck(IReadOnlyList<CheckOutcome> checks, string[] lines)
    {
        string[][] documented = DocumentationTables.ReadTable(lines, DocumentationTables.FactRuleHeading)
            .Where(static cells => cells.Length >= 6 && !string.Equals(Clean(cells[0]), "Fact kind", StringComparison.Ordinal))
            .ToArray();
        ComparisonFactRule[] catalogue = ComparisonFactRules.Entries.ToArray();
        IReadOnlyList<string> coverageErrors = ComparisonFactRules.ValidateCoverage();
        string[] duplicateRows = documented
            .GroupBy(static cells => Clean(cells[0]), StringComparer.Ordinal)
            .Where(static group => group.Count() != 1)
            .Select(static group => group.Key)
            .ToArray();
        string[] missingRows = catalogue
            .Where(entry => !documented.Any(cells => string.Equals(Clean(cells[0]), entry.FactKind, StringComparison.Ordinal)))
            .Select(static entry => entry.FactKind)
            .ToArray();
        string[] unknownRows = documented
            .Where(cells => !catalogue.Any(entry => string.Equals(entry.FactKind, Clean(cells[0]), StringComparison.Ordinal)))
            .Select(static cells => Clean(cells[0]))
            .ToArray();
        string[] missingMatrixWitnesses = catalogue
            .Where(static entry => entry.Witness is not null)
            .SelectMany(static entry => entry.Witness!.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            .Where(static witness => witness.StartsWith("matrix:", StringComparison.Ordinal))
            .Select(static witness => witness["matrix:".Length..])
            .Where(id => !checks.Any(check => string.Equals(check.Id, id, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        string[] mismatches = catalogue
            .Select(entry =>
            {
                string[]? row = documented.FirstOrDefault(cells =>
                    string.Equals(Clean(cells[0]), entry.FactKind, StringComparison.Ordinal));
                if (row is null)
                {
                    return null;
                }

                string[] expected =
                {
                    entry.FactKind,
                    string.Join(", ", entry.Properties.Select(static property => property.Id)),
                    entry.ComparedRule ?? "Non-contract",
                    entry.Witness ?? "Not applicable",
                    entry.VerdictWhenRuleDoesNotApply,
                    entry.NonContractReason ?? "Not applicable",
                };
                string[] actual = row.Take(6).Select(Clean).ToArray();
                return expected.SequenceEqual(actual, StringComparer.Ordinal)
                    ? null
                    : entry.FactKind;
            })
            .Where(static value => value is not null)
            .Select(static value => value!)
            .ToArray();
        bool passed = coverageErrors.Count == 0 && duplicateRows.Length == 0 && missingRows.Length == 0 &&
            unknownRows.Length == 0 && mismatches.Length == 0 && missingMatrixWitnesses.Length == 0;

        return Check.Assert(
            "D06.comparison-facts.coverage",
            "Recorded-fact comparison coverage",
            "every fact populated by the metadata walk is assigned to a measured comparison rule or an explicit non-contract reason, and the rule table is documented exactly",
            "recorded=catalogued=documented, rule-gaps=Unsupported",
            passed ? "recorded=catalogued=documented, rule-gaps=Unsupported" : "recorded-fact coverage mismatch",
            $"properties={catalogue.SelectMany(static entry => entry.Properties).Count()}; factKinds={catalogue.Length}; " +
            $"coverageErrors=[{string.Join(", ", coverageErrors)}]; duplicateRows=[{string.Join(", ", duplicateRows)}]; " +
            $"missingRows=[{string.Join(", ", missingRows)}]; unknownRows=[{string.Join(", ", unknownRows)}]; " +
            $"mismatches=[{string.Join(", ", mismatches)}]; " +
            $"missingMatrixWitnesses=[{string.Join(", ", missingMatrixWitnesses)}]",
            passed);
    }

    private static CheckOutcome ComparisonFactInteractionCheck(
        IReadOnlyList<CheckOutcome> checks,
        string[] lines)
    {
        string[][] documented = DocumentationTables.ReadTable(lines, DocumentationTables.FactInteractionHeading)
            .Where(static cells => cells.Length >= 4 &&
                !string.Equals(Clean(cells[0]), "Family pair", StringComparison.Ordinal))
            .ToArray();
        ContractFactInteraction[] catalogue = ContractFactInteractions.Entries.ToArray();
        IReadOnlyList<string> coverageErrors = ContractFactInteractions.ValidateCoverage();
        string[] duplicateRows = documented
            .GroupBy(static cells => Clean(cells[0]), StringComparer.Ordinal)
            .Where(static group => group.Count() != 1)
            .Select(static group => group.Key)
            .ToArray();
        string[] missingRows = catalogue
            .Where(entry => !documented.Any(cells =>
                string.Equals(Clean(cells[0]), entry.Id, StringComparison.Ordinal)))
            .Select(static entry => entry.Id)
            .ToArray();
        string[] unknownRows = documented
            .Where(cells => !catalogue.Any(entry =>
                string.Equals(entry.Id, Clean(cells[0]), StringComparison.Ordinal)))
            .Select(static cells => Clean(cells[0]))
            .ToArray();
        string[] missingMatrixWitnesses = catalogue
            .Where(static entry => entry.Witness is not null &&
                entry.Witness.StartsWith("matrix:", StringComparison.Ordinal))
            .Select(static entry => entry.Witness!["matrix:".Length..])
            .Where(id => !checks.Any(check => string.Equals(check.Id, id, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        string[] mismatches = catalogue
            .Select(entry =>
            {
                string[]? row = documented.FirstOrDefault(cells =>
                    string.Equals(Clean(cells[0]), entry.Id, StringComparison.Ordinal));
                if (row is null)
                {
                    return null;
                }

                string[] expected =
                {
                    entry.Id,
                    entry.Rule is null ? "Cannot interact" : "Interacts",
                    entry.Rule ?? entry.NonInteractionReason!,
                    entry.Witness ?? "Not applicable",
                };
                string[] actual = row.Take(4).Select(Clean).ToArray();
                return expected.SequenceEqual(actual, StringComparer.Ordinal) ? null : entry.Id;
            })
            .Where(static value => value is not null)
            .Select(static value => value!)
            .ToArray();
        bool passed = coverageErrors.Count == 0 && duplicateRows.Length == 0 &&
            missingRows.Length == 0 && unknownRows.Length == 0 && mismatches.Length == 0 &&
            missingMatrixWitnesses.Length == 0;

        return Check.Assert(
            "D06.comparison-fact-interactions.coverage",
            "Recorded-fact interaction coverage",
            "every unordered pair of contract-fact families has a witnessed comparison rule or an explicit non-interaction reason, and the interaction table is documented exactly",
            "pairs=catalogued=documented, unclassified=0",
            passed ? "pairs=catalogued=documented, unclassified=0" : "fact-family interaction coverage mismatch",
            $"families={ContractFactInteractions.Families.Count}; pairs={catalogue.Length}; " +
            $"coverageErrors=[{string.Join(", ", coverageErrors)}]; duplicateRows=[{string.Join(", ", duplicateRows)}]; " +
            $"missingRows=[{string.Join(", ", missingRows)}]; unknownRows=[{string.Join(", ", unknownRows)}]; " +
            $"mismatches=[{string.Join(", ", mismatches)}]; missingMatrixWitnesses=[{string.Join(", ", missingMatrixWitnesses)}]",
            passed);
    }

    /// <summary>
    /// The serializer option allowlist is compared with the documented list and with the option values the
    /// executed checks were actually accepted under. The comparison runs in both directions: a value the
    /// allowlist names but no executed check was accepted under fails, so the allowlist cannot claim more than
    /// the committed checks prove, and a value an executed check was accepted under that the allowlist does not
    /// name fails as well.
    /// </summary>
    private static CheckOutcome OptionAllowlistCheck(string[] lines)
    {
        string[] documented = DocumentationTables.ReadAllowlist(lines, AllowlistKind.OptionValues)?.ToArray()
            ?? Array.Empty<string>();
        string[] allowlisted = ContractAllowlists.OptionValueNames.ToArray();
        string[] accepted = TraversalInventory.Ledger.AcceptedOptionValues()
            .Select(static value => value.Display)
            .ToArray();

        string[] codeNotDocumented = allowlisted
            .Where(name => !documented.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] documentedNotInCode = documented
            .Where(name => !allowlisted.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] allowlistedButNotAccepted = allowlisted
            .Where(name => !accepted.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] acceptedButNotAllowlisted = accepted
            .Where(name => !allowlisted.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] familiesWithoutAcceptedValue = SerializerOptionFacts.All
            .Where(kind => !accepted.Any(name =>
                name.StartsWith($"{SerializerOptionFacts.Id(kind)}=", StringComparison.Ordinal)))
            .Select(SerializerOptionFacts.Id)
            .ToArray();

        bool passed =
            codeNotDocumented.Length == 0 &&
            documentedNotInCode.Length == 0 &&
            allowlistedButNotAccepted.Length == 0 &&
            acceptedButNotAllowlisted.Length == 0 &&
            familiesWithoutAcceptedValue.Length == 0;

        return Check.Assert(
            "D06.allowlist.option-values",
            "Deny-by-default allowlist",
            "the serializer option allowlist is compared with the documented list and with the option values the executed checks were accepted under",
            "code=documented=accepted, families=recorded",
            passed
                ? "code=documented=accepted"
                : $"code={allowlisted.Length}, documented={documented.Length}, accepted={accepted.Length}",
            $"codeNotDocumented=[{string.Join(", ", codeNotDocumented)}]; documentedNotInCode=[{string.Join(", ", documentedNotInCode)}]; " +
            $"allowlistedButNotAccepted=[{string.Join(", ", allowlistedButNotAccepted)}]; " +
            $"acceptedButNotAllowlisted=[{string.Join(", ", acceptedButNotAllowlisted)}]; " +
            $"familiesWithoutAcceptedValue=[{string.Join(", ", familiesWithoutAcceptedValue)}]",
            passed);
    }

    private static CheckOutcome AttributeAllowlistCheck(string[] lines)
    {
        string[] documented = DocumentationTables.ReadAllowlist(lines, AllowlistKind.AttributeValues)?.ToArray()
            ?? Array.Empty<string>();
        string[] allowlisted = ContractAllowlists.AttributeValueNames.ToArray();
        string[] accepted = TraversalInventory.Ledger.AcceptedAttributes().ToArray();

        string[] codeNotDocumented = allowlisted
            .Where(name => !documented.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] documentedNotInCode = documented
            .Where(name => !allowlisted.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] allowlistedButNotAccepted = allowlisted
            .Where(name => !accepted.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] acceptedButNotAllowlisted = accepted
            .Where(name => !allowlisted.Contains(name, StringComparer.Ordinal))
            .ToArray();

        bool passed =
            codeNotDocumented.Length == 0 &&
            documentedNotInCode.Length == 0 &&
            allowlistedButNotAccepted.Length == 0 &&
            acceptedButNotAllowlisted.Length == 0;

        return Check.Assert(
            "D06.allowlist.attribute-values",
            "Deny-by-default allowlist",
            "the declared JSON attribute allowlist is compared with the documented list and with the attribute facts the executed checks accepted under",
            "code=documented=accepted",
            passed ? "code=documented=accepted" : $"code={allowlisted.Length}, documented={documented.Length}, accepted={accepted.Length}",
            $"codeNotDocumented=[{string.Join(", ", codeNotDocumented)}]; documentedNotInCode=[{string.Join(", ", documentedNotInCode)}]; " +
            $"allowlistedButNotAccepted=[{string.Join(", ", allowlistedButNotAccepted)}]; " +
            $"acceptedButNotAllowlisted=[{string.Join(", ", acceptedButNotAllowlisted)}]",
            passed);
    }

    /// <summary>
    /// Binds the declared-attribute walk to every attribute type in the loaded System.Text.Json assembly.
    /// The runtime enumeration is the boundary: each type must have an inventoried measured fact or an
    /// explicit documented exclusion. A new framework declaration therefore fails this gate until a probe or
    /// an evidence-backed exclusion is added.
    /// </summary>
    private static CheckOutcome AttributeDeclarationCoverageCheck(string[] lines)
    {
        var documented = DocumentationTables.ReadTable(lines, DocumentationTables.AttributeCoverageHeading)
            .Where(static cells => cells.Length >= 3 && !string.Equals(cells[1].Trim(), "Status", StringComparison.OrdinalIgnoreCase))
            .Select(static cells => new DeclarationCoverageRow(
                Clean(cells[0]),
                Clean(cells[1]),
                Clean(cells[2])))
            .ToArray();

        string[] runtime = DeclaredAttributeFacts.RuntimeDeclarationTypes
            .Select(TypeShapes.TypeName)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        string[] inventoried = DeclaredAttributeFacts.InventoriedValues
            .Select(static fact => fact.AttributeType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        string[] excluded = DeclaredAttributeFacts.ExplicitExclusions
            .Select(static exclusion => TypeShapes.TypeName(exclusion.Type))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        string[] covered = inventoried.Concat(excluded).Distinct(StringComparer.Ordinal).ToArray();

        string[] runtimeWithoutCoverage = runtime.Where(name => !covered.Contains(name, StringComparer.Ordinal)).ToArray();
        string[] coverageOutsideRuntime = covered.Where(name => !runtime.Contains(name, StringComparer.Ordinal)).ToArray();
        string[] duplicateDocumentation = documented
            .GroupBy(static row => row.TypeName, StringComparer.Ordinal)
            .Where(static group => group.Count() != 1)
            .Select(static group => group.Key)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        string[] runtimeWithoutDocumentation = runtime
            .Where(name => documented.Count(row => string.Equals(row.TypeName, name, StringComparison.Ordinal)) != 1)
            .ToArray();
        string[] documentationOutsideRuntime = documented
            .Where(row => !runtime.Contains(row.TypeName, StringComparer.Ordinal))
            .Select(static row => row.TypeName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        string[] statusMismatches = runtime
            .Where(name => documented.FirstOrDefault(row => string.Equals(row.TypeName, name, StringComparison.Ordinal)) is DeclarationCoverageRow row &&
                !string.Equals(row.Status, inventoried.Contains(name, StringComparer.Ordinal) ? "Inventoried" : "Excluded", StringComparison.Ordinal))
            .ToArray();
        string[] missingEvidence = documented
            .Where(static row => row.Status.Length == 0 || row.Evidence.Length == 0)
            .Select(static row => row.TypeName)
            .ToArray();
        string[] exclusionReasonMismatches = DeclaredAttributeFacts.ExplicitExclusions
            .Select(exclusion =>
            {
                string typeName = TypeShapes.TypeName(exclusion.Type);
                DeclarationCoverageRow? row = documented.FirstOrDefault(candidate =>
                    string.Equals(candidate.TypeName, typeName, StringComparison.Ordinal));
                return row is not null &&
                    string.Equals(row.Status, "Excluded", StringComparison.Ordinal) &&
                    string.Equals(row.Evidence, exclusion.Reason, StringComparison.Ordinal)
                    ? null
                    : typeName;
            })
            .Where(static name => name is not null)
            .Select(static name => name!)
            .ToArray();
        string[] invalidExclusions = DeclaredAttributeFacts.ExplicitExclusions
            .Where(static exclusion => !exclusion.Type.IsAbstract)
            .Select(static exclusion => TypeShapes.TypeName(exclusion.Type))
            .ToArray();

        bool passed =
            runtimeWithoutCoverage.Length == 0 &&
            coverageOutsideRuntime.Length == 0 &&
            duplicateDocumentation.Length == 0 &&
            runtimeWithoutDocumentation.Length == 0 &&
            documentationOutsideRuntime.Length == 0 &&
            statusMismatches.Length == 0 &&
            missingEvidence.Length == 0 &&
            exclusionReasonMismatches.Length == 0 &&
            invalidExclusions.Length == 0;

        return Check.Assert(
            "D06.allowlist.attribute-declaration-coverage",
            "Runtime declaration coverage",
            "every System.Text.Json.Serialization attribute type in the loaded System.Text.Json assembly is inventoried with measured facts or explicitly excluded with a measured reason",
            "runtime=covered=documented, exclusions=measured",
            passed ? "runtime=covered=documented" : $"runtime={runtime.Length}, inventoried={inventoried.Length}, excluded={excluded.Length}, documented={documented.Length}",
            $"runtimeWithoutCoverage=[{string.Join(", ", runtimeWithoutCoverage)}]; coverageOutsideRuntime=[{string.Join(", ", coverageOutsideRuntime)}]; " +
            $"duplicateDocumentation=[{string.Join(", ", duplicateDocumentation)}]; runtimeWithoutDocumentation=[{string.Join(", ", runtimeWithoutDocumentation)}]; " +
            $"documentationOutsideRuntime=[{string.Join(", ", documentationOutsideRuntime)}]; statusMismatches=[{string.Join(", ", statusMismatches)}]; " +
            $"missingEvidence=[{string.Join(", ", missingEvidence)}]; exclusionReasonMismatches=[{string.Join(", ", exclusionReasonMismatches)}]; " +
            $"invalidExclusions=[{string.Join(", ", invalidExclusions)}]; runtime=[{string.Join(", ", runtime)}]; inventoried=[{string.Join(", ", inventoried)}]; excluded=[{string.Join(", ", excluded)}]",
            passed);
    }

    private static string Clean(string value) => value.Trim().Replace("`", string.Empty, StringComparison.Ordinal).Trim();

    private sealed record DeclarationCoverageRow(string TypeName, string Status, string Evidence);

    private static CheckOutcome ConverterConfigurationAllowlistCheck(string[] lines)
    {
        string[] documented = DocumentationTables.ReadAllowlist(lines, AllowlistKind.ConverterConfigurations)?.ToArray()
            ?? Array.Empty<string>();
        string[] allowlisted = ContractAllowlists.ConverterConfigurationNames.ToArray();
        string[] accepted = TraversalInventory.Ledger.AcceptedConverterConfigurations()
            .Select(static value => value.Display)
            .ToArray();

        string[] codeNotDocumented = allowlisted
            .Where(name => !documented.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] documentedNotInCode = documented
            .Where(name => !allowlisted.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] allowlistedButNotAccepted = allowlisted
            .Where(name => !accepted.Contains(name, StringComparer.Ordinal))
            .ToArray();
        string[] acceptedButNotAllowlisted = accepted
            .Where(name => !allowlisted.Contains(name, StringComparer.Ordinal))
            .ToArray();

        bool passed =
            codeNotDocumented.Length == 0 &&
            documentedNotInCode.Length == 0 &&
            allowlistedButNotAccepted.Length == 0 &&
            acceptedButNotAllowlisted.Length == 0;

        return Check.Assert(
            "D06.allowlist.converter-configurations",
            "Deny-by-default allowlist",
            "the enum converter-configuration allowlist is compared with the documented list and with the configurations the executed checks accepted under",
            "code=documented=accepted",
            passed ? "code=documented=accepted" : $"code={allowlisted.Length}, documented={documented.Length}, accepted={accepted.Length}",
            $"codeNotDocumented=[{string.Join(", ", codeNotDocumented)}]; documentedNotInCode=[{string.Join(", ", documentedNotInCode)}]; " +
            $"allowlistedButNotAccepted=[{string.Join(", ", allowlistedButNotAccepted)}]; " +
            $"acceptedButNotAllowlisted=[{string.Join(", ", acceptedButNotAllowlisted)}]",
            passed);
    }

    /// <summary>
    /// The implemented discovery sources are read from the traversal inventory and compared with the path
    /// inventory table and the executed per-path checks. Every declared source has to be visited by an
    /// executed contract, so a source that is added without coverage fails here.
    /// </summary>
    private static IEnumerable<CheckOutcome> DiscoveryPathChecks(IReadOnlyList<CheckOutcome> checks, string[] lines)
    {
        var documented = new List<KeyValuePair<string, string>>();

        foreach (string[] cells in DocumentationTables.ReadTable(lines, DocumentationTables.InventoryHeading))
        {
            if (cells.Length >= 2 &&
                MetadataDiscoverySources.TryParseInventoryEntry($"|{cells[0]}|{cells[1]}|", out string? source, out string? checkId))
            {
                documented.Add(new KeyValuePair<string, string>(source, checkId));
            }
        }

        string[] implemented = MetadataDiscoverySources.Implemented.ToArray();
        string[] declared = MetadataSourceRules.All.Select(MetadataSourceRules.Id).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        IReadOnlySet<string> observed = MetadataDiscoverySources.ObservedFromChecks(checks);
        IReadOnlyList<KeyValuePair<string, string>> bindings = MetadataDiscoverySources.InventoryBindings(checks);

        string[] notVisited = declared.Where(source => !implemented.Contains(source, StringComparer.Ordinal)).ToArray();
        string[] notCoveredByAnyCheck = implemented.Where(source => !observed.Contains(source)).ToArray();
        string[] notInRegistry = observed
            .Where(source => !declared.Contains(source, StringComparer.Ordinal))
            .OrderBy(static source => source, StringComparer.Ordinal)
            .ToArray();
        string[] missingFromInventory = implemented
            .Where(source => !documented.Any(entry => string.Equals(entry.Key, source, StringComparison.Ordinal)))
            .ToArray();
        string[] documentedWithoutImplementation = documented
            .Where(entry => !declared.Contains(entry.Key, StringComparer.Ordinal))
            .Select(static entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static source => source, StringComparer.Ordinal)
            .ToArray();
        string[] checksWithoutBinding = implemented
            .Where(source => !bindings.Any(binding => string.Equals(binding.Key, source, StringComparison.Ordinal)))
            .ToArray();
        string[] rowMismatches = InventoryMismatches(bindings, documented, checks);
        string[] missingCheckIds = bindings
            .Where(binding => !checks.Any(check => string.Equals(check.Id, binding.Value, StringComparison.Ordinal)))
            .Select(static binding => binding.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        bool passed =
            notVisited.Length == 0 &&
            notCoveredByAnyCheck.Length == 0 &&
            notInRegistry.Length == 0 &&
            missingFromInventory.Length == 0 &&
            documentedWithoutImplementation.Length == 0 &&
            checksWithoutBinding.Length == 0 &&
            rowMismatches.Length == 0 &&
            missingCheckIds.Length == 0;

        yield return Check.Assert(
            "D06.discovery-paths.inventory",
            "Metadata discovery inventory",
            "the discovery sources the traversal recorded, the path inventory, and the executed per-path checks are compared",
            "visited=documented=covered",
            passed ? "visited=documented=covered" : $"visited={implemented.Length}, documented={documented.Count}, covered={bindings.Count}",
            $"visited=[{string.Join(", ", implemented)}]; declaredButNotVisited=[{string.Join(", ", notVisited)}]; " +
            $"notCoveredByAnyCheck=[{string.Join(", ", notCoveredByAnyCheck)}]; notInRegistry=[{string.Join(", ", notInRegistry)}]; " +
            $"notDocumented=[{string.Join(", ", missingFromInventory)}]; documentedWithoutDeclaration=[{string.Join(", ", documentedWithoutImplementation)}]; " +
            $"checksWithoutBinding=[{string.Join(", ", checksWithoutBinding)}]; rowMismatches=[{string.Join(", ", rowMismatches)}]; " +
            $"missingCheckIds=[{string.Join(", ", missingCheckIds)}]",
            passed);
    }

    /// <summary>
    /// The classification rule catalogue is compared with the documented rule table, every rule the run
    /// applied has to come from that catalogue, and every rule reachable through a visited source or a
    /// recorded node kind has to be documented as well.
    /// </summary>
    private static IEnumerable<CheckOutcome> ClassificationRuleChecks(string[] lines)
    {
        var documented = new List<ClassificationRule>();

        foreach (string[] cells in DocumentationTables.ReadTable(lines, DocumentationTables.RuleTableHeading))
        {
            if (DocumentationTables.TryParseRuleRow(cells, out string ruleId, out bool supported))
            {
                documented.Add(new ClassificationRule(ruleId, supported, cells.Length > 2 ? cells[2] : string.Empty));
            }
        }

        ClassificationRule[] catalogue = RuleCatalog.Entries.ToArray();
        string[] undocumented = catalogue
            .Where(rule => !documented.Any(entry => string.Equals(entry.Id, rule.Id, StringComparison.Ordinal)))
            .Select(static rule => rule.Id)
            .ToArray();
        string[] uncatalogued = documented
            .Where(rule => !catalogue.Any(entry => string.Equals(entry.Id, rule.Id, StringComparison.Ordinal)))
            .Select(static rule => rule.Id)
            .ToArray();
        string[] verdictMismatches = documented
            .Where(rule => catalogue.Any(entry =>
                string.Equals(entry.Id, rule.Id, StringComparison.Ordinal) && entry.Supported != rule.Supported))
            .Select(static rule => rule.Id)
            .ToArray();

        bool documentedPassed =
            undocumented.Length == 0 && uncatalogued.Length == 0 && verdictMismatches.Length == 0;

        yield return Check.Assert(
            "D06.classification-rules.documented",
            "Classification rule catalogue",
            "the classification rules in code are compared with the documented rule table",
            "code=documented",
            documentedPassed ? "code=documented" : $"code={catalogue.Length}, documented={documented.Count}",
            $"codeNotDocumented=[{string.Join(", ", undocumented)}]; documentedNotInCode=[{string.Join(", ", uncatalogued)}]; " +
            $"verdictMismatches=[{string.Join(", ", verdictMismatches)}]; rules={catalogue.Length}; documented={documented.Count}",
            documentedPassed);

        string[] applied = TraversalInventory.Ledger.AppliedRules().ToArray();
        string[] appliedNotCatalogued = applied.Where(rule => !RuleCatalog.Contains(rule)).ToArray();
        string[] supportedNotAllowlisted = applied.Where(rule => !RuleCatalog.IsSupportedRule(rule) && rule.StartsWith("supported.", StringComparison.Ordinal)).ToArray();
        string[] sourceRulesNotCatalogued = MetadataSourceRules.All
            .SelectMany(MetadataSourceRules.ClassificationRules)
            .Where(rule => !RuleCatalog.Contains(rule))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static rule => rule, StringComparer.Ordinal)
            .ToArray();
        string[] visitedNodeKinds = TraversalInventory.Ledger.VisitedNodeKinds()
            .Select(RecordedKindRules.Name)
            .ToArray();
        string[] nodeKindRulesNotCatalogued = RecordedKindRules.All
            .SelectMany(RecordedKindRules.ClassificationRules)
            .Where(rule => !RuleCatalog.Contains(rule))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static rule => rule, StringComparer.Ordinal)
            .ToArray();
        string[] unvisitedNodeKinds = RecordedKindRules.All
            .Where(kind => !TraversalInventory.Ledger.VisitedNodeKinds().Contains(kind))
            .Select(RecordedKindRules.Name)
            .ToArray();
        string[] unvisitedSources = MetadataSourceRules.All
            .Where(source => !TraversalInventory.Ledger.VisitedSources().Contains(source))
            .Select(MetadataSourceRules.Id)
            .ToArray();

        bool appliedPassed =
            appliedNotCatalogued.Length == 0 &&
            supportedNotAllowlisted.Length == 0 &&
            sourceRulesNotCatalogued.Length == 0 &&
            nodeKindRulesNotCatalogued.Length == 0 &&
            unvisitedNodeKinds.Length == 0 &&
            unvisitedSources.Length == 0;

        yield return Check.Assert(
            "D06.classification-rules.observed",
            "Classification rule catalogue",
            "every rule the traversal and the classifier applied is a documented rule of the catalogue",
            "applied=documented, nodeKinds=all-visited, sources=all-visited",
            appliedPassed ? "applied=documented" : $"appliedNotCatalogued={appliedNotCatalogued.Length}",
            $"applied=[{string.Join(", ", applied)}]; appliedNotCatalogued=[{string.Join(", ", appliedNotCatalogued)}]; " +
            $"supportedNotAllowlisted=[{string.Join(", ", supportedNotAllowlisted)}]; " +
            $"sourceRulesNotCatalogued=[{string.Join(", ", sourceRulesNotCatalogued)}]; " +
            $"nodeKindRulesNotCatalogued=[{string.Join(", ", nodeKindRulesNotCatalogued)}]; " +
            $"visitedNodeKinds=[{string.Join(", ", visitedNodeKinds)}]; unvisitedNodeKinds=[{string.Join(", ", unvisitedNodeKinds)}]; " +
            $"unvisitedSources=[{string.Join(", ", unvisitedSources)}]",
            appliedPassed);
    }

    /// <summary>
    /// The allowlists of the deny-by-default classifier are compared with the documented lists, so a
    /// supported verdict can never rest on a construct that is not published, and the framework is asked to
    /// confirm that every allowlisted scalar type has a usable framework converter.
    /// </summary>
    private static IEnumerable<CheckOutcome> AllowlistChecks(string[] lines)
    {
        string[] documentedScalars = DocumentationTables.ReadAllowlist(lines, AllowlistKind.ScalarTypes)?.ToArray() ?? Array.Empty<string>();
        string[] documentedConverters = DocumentationTables.ReadAllowlist(lines, AllowlistKind.Converters)?.ToArray() ?? Array.Empty<string>();
        string[] documentedResolvers = DocumentationTables.ReadAllowlist(lines, AllowlistKind.Resolvers)?.ToArray() ?? Array.Empty<string>();

        string[] documented = documentedScalars.Concat(documentedConverters).Concat(documentedResolvers).ToArray();
        string[] coded = ContractAllowlists.ScalarTypeNames
            .Concat(ContractAllowlists.ConverterNames)
            .Concat(ContractAllowlists.ResolverNames)
            .ToArray();
        string[] notDocumented = coded.Where(name => !documented.Contains(name, StringComparer.Ordinal)).ToArray();
        string[] notInCode = documented.Where(name => !coded.Contains(name, StringComparer.Ordinal)).ToArray();
        bool documentedPassed = notDocumented.Length == 0 && notInCode.Length == 0;

        yield return Check.Assert(
            "D06.allowlist.documented",
            "Deny-by-default allowlist",
            "the scalar, converter, and resolver allowlists in code are compared with the documented lists",
            "code=documented",
            documentedPassed ? "code=documented" : $"code={coded.Length}, documented={documented.Length}",
            $"scalars={documentedScalars.Length}; converters={documentedConverters.Length}; resolvers={documentedResolvers.Length}; " +
            $"codeNotDocumented=[{string.Join(", ", notDocumented)}]; documentedNotInCode=[{string.Join(", ", notInCode)}]",
            documentedPassed);

        JsonSerializerOptions options = JsonContractOptions.Reflection();
        var failures = new List<string>();
        var verified = new List<string>();

        foreach (Type type in ContractAllowlists.ScalarTypes)
        {
            JsonConverter converter = options.GetConverter(type);
            bool frameworkConverter = ContractAllowlists.IsFrameworkAssembly(converter.GetType());
            bool placeholder = converter.GetType().Name.StartsWith("UnsupportedTypeConverter", StringComparison.Ordinal);
            bool scalarKind = options.GetTypeInfo(type).Kind == JsonTypeInfoKind.None;

            if (frameworkConverter && !placeholder && scalarKind)
            {
                verified.Add(TypeShapes.TypeName(type));
            }
            else
            {
                failures.Add(
                    $"{TypeShapes.TypeName(type)}: frameworkConverter={frameworkConverter}, placeholder={placeholder}, scalarKind={scalarKind}");
            }
        }

        JsonConverter control = options.GetConverter(typeof(IntPtr));
        bool controlRejected =
            control.GetType().Name.StartsWith("UnsupportedTypeConverter", StringComparison.Ordinal) &&
            !ContractAllowlists.ScalarTypes.Contains(typeof(IntPtr));
        bool converterEntriesFramework = ContractAllowlists.Converters.All(entry =>
            ContractAllowlists.IsFrameworkAssembly(entry.Definition) &&
            typeof(JsonConverter).IsAssignableFrom(entry.Definition));
        bool resolverEntriesFramework = ContractAllowlists.Resolvers.All(ContractAllowlists.IsFrameworkAssembly);
        bool verifiedPassed =
            failures.Count == 0 && controlRejected && converterEntriesFramework && resolverEntriesFramework;

        yield return Check.Assert(
            "D06.allowlist.verified",
            "Deny-by-default allowlist",
            "every allowlisted scalar type resolves to a usable framework converter, the allowlisted converters and resolvers ship in the framework assembly, and a type outside the allowlist is rejected by the same test",
            "scalars=framework-converter-and-scalar-kind, converters=framework, resolvers=framework, control=rejected",
            verifiedPassed
                ? "scalars=verified, converters=framework, resolvers=framework, control=rejected"
                : $"scalarFailures={failures.Count}, controlRejected={controlRejected}",
            $"verifiedScalars={verified.Count}/{ContractAllowlists.ScalarTypes.Count}; failures=[{string.Join(", ", failures)}]; " +
            $"control={control.GetType().Name}; controlRejected={controlRejected}; " +
            $"convertersFramework={converterEntriesFramework}; resolversFramework={resolverEntriesFramework}",
            verifiedPassed);
    }

    /// <summary>
    /// Compares the executed bindings of every discovery source with the inventory row that documents it: the
    /// inventory must name a check that actually exercises the source, so documenting a path with the wrong
    /// check id fails the gate.
    /// </summary>
    private static string[] InventoryMismatches(
        IReadOnlyList<KeyValuePair<string, string>> bindings,
        IReadOnlyList<KeyValuePair<string, string>> documented,
        IReadOnlyList<CheckOutcome> checks)
    {
        var mismatches = new List<string>();

        foreach (KeyValuePair<string, string> row in documented)
        {
            string[] exercised = bindings
                .Where(binding => string.Equals(binding.Key, row.Key, StringComparison.Ordinal))
                .Select(static binding => binding.Value)
                .ToArray();

            if (exercised.Length == 0)
            {
                continue;
            }

            if (!exercised.Contains(row.Value, StringComparer.Ordinal) ||
                !checks.Any(check => string.Equals(check.Id, row.Value, StringComparison.Ordinal)))
            {
                mismatches.Add($"{row.Key}: inventory={row.Value}, exercised=[{string.Join(", ", exercised)}]");
            }
        }

        return mismatches.OrderBy(static mismatch => mismatch, StringComparer.Ordinal).ToArray();
    }
}
