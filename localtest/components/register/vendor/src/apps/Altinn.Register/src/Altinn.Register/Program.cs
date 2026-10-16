using Altinn.Register;
using Microsoft.IdentityModel.Logging;

WebApplication app = RegisterHost.Create(args);

if (app.Configuration.GetValue<bool>("Altinn:RunInitOnly", defaultValue: false))
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Running in init-only mode. Initializing Altinn Register...");
    logger.LogInformation("Environment: {EnvironmentName}", app.Environment.EnvironmentName);

    await app.StartAsync();
    logger.LogInformation("Altinn Register initialized successfully - shutting down.");

    await app.StopAsync();
    logger.LogInformation("Altinn Register shutdown complete.");

    return;
}

app.AddDefaultAltinnMiddleware(errorHandlingPath: "/register/api/v1/error");

if (app.Environment.IsDevelopment())
{
    // Enable higher level of detail in exceptions related to JWT validation
    IdentityModelEventSource.ShowPII = true;

    // Enable Swagger
    app.UseSwagger();
    app.UseSwaggerUI(opt =>
    {
        // build a swagger endpoint for each discovered API version
        foreach (var desc in app.DescribeApiVersions())
        {
            var url = $"/swagger/{desc.GroupName}/swagger.json";
            var name = desc.GroupName.ToUpperInvariant();
            opt.SwaggerEndpoint(url, name);
        }
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultAltinnEndpoints();
app.MapControllers();

await app.RunAsync();

/// <summary>
/// Startup class.
/// </summary>
public sealed partial class Program
{
    private Program()
    {
    }
}
