using CommunityToolkit.Diagnostics;
using Xunit.v3;

namespace Altinn.Register.TestUtils.Traits;

/// <summary>
/// Attribute used to decorate a test method, test class, or assembly with an arbitrary category name ("trait").
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public class CategoryAttribute
    : Attribute
    , ITraitAttribute
{
    internal static string TraitName { get; } = "Category";

    public CategoryAttribute(string name)
    {
        Guard.IsNotNullOrWhiteSpace(name);

        Name = name;
    }

    /// <summary>
    /// Get the category name.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits()
        => [new(TraitName, Name)];
}
