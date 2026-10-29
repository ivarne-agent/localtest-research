using System.Text;

namespace Altinn.App.Aspire;

/// <summary>
/// The app's secrets directory, named as the platform names it for a deployed app (the manifest's
/// <c>app.secrets</c>), and the development app codes every runner writes into it: the same bytes as studioctl's
/// <c>appsecrets.WriteDevelopmentAppCodes</c> (a test compares them with its golden file).
/// </summary>
internal static class AppSecrets
{
    /// <summary>
    /// The development app codes: the operator's shape, with the workflow engine's callback code only, since that is
    /// the one callback an app both mints and verifies locally. The code is valid from before any local run until
    /// long after, because nothing rotates it locally; callback tokens are signed with HMAC-SHA256, so the app
    /// libraries refuse a code shorter than 16 bytes.
    /// </summary>
    public const string DevelopmentAppCodes = """
        {
          "AppCodes": {
            "WorkflowEngineCallback": [
              {
                "Code": "LOCAL-DEV-ONLY-workflow-engine-callback-secret",
                "ExpiresAt": "2999-01-01T00:00:00Z",
                "Id": "local-dev",
                "IssuedAt": "2020-01-01T00:00:00Z"
              }
            ]
          }
        }

        """;

    /// <summary>The app's secrets directory under the package's data directory, as studioctl places it.</summary>
    public static string Directory(string dataDirectory, string org, string app) =>
        Path.Combine(dataDirectory, "apps", org, app, "secrets");

    /// <summary>
    /// Creates the directory owner-only and writes the app codes into it, owner-only and atomically, unless the file
    /// holds exactly these bytes already (an app polling it is not woken by every run).
    /// </summary>
    public static string WriteDevelopmentAppCodes(string directory, string fileName)
    {
        EnsureDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var payload = Encoding.UTF8.GetBytes(DevelopmentAppCodes.ReplaceLineEndings("\n"));
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(payload))
        {
            return path;
        }
        WriteOwnerOnly(directory, fileName, payload);
        return path;
    }

    /// <summary>
    /// Writes a file into the secrets directory, creating the directory owner-only: owner-only and atomic (a temporary
    /// file in the same directory renamed into place), so an app polling the file never reads a half-written one.
    /// </summary>
    public static void WriteOwnerOnly(string directory, string fileName, byte[] payload)
    {
        EnsureDirectory(directory);
        var path = Path.Combine(directory, fileName);
        var temporary = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            var create = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                create.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using (var stream = new FileStream(temporary, create))
            {
                stream.Write(payload);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void EnsureDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateDirectory(directory);
            return;
        }
        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        System.IO.Directory.CreateDirectory(directory, ownerOnly);
        File.SetUnixFileMode(directory, ownerOnly);
    }
}
