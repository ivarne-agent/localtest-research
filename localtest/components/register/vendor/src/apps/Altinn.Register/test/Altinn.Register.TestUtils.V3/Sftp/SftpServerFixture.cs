using System.Diagnostics;
using Renci.SshNet;
using Testcontainers.Sftp;

namespace Altinn.Register.TestUtils.Sftp;

/// <summary>
/// An assembly fixture that starts a single SFTP server (the <c>atmoz/sftp</c> image) in a
/// test-container, and provides helpers for uploading files to it.
/// </summary>
public sealed class SftpServerFixture
    : IAsyncLifetime
{
    /// <summary>The username used to authenticate against the SFTP server.</summary>
    public const string Username = "ccr";

    // The atmoz/sftp user is chrooted to its home directory, so the writable upload
    // directory configured below is exposed to clients at "/{UploadDirectory}".
    private const string UploadDirectory = "upload";

    private string password = Guid.NewGuid().ToString("N");

    private readonly SftpContainer _container;
    private Task? _startedTask = null;
    private readonly Lock _lock = new();

    public SftpServerFixture()
    {
        var builder = new SftpBuilder("atmoz/sftp:alpine")
            .WithUsername(Username)
            .WithPassword(password)
            .WithUploadDirectory(UploadDirectory)
            .WithCleanUp(true);

        if (Debugger.IsAttached)
        {
            builder = builder.WithPortBinding(44182, SftpBuilder.SftpPort);
        }

        _container = builder.Build();
    }

    /// <summary>
    /// Allocates connection details for use by a single test, including a freshly created unique
    /// upload directory under <see cref="UploadRootPath"/> so concurrent tests sharing this server
    /// don't interfere with each other.
    /// </summary>
    /// <returns>An <see cref="SftpServerInfo"/> whose <see cref="SftpServerInfo.UploadDirectory"/>
    /// is the absolute remote path of the directory created for this caller.</returns>
    public async Task<SftpServerInfo> CreateTestServer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await EnsureContainerStartedAsync(cancellationToken);

        var uploadDirectory = $"{UploadRootPath}/{Guid.NewGuid():N}";

        using var client = new SftpClient(Host, Port, Username, password);
        await client.ConnectAsync(cancellationToken);
        try
        {
            client.CreateDirectory(uploadDirectory);
        }
        finally
        {
            client.Disconnect();
        }

        return new SftpServerInfo(Host, Port, Username, password, uploadDirectory);
    }

    /// <summary>Gets the host the SFTP server is reachable on.</summary>
    public string Host => _container.Hostname;

    /// <summary>Gets the (randomly assigned) host port the SFTP server is reachable on.</summary>
    public int Port => _container.GetMappedPublicPort(SftpBuilder.SftpPort);

    /// <summary>
    /// Gets the writable root directory as seen by the chrooted SFTP user.
    /// </summary>
    public string UploadRootPath => $"/{UploadDirectory}";

    /// <summary>
    /// Lazily starts the underlying container exactly once, on the first call to
    /// <see cref="CreateTestServer"/>. Tests that never request a server (the majority in the
    /// assembly) don't pay the container-startup cost.
    /// </summary>
    private Task EnsureContainerStartedAsync(CancellationToken cancellationToken)
    {
        var task = Volatile.Read(ref _startedTask);
        if (task is not null)
        {
            return task.WaitAsync(cancellationToken);
        }

        lock (_lock)
        {
            task = Volatile.Read(ref _startedTask);
            if (task is not null)
            {
                return task.WaitAsync(cancellationToken);
            }

            task = StartContainerAsync(_container);
            Volatile.Write(ref _startedTask, task);
            return task.WaitAsync(cancellationToken);
        }

        static async Task StartContainerAsync(SftpContainer container)
        {
            await Task.Yield();
            await container.StartAsync(CancellationToken.None);
        }
    }

    ValueTask IAsyncLifetime.InitializeAsync() => ValueTask.CompletedTask;

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        Task? task;
        lock (_lock)
        {
            task = Volatile.Read(ref _startedTask);
        }

        if (task is not null)
        {
            await task;
        }

        await _container.DisposeAsync();
    }
}
