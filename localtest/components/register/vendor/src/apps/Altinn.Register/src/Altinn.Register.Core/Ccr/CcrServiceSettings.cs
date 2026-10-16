using System.ComponentModel.DataAnnotations;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Settings for the <see cref="CcrService"/>.
/// </summary>
public sealed class CcrServiceSettings
    : IValidatableObject
{
    /// <summary>
    /// Allowed CCR clients, keyed by username.
    /// </summary>
    public Dictionary<string, CcrClientIdentitySettings> Clients { get; set; }
        = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    IEnumerable<ValidationResult> IValidatableObject.Validate(
        ValidationContext validationContext)
    {
        foreach (var kvp in Clients)
        {
            var userName = kvp.Key;
            var settings = kvp.Value;
            var name = $"Clients[{userName}]";

            if (settings is null)
            {
                yield return new ValidationResult(
                    "Client settings cannot be null.",
                    [name]);
                continue;
            }

            var childContext = new ValidationContext(settings, validationContext, validationContext.Items);

            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(settings, childContext, results, validateAllProperties: true))
            {
                foreach (var result in results)
                {
                    yield return new ValidationResult(
                        result.ErrorMessage,
                        result.MemberNames
                            .Select(m => $"{name}.{m}")
                            .DefaultIfEmpty(name));
                }
            }
        }
    }
}
