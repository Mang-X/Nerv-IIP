using Opc.Ua;
using Opc.Ua.Client;
using System.Text;

namespace Nerv.IIP.ConnectorHost.Connectors.OpcUa;

#pragma warning disable CS0618
public sealed class OpcUaNetStandardClient(IOpcUaCredentialResolver credentialResolver) : IOpcUaClient, IDisposable
{
    private Session? _session;
    private Subscription? _subscription;
    private string? _lastKeepAliveError;

    public async Task ConnectAsync(OpcUaConnectionOptions options, CancellationToken cancellationToken)
    {
        await DisconnectAsync(cancellationToken);
        _lastKeepAliveError = null;

        var configuration = CreateApplicationConfiguration(options);
        await configuration.Validate(ApplicationType.Client);

        var endpointDescription = await CoreClientUtils.SelectEndpointAsync(
            configuration,
            options.EndpointUrl,
            UseSecurity(options),
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
        _session.KeepAlive += (_, args) =>
        {
            if (ServiceResult.IsBad(args.Status))
            {
                _lastKeepAliveError = $"OPC UA keepalive failed: {args.Status}";
            }
        };
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
        _subscription.Create();
        var samplingWindowMilliseconds = Math.Max(1000, tags.Max(x => x.SamplingIntervalMilliseconds) * 2);
        await Task.Delay(TimeSpan.FromMilliseconds(samplingWindowMilliseconds), cancellationToken);
        if (_lastKeepAliveError is not null)
        {
            throw new OpcUaConnectionLostException(_lastKeepAliveError);
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _subscription?.Delete(true);
        _subscription = null;
        _session?.Close();
        _session?.Dispose();
        _session = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _session?.Dispose();
    }

    private Session RequireSession()
    {
        return _session ?? throw new InvalidOperationException("OPC UA session is not connected.");
    }

    private static bool UseSecurity(OpcUaConnectionOptions options)
    {
        return !string.Equals(options.SecurityPolicy, "None", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.SecurityMode, "None", StringComparison.OrdinalIgnoreCase);
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

    private static ApplicationConfiguration CreateApplicationConfiguration(OpcUaConnectionOptions options)
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
                    StorePath = "CertificateStores/OpcUa/own",
                    SubjectName = "Nerv.IIP Connector Host OPC UA"
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "CertificateStores/OpcUa/trusted"
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "CertificateStores/OpcUa/rejected"
                },
                AutoAcceptUntrustedCertificates = options.AutoAcceptUntrustedServerCertificates
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 15_000
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = 60_000
            }
        };
    }
}
