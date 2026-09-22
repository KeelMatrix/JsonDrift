using System.Text.Json.Serialization;

namespace KeelMatrix.JsonDrift.RuleMatrix.Contracts;

// R10 - polymorphic discriminator and type metadata
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DogV1), "dog")]
internal abstract class AnimalV1
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class DogV1 : AnimalV1
{
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DogV1Extended), "dog")]
[JsonDerivedType(typeof(CatV1Extended), "cat")]
internal abstract class AnimalV1Extended
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class DogV1Extended : AnimalV1Extended
{
}

internal sealed class CatV1Extended : AnimalV1Extended
{
}

internal sealed class ConcreteAnimal
{
    public string Name { get; set; } = string.Empty;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AbstractDog), "dog")]
internal abstract class AbstractAnimal
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class AbstractDog : AbstractAnimal
{
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ConcreteDog), "dog")]
internal class ConcretePolymorphicAnimal
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class ConcreteDog : ConcretePolymorphicAnimal
{
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DogV2Renamed), "canine")]
internal abstract class AnimalV2Renamed
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class DogV2Renamed : AnimalV2Renamed
{
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DogV3Property), "canine")]
internal abstract class AnimalV3Property
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class DogV3Property : AnimalV3Property
{
}
