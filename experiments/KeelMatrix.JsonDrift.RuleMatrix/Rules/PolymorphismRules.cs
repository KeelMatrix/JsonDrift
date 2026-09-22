using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using KeelMatrix.JsonDrift.RuleMatrix.Contracts;
using KeelMatrix.JsonDrift.RuleMatrix.Matrix;

namespace KeelMatrix.JsonDrift.RuleMatrix.Rules;

/// <summary>
/// R10 polymorphic discriminator and type metadata.
/// </summary>
internal static class PolymorphismRules
{
    public static IEnumerable<CheckOutcome> Run()
    {
        var results = new List<CheckOutcome>();
        JsonSerializerOptions reflection = JsonContractOptions.Reflection();

        JsonTypeInfo animalV1 = reflection.GetTypeInfo(typeof(AnimalV1));
        JsonTypeInfo animalV1Extended = reflection.GetTypeInfo(typeof(AnimalV1Extended));
        JsonTypeInfo animalV2Renamed = reflection.GetTypeInfo(typeof(AnimalV2Renamed));
        JsonTypeInfo animalV3Property = reflection.GetTypeInfo(typeof(AnimalV3Property));
        JsonTypeInfo concreteAnimal = reflection.GetTypeInfo(typeof(ConcreteAnimal));
        JsonTypeInfo abstractAnimal = reflection.GetTypeInfo(typeof(AbstractAnimal));
        JsonTypeInfo concretePolymorphicAnimal = reflection.GetTypeInfo(typeof(ConcretePolymorphicAnimal));

        var dogV1 = new DogV1 { Name = "Rex" };
        var dogV1Extended = new DogV1Extended { Name = "Rex" };
        var dogV2Renamed = new DogV2Renamed { Name = "Rex" };
        var dogV3Property = new DogV3Property { Name = "Rex" };
        var concrete = new ConcreteAnimal { Name = "Rex" };
        var abstractDog = new AbstractDog { Name = "Rex" };
        var concretePolymorphic = new ConcretePolymorphicAnimal { Name = "Rex" };

        results.AddRange(Check.Classify(
            "R10.polymorphism.discriminator-value-renamed",
            "Polymorphic type metadata change",
            "a derived type discriminator value is renamed",
            WireProbe.Across(dogV1, animalV1, animalV2Renamed),
            readerBackwardCompatible: false,
            WireProbe.Across(dogV2Renamed, animalV2Renamed, animalV1),
            writerForwardCompatible: false));

        results.AddRange(Check.Classify(
            "R10.polymorphism.discriminator-property-renamed",
            "Polymorphic type metadata change",
            "the discriminator property name changes",
            WireProbe.Across(dogV1, animalV1, animalV3Property),
            readerBackwardCompatible: false,
            WireProbe.Across(dogV3Property, animalV3Property, animalV1),
            writerForwardCompatible: false));

        results.AddRange(Check.Classify(
            "R10.polymorphism.derived-type-added",
            "Polymorphic type metadata change",
            "an additional derived type is registered",
            WireProbe.Across(dogV1, animalV1, animalV1Extended),
            readerBackwardCompatible: true,
            WireProbe.Across(dogV1Extended, animalV1Extended, animalV1),
            writerForwardCompatible: true));

        results.AddRange(Check.Classify(
            "R10d.polymorphism.discriminator-required",
            "Polymorphic reader materialization change",
            "a concrete non-polymorphic contract becomes an abstract polymorphic contract",
            WireProbe.Across(concrete, concreteAnimal, abstractAnimal),
            readerBackwardCompatible: false,
            WireProbe.Across(abstractDog, abstractAnimal, concreteAnimal),
            writerForwardCompatible: false));

        results.AddRange(Check.Classify(
            "R10e.polymorphism.dispatch-added-concrete",
            "Polymorphic reader materialization change",
            "polymorphic dispatch is added while the declared base remains concrete",
            WireProbe.Across(concrete, concreteAnimal, concretePolymorphicAnimal),
            readerBackwardCompatible: true,
            WireProbe.Across(concretePolymorphic, concretePolymorphicAnimal, concreteAnimal),
            writerForwardCompatible: true));

        results.Add(Check.Compatible(
            "R10.polymorphism.control",
            "Polymorphic type metadata change",
            "the contract is unchanged",
            WireProbe.Across(dogV1, animalV1, animalV1)));

        return results;
    }
}
