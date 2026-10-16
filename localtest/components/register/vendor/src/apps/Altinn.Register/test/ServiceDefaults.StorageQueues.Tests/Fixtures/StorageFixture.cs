using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage.Queues;
using Testcontainers.Azurite;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.Fixtures;

internal sealed class StorageFixture
    : IAsyncLifetime
{
    private const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";
    private const string CertificatePath = "/certs/azurite.pem";
    private const string PrivateKeyPath = "/certs/azurite-key.pem";

    private readonly Lock _lock = new();
    private Task<AzuriteContainer>? _startedTask = null;
    private uint _queueCounter = 0;

    public HttpPipelineTransport CreateTransport()
    {
        var handler = new HttpClientHandler
        {
            // We use a self-signed certificate for the container
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };

        return new HttpClientTransport(handler);
    }

    public async Task<QueueInfo> CreateQueue()
    {
        var container = await EnsureContainerStartedAsync(CancellationToken.None);

        var queueEndpoint = GetStorageAccountUri(container);
        var client = new QueueServiceClient(
            BuildConnectionString(container),
            new QueueClientOptions
            {
                Transport = CreateTransport(),
            });

        var name = $"queue-{Interlocked.Increment(ref _queueCounter):D5}";
        var poisonName = $"{name}-poison";

        var cancellationToken = TestContext.Current.CancellationToken;
        await client.CreateQueueAsync(name, cancellationToken: cancellationToken);
        await client.CreateQueueAsync(poisonName, cancellationToken: cancellationToken);

        var credential = new StorageTokenCredential(subject: name);
        return new QueueInfo
        {
            StorageAccountUri = queueEndpoint,
            Credential = credential,
            QueueName = name,
            PoisonQueueName = poisonName,
        };
    }

    public QueueClient CreateQueueClient(QueueInfo queue)
        => CreateQueueClient(queue.StorageAccountUri, queue.QueueName, queue.Credential);

    public QueueClient CreatePoisonQueueClient(QueueInfo queue)
        => CreateQueueClient(queue.StorageAccountUri, queue.PoisonQueueName, queue.Credential);

    public QueueClient CreateQueueClient(Uri storageAccountUri, string queueName, TokenCredential credential)
        => new(
            new Uri(storageAccountUri, queueName),
            credential,
            new QueueClientOptions
            {
                Transport = CreateTransport(),
            });

    /// <summary>
    /// Lazily starts the underlying container exactly once, on the first call to
    /// <see cref="CreateQueue"/>. Tests that never request a queue (the majority in the
    /// assembly) don't pay the container-startup cost.
    /// </summary>
    private Task<AzuriteContainer> EnsureContainerStartedAsync(CancellationToken cancellationToken)
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

            task = StartContainerAsync();
            Volatile.Write(ref _startedTask, task);
            return task.WaitAsync(cancellationToken);
        }

        static async Task<AzuriteContainer> StartContainerAsync()
        {
            await Task.Yield();

            var (certificatePem, privateKeyPem) = CreateSelfSignedCertificatePem();

            var container = new AzuriteBuilder(AzuriteImage)
                .WithResourceMapping(Encoding.ASCII.GetBytes(certificatePem), CertificatePath)
                .WithResourceMapping(Encoding.ASCII.GetBytes(privateKeyPem), PrivateKeyPath)
                .WithCommand(
                    "--skipApiVersionCheck", // https://github.com/Azure/Azurite/issues/2651
                    "--oauth",
                    "basic",
                    "--cert",
                    CertificatePath,
                    "--key",
                    PrivateKeyPath)
                .Build();
            await container.StartAsync(CancellationToken.None);
            return container;
        }
    }

    ValueTask IAsyncLifetime.InitializeAsync() => ValueTask.CompletedTask;

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        Task<AzuriteContainer>? task;
        lock (_lock)
        {
            task = Volatile.Read(ref _startedTask);
        }

        if (task is not null)
        {
            await (await task).DisposeAsync();
        }
    }

    private static string BuildConnectionString(AzuriteContainer container)
        => $"DefaultEndpointsProtocol=https;AccountName={AzuriteBuilder.AccountName};AccountKey={AzuriteBuilder.AccountKey};QueueEndpoint={GetStorageAccountUri(container)};";

    private static Uri GetStorageAccountUri(AzuriteContainer container)
        => new($"https://{container.Hostname}:{container.GetMappedPublicPort(AzuriteBuilder.QueuePort)}/{AzuriteBuilder.AccountName}/");

    private static (string CertificatePem, string PrivateKeyPem) CreateSelfSignedCertificatePem()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=127.0.0.1",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddDnsName("localhost");
        subjectAlternativeName.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(subjectAlternativeName.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(14));

        return (certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    private sealed class StorageTokenCredential(string subject)
        : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Only use async overloads");
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            const string tenantId = "00000000-0000-0000-0000-000000000000";
            var now = DateTimeOffset.UtcNow;
            var expires = now.AddHours(1);

            using var rsa = RSA.Create(2048);
            var header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = "RS256",
                typ = "JWT",
            }));

            var payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
            {
                aud = "https://storage.azure.com/",
                iss = $"https://sts.windows.net/{tenantId}/",
                sub = subject,
                appid = "11111111-1111-1111-1111-111111111111",
                oid = Guid.CreateVersion7(), // object id
                tid = tenantId, // tenant id
                ver = "1.0",
                iat = now.ToUnixTimeSeconds(), // issued at
                nbf = now.ToUnixTimeSeconds(), // not before
                exp = expires.ToUnixTimeSeconds(), // expires
            }));

            var signingInput = $"{header}.{payload}";
            var signature = Base64Url.EncodeToString(rsa.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));

            var jwt = $"{signingInput}.{signature}";
            return ValueTask.FromResult(new AccessToken(jwt, expires));
        }
    }

    public sealed class QueueInfo
    {
        public required Uri StorageAccountUri { get; init; }

        public required TokenCredential Credential { get; init; }

        public required string QueueName { get; init; }

        public required string PoisonQueueName { get; init; }
    }
}
