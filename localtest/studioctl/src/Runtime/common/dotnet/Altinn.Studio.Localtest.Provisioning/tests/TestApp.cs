namespace Altinn.Studio.Localtest.Provisioning.Tests;

internal static class TestApp
{
    /// <summary>The fixture copy of <c>frontend-test</c>'s app repository (the folder holding <c>App/</c>).</summary>
    public static string FrontendTestRoot => Path.Combine(AppContext.BaseDirectory, "TestApps", "frontend-test");

    public static string FrontendTestFile(string relative) =>
        Path.Combine(FrontendTestRoot, "App", relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A scratch copy of the fixture, deleted on dispose, for tests that change files.</summary>
    public static ScratchApp CopyFrontendTest() => new(FrontendTestRoot);

    internal sealed class ScratchApp : IDisposable
    {
        public ScratchApp(string source)
        {
            Root = Directory.CreateTempSubdirectory("provisioning-tests-").FullName;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        public string Root { get; }

        public string AppFile(string relative) =>
            Path.Combine(Root, "App", relative.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
