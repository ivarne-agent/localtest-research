using System.Collections.Immutable;
using System.Text;
using Altinn.Register.Contracts.V1;
using Altinn.Register.Model.Extensions;

using CommunityToolkit.Diagnostics;

using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Altinn.Register.ModelBinding;

/// <summary>
/// Binds the <see cref="PartyComponentOptions"/> model.
/// </summary>
public class PartyComponentOptionModelBinder
    : ModelBinder<PartyComponentOptions>,
    ISingleton<PartyComponentOptionModelBinder>
{
    private static ImmutableDictionary<PartyComponentOptions, string> _stringified = ImmutableDictionary<PartyComponentOptions, string>.Empty;

    private static readonly string PersonNameOption = "person-name";

    private PartyComponentOptionModelBinder()
    {
    }

    /// <summary>
    /// The allowed values.
    /// </summary>
    public static readonly ImmutableArray<string> AllowedValues = [PersonNameOption];

    /// <summary>
    /// Stringify a <see cref="PartyComponentOptions"/> to a query-valid value.
    /// </summary>
    /// <param name="value">The value to stringify.</param>
    /// <returns>The stringified value, or <see langword="null"/> if it is <see cref="PartyComponentOptions.None"/>.</returns>
    public static string? Stringify(PartyComponentOptions value)
    {
        if (value.HasFlag(PartyComponentOptions.None))
        {
            return null;
        }

        return ImmutableInterlocked.GetOrAdd(ref _stringified, value, CreateString);

        static string CreateString(PartyComponentOptions value)
        {
            var builder = new StringBuilder();
            if (value.HasFlag(PartyComponentOptions.NameComponents))
            {
                builder.Append(PersonNameOption);
            }

            return builder.ToString();
        }
    }

    /// <inheritdoc/>
    public override Task BindModelAsync(ModelBindingContext bindingContext)
    {
        Guard.IsNotNull(bindingContext);

        var values = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        var result = PartyComponentOptions.None;

        bool hasErrors = false;
        foreach (var value in values)
        {
            foreach (var name in value.AsSpan().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                result |= Parse(name, bindingContext, ref hasErrors);
            }
        }

        bindingContext.Result = hasErrors ? ModelBindingResult.Failed() : ModelBindingResult.Success(result);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public static PartyComponentOptionModelBinder Instance { get; } = new();

    private static PartyComponentOptions Parse(ReadOnlySpan<char> name, ModelBindingContext bindingContext, ref bool hasErrors)
    {
        if (name.Equals(PersonNameOption, StringComparison.Ordinal))
        {
            return PartyComponentOptions.NameComponents;
        }

        hasErrors = true;
        bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"Invalid value: '{name}'");
        return PartyComponentOptions.None;
    }
}
