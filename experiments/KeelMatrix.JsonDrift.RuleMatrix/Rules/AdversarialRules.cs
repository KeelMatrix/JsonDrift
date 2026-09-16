using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// The executable adversarial check set. Each case builds a contract that tries to keep opaque metadata
/// behind a path no rule names, and each must fail closed: the classifier reports the contract unsupported,
/// the canonical document records it as unsupported, and a report that contains it cannot be green. The cases
/// cover the shapes the walk is most likely to miss, including registered derived types that are collections,
/// dictionaries, or polymorphic bases, polymorphic values reached through members, collections, and
/// dictionaries, member-level converters inside a derived type, converter factories, metadata customization
/// callbacks, resolver chains, converters declared on a generic argument, and scalar types the allowlist does
/// not cover.
/// </summary>
internal static class AdversarialRules
{
    private sealed record Case(
        string Id,
        string Change,
        MetadataSourceKind Source,
        string PathId,
        Func<JsonTypeInfo> Contract,
        string Evidence = "",
        string RejectedEvidence = "");

    public static IEnumerable<CheckOutcome> Run()
    {
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();
        JsonSerializerOptions factory = JsonContractOptions.Reflection(new AdversarialFactory());
        JsonSerializerOptions customization = AdversarialMetadataCustomization.Options();
        JsonSerializerOptions chain = AdversarialMetadataCustomization.Chained();

        Case[] cases =
        {
            new(
                "A01.adversarial.derived-collection",
                "a registered derived type is itself a collection",
                MetadataSourceKind.PolymorphismDerivedTypes,
                "A01.adversarial.derived-collection",
                () => reflection.GetTypeInfo(typeof(RevOpaqueSequenceBase)),
                $"\"elementType\": \"{TypeShapes.TypeName(typeof(RevOpaqueItem))}\""),
            new(
                "A01.adversarial.derived-dictionary",
                "a registered derived type is itself a dictionary",
                MetadataSourceKind.PolymorphismDerivedTypes,
                "A01.adversarial.derived-dictionary",
                () => reflection.GetTypeInfo(typeof(RevOpaqueMapBase)),
                $"\"valueType\": \"{TypeShapes.TypeName(typeof(RevOpaqueItem))}\""),
            new(
                "A01.adversarial.derived-polymorphic-base",
                "polymorphism is nested inside a registered derived type",
                MetadataSourceKind.PolymorphismDerivedTypes,
                "A01.adversarial.derived-polymorphic-base",
                () => reflection.GetTypeInfo(typeof(PolyOuterBase))),
            new(
                "A01.adversarial.polymorphic-member",
                "a polymorphic type is reached through a member",
                MetadataSourceKind.ObjectMembers,
                "A01.adversarial.polymorphic-member",
                () => reflection.GetTypeInfo(typeof(PolyMemberHolder))),
            new(
                "A01.adversarial.polymorphic-collection",
                "a collection holds polymorphic values",
                MetadataSourceKind.EnumerableElementTypes,
                "A01.adversarial.polymorphic-collection",
                () => reflection.GetTypeInfo(typeof(PolyListHolder))),
            new(
                "A01.adversarial.polymorphic-dictionary",
                "a dictionary holds polymorphic values",
                MetadataSourceKind.DictionaryValueTypes,
                "A01.adversarial.polymorphic-dictionary",
                () => reflection.GetTypeInfo(typeof(PolyMapHolder))),
            new(
                "A01.adversarial.derived-member-converter",
                "a member of a registered derived type declares an unrecognized converter",
                MetadataSourceKind.MemberConverterAttribute,
                "A01.adversarial.derived-member-converter",
                () => reflection.GetTypeInfo(typeof(PolyConverterRoot))),
            new(
                "A01.adversarial.converter-factory",
                "a converter factory is registered for the type of a member",
                MetadataSourceKind.OptionsConverters,
                "A01.adversarial.converter-factory",
                () => factory.GetTypeInfo(typeof(AdversarialFactoryHolder))),
            new(
                "A01.adversarial.type-info-modifier",
                "a metadata customization callback replaces a member converter",
                MetadataSourceKind.ResolverChain,
                "A01.adversarial.type-info-modifier",
                () => customization.GetTypeInfo(typeof(InvoiceAmounts))),
            new(
                "A01.adversarial.resolver-chain",
                "the options resolve metadata through a resolver chain",
                MetadataSourceKind.ResolverChain,
                "A01.adversarial.resolver-chain",
                () => chain.GetTypeInfo(typeof(InvoiceAmounts))),
            new(
                "A01.adversarial.generic-argument",
                "a converter is declared on the type that is used as a generic argument",
                MetadataSourceKind.ObjectMembers,
                "A01.adversarial.generic-argument",
                () => reflection.GetTypeInfo(typeof(AdversarialGenericArgumentHolder))),
            new(
                "A01.adversarial.unlisted-scalar-type",
                "a member has a scalar type the framework resolves to an unsupported converter",
                MetadataSourceKind.ObjectMembers,
                "A01.adversarial.unlisted-scalar-type",
                () => reflection.GetTypeInfo(typeof(UnlistedScalarHolder))),
        };

        foreach (Case adversarialCase in cases)
        {
            yield return OpaqueReachCheck.Create(
                adversarialCase.Id,
                adversarialCase.Change,
                MetadataSourceRules.Id(adversarialCase.Source),
                adversarialCase.PathId,
                adversarialCase.Contract(),
                "Deny-by-default adversarial check",
                adversarialCase.Evidence,
                adversarialCase.RejectedEvidence);
        }
    }
}
