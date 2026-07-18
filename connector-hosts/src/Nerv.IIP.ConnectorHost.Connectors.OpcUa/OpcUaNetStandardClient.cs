using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Text;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa;

#pragma warning disable CS0618
public sealed class OpcUaNetStandardClient(
    IOpcUaCredentialResolver credentialResolver,
    TimeSpan? connectionDetectionBudget = null) : IOpcUaClient, IDisposable
{
    private Session? _session;
    private Subscription? _subscription;
    private KeepAliveEventHandler? _keepAliveHandler;
    private readonly TimeSpan _connectionDetectionBudget = connectionDetectionBudget ?? TimeSpan.FromSeconds(4);

    public Task ConnectAsync(OpcUaConnectionOptions options, CancellationToken cancellationToken)
    {
        return ConnectAsync(options, static () => { }, cancellationToken);
    }

    public async Task ConnectAsync(
        OpcUaConnectionOptions options,
        Action onConnectionLost,
        CancellationToken cancellationToken)
    {
        await DisconnectAsync(cancellationToken);

        var configuration = CreateApplicationConfiguration(options);
        await configuration.Validate(ApplicationType.Client);
        if (options.UsesSecurity)
        {
            var application = new ApplicationInstance(configuration)
            {
                ApplicationName = configuration.ApplicationName,
                ApplicationType = ApplicationType.Client
            };
            var certificateReady = await application.CheckApplicationInstanceCertificatesAsync(false, 60, cancellationToken);
            if (!certificateReady)
            {
                throw new InvalidOperationException("OPC UA client application certificate could not be created or loaded.");
            }
        }

        var endpointDescription = await CoreClientUtils.SelectEndpointAsync(
            configuration,
            options.EndpointUrl,
            options.UsesSecurity,
            telemetry: null!,
            cancellationToken);
        var endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create(configuration));
        var identity = await CreateIdentityAsync(options, cancellationToken);

        _session = await Session.Create(
            configuration,
            endpoint,
            updateBeforeConnect: true,
            checkDomain: false,
            sessionName: "Nerv.IIP Connector Host OPC UA",
            sessionTimeout: 60_000,
            identity,
            preferredLocales: null,
            cancellationToken);
        _session.KeepAliveInterval = checked((int)Math.Max(500, _connectionDetectionBudget.TotalMilliseconds / 2));
        _keepAliveHandler = (_, args) =>
        {
            if (ServiceResult.IsBad(args.Status))
            {
                onConnectionLost();
            }
        };
        _session.KeepAlive += _keepAliveHandler;
    }

    public async Task<IReadOnlyList<OpcUaNode>> BrowseAsync(string rootNodeId, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        var browser = new Browser(session)
        {
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable
        };

        var references = await browser.BrowseAsync(NodeId.Parse(rootNodeId), cancellationToken);
        return references
            .Select(reference => new OpcUaNode(reference.NodeId.ToString(), reference.DisplayName.Text, reference.NodeClass == NodeClass.Variable))
            .ToList();
    }

    public async Task SubscribeAsync(
        IReadOnlyList<OpcUaTagSubscription> tags,
        Func<OpcUaDataChange, CancellationToken, Task> onDataChange,
        CancellationToken cancellationToken)
    {
        var session = RequireSession();
        _subscription = new Subscription(session.DefaultSubscription)
        {
            DisplayName = "Nerv.IIP IndustrialTelemetry OPC UA",
            PublishingEnabled = true,
            PublishingInterval = tags.Min(x => x.SamplingIntervalMilliseconds),
            KeepAliveCount = 10,
            LifetimeCount = 100
        };

        foreach (var tag in tags)
        {
            var item = new MonitoredItem(_subscription.DefaultItem)
            {
                StartNodeId = NodeId.Parse(tag.NodeId),
                AttributeId = Attributes.Value,
                DisplayName = tag.TagKey,
                SamplingInterval = tag.SamplingIntervalMilliseconds,
                QueueSize = 100,
                DiscardOldest = true
            };
            item.Notification += (monitoredItem, args) =>
            {
                foreach (var value in item.DequeueValues())
                {
                    _ = onDataChange(
                        new OpcUaDataChange(
                            tag.NodeId,
                            value.Value,
                            value.SourceTimestamp == DateTime.MinValue
                                ? DateTimeOffset.UtcNow
                                : new DateTimeOffset(DateTime.SpecifyKind(value.SourceTimestamp, DateTimeKind.Utc)),
                            StatusCode.IsGood(value.StatusCode) ? "Good" : value.StatusCode.ToString()),
                        cancellationToken);
                }
            };
            _subscription.AddItem(item);
        }

        session.AddSubscription(_subscription);
        await _subscription.CreateAsync(cancellationToken);
        await _subscription.ApplyChangesAsync(cancellationToken);
    }

    public async Task<OpcUaWriteReceipt> WriteAsync(OpcUaWriteRequest request, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        var value = new WriteValue
        {
            NodeId = NodeId.Parse(request.NodeId),
            AttributeId = Attributes.Value,
            Value = new DataValue(new Variant(request.Value))
        };
        var values = new WriteValueCollection { value };
        var response = await session.WriteAsync(
            null,
            values,
            cancellationToken);
        var status = response.Results.Count == 0 ? StatusCodes.BadUnexpectedError : response.Results[0];
        var statusText = StatusCode.IsGood(status) ? "Good" : status.ToString();
        return new OpcUaWriteReceipt(
            statusText,
            StatusCode.IsGood(status) ? "opcua.write.accepted" : "opcua.write.rejected",
            statusText);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _subscription?.Delete(true);
        _subscription = null;
        if (_session is not null && _keepAliveHandler is not null)
        {
            _session.KeepAlive -= _keepAliveHandler;
        }

        _keepAliveHandler = null;
        _session?.Close();
        _session?.Dispose();
        _session = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        if (_session is not null && _keepAliveHandler is not null)
        {
            _session.KeepAlive -= _keepAliveHandler;
        }

        _session?.Dispose();
    }

    private Session RequireSession()
    {
        return _session ?? throw new InvalidOperationException("OPC UA session is not connected.");
    }

    private async Task<UserIdentity> CreateIdentityAsync(OpcUaConnectionOptions options, CancellationToken cancellationToken)
    {
        var credential = await credentialResolver.ResolveAsync(options.CredentialReference, cancellationToken);
        return credential is null
            ? new UserIdentity()
            : new UserIdentity(new UserNameIdentityToken
            {
                UserName = credential.UserName,
                Password = Encoding.UTF8.GetBytes(credential.Password)
            });
    }

    private ApplicationConfiguration CreateApplicationConfiguration(OpcUaConnectionOptions options)
    {
        return new ApplicationConfiguration
        {
            ApplicationName = "Nerv.IIP Connector Host OPC UA",
            ApplicationUri = $"urn:{Environment.MachineName}:Nerv.IIP.ConnectorHost.OpcUa",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = GetCertificateStorePath("own"),
                    SubjectName = "Nerv.IIP Connector Host OPC UA"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = GetCertificateStorePath("trusted")
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = GetCertificateStorePath("issuers")
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = GetCertificateStorePath("rejected")
                },
                AutoAcceptUntrustedCertificates = options.AutoAcceptUntrustedServerCertificates
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = checked((int)Math.Max(500, _connectionDetectionBudget.TotalMilliseconds / 2))
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = 60_000
            }
        };
    }

    private static string GetCertificateStorePath(string storeName)
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.Combine(Path.GetTempPath(), "Nerv.IIP");
        }

        return Path.Combine(basePath, "Nerv.IIP", "ConnectorHost", "OpcUa", "CertificateStores", storeName);
    }
}
