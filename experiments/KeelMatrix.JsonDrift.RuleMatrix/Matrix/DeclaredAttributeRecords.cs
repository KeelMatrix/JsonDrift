namespace KeelMatrix.JsonDrift.RuleMatrix.Matrix;

/// <summary>An explicitly excluded runtime System.Text.Json declaration type and its measured reason.</summary>
internal sealed record DeclaredAttributeExclusion(Type Type, string Reason);
