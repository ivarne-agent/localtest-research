using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Core.Utils;

/// <summary>
/// Extensions for configuration-related functionality.
/// </summary>
public static class ConfigurationExtensions
{
    /// <param name="optionsBuilder">The options builder to add the services to.</param>
    /// <typeparam name="TOptions">The options type to be configured.</typeparam>
    extension<TOptions>(OptionsBuilder<TOptions> optionsBuilder)
        where TOptions : class
    {
        /// <summary>
        /// Registers the dependency injection container to bind <typeparamref name="TOptions"/> against
        /// the <see cref="IConfiguration"/> obtained from the DI service provider, using an intermediate
        /// raw options type to facilitate transformation of the configuration values before they're applied
        /// to the final options type.
        /// </summary>
        /// <typeparam name="TOptionsRaw">The type of the raw options to bind from the configuration.</typeparam>
        /// <param name="configSectionPath">The name of the configuration section to bind from.</param>
        /// <param name="apply">A delegate to apply the raw options to the final options type.</param>
        /// <param name="configureBinder">Optional. Used to configure the <see cref="BinderOptions"/>.</param>
        /// <returns>The <see cref="OptionsBuilder{TOptions}"/> so that additional calls can be chained.</returns>
        public OptionsBuilder<TOptions> BindConfigurationAs<TOptionsRaw>(
            string configSectionPath,
            Action<TOptions, TOptionsRaw> apply,
            Action<BinderOptions>? configureBinder = null)
            where TOptionsRaw : class, new()
        {
            Guard.IsNotNull(optionsBuilder);
            Guard.IsNotNull(configSectionPath);
            Guard.IsNotNull(apply);

            optionsBuilder.Configure<IConfiguration>((opts, config) =>
            {
                IConfiguration section = string.Equals(string.Empty, configSectionPath, StringComparison.OrdinalIgnoreCase)
                    ? config
                    : config.GetSection(configSectionPath);

                TOptionsRaw raw = new();
                section.Bind(raw, configureBinder);
                apply(opts, raw);
            });

            optionsBuilder.Services.AddSingleton<IOptionsChangeTokenSource<TOptions>, ConfigurationChangeTokenSource<TOptions>>(sp =>
            {
                return new ConfigurationChangeTokenSource<TOptions>(optionsBuilder.Name, sp.GetRequiredService<IConfiguration>());
            });

            return optionsBuilder;
        }
    }
}
