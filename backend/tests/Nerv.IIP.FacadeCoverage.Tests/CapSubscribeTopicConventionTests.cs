using System.Reflection;

namespace Nerv.IIP.FacadeCoverage.Tests;

/// <summary>
/// Regression guard for the cross-service CAP consume defect fixed in MAN-443 / #797.
///
/// netcorepal publishes integration events on the CAP topic <c>typeof(TEvent).Name</c> (the short
/// type name — every <c>cap.published.Name</c> row confirms this). A consumer must therefore
/// <c>[CapSubscribe]</c> to that SAME short name; many services had subscribed with hardcoded
/// fully-qualified names (<c>"Nerv.IIP.Contracts.&lt;domain&gt;.&lt;Event&gt;"</c>), so their queues never
/// bound to the published routing key and they silently consumed nothing under a real broker.
///
/// This test reflects every <c>ICapSubscribe</c> handler and asserts the <c>[CapSubscribe]</c> topic
/// equals the short name of the consumed event type, except for explicitly registered versioned
/// canonical topics whose producers publish an ADR 0011 routing key instead of the CLR alias.
/// </summary>
public sealed class CapSubscribeTopicConventionTests
{
    // Web assemblies that host CAP consumers. Kept independent of the facade-coverage list because
    // this convention also governs the non-business hosts (Notification, AppHub).
    private static readonly string[] ConsumerWebAssemblyNames =
    [
        "Nerv.IIP.Business.Erp.Web",
        "Nerv.IIP.Business.IndustrialTelemetry.Web",
        "Nerv.IIP.Business.Inventory.Web",
        "Nerv.IIP.Business.Maintenance.Web",
        "Nerv.IIP.Business.Mes.Web",
        "Nerv.IIP.Business.Quality.Web",
        "Nerv.IIP.Business.Scheduling.Web",
        "Nerv.IIP.Business.Wms.Web",
        "Nerv.IIP.Notification.Web",
        "Nerv.IIP.AppHub.Web",
    ];

    private static readonly IReadOnlyDictionary<string, HashSet<string>> CanonicalTopicExceptions =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["MesOperationActualTimeSettledV2IntegrationEvent"] =
            [
                "nerv-iip.development.business-mes.mes.operation-actual-time-settled.v2",
                "nerv-iip.production.business-mes.mes.operation-actual-time-settled.v2",
            ],
            ["MesOperationActualTimeSettlementVoidedV2IntegrationEvent"] =
            [
                "nerv-iip.development.business-mes.mes.operation-actual-time-settlement-voided.v2",
                "nerv-iip.production.business-mes.mes.operation-actual-time-settlement-voided.v2",
            ],
        };

    [Fact]
    public void CapSubscribe_topics_match_the_event_short_name()
    {
        var checkedCount = 0;
        var violations = new List<string>();

        foreach (var assemblyName in ConsumerWebAssemblyNames)
        {
            var assembly = LoadAssembly(assemblyName);
            foreach (var type in SafeGetTypes(assembly))
            {
                if (!ImplementsCapSubscribe(type))
                {
                    continue;
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var topics = ReadCapSubscribeTopics(method);
                    if (topics.Count == 0)
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length == 0)
                    {
                        continue;
                    }

                    var eventShortName = parameters[0].ParameterType.Name;
                    foreach (var topic in topics)
                    {
                        checkedCount++;
                        if (!string.Equals(topic, eventShortName, StringComparison.Ordinal)
                            && !(CanonicalTopicExceptions.TryGetValue(eventShortName, out var canonicalTopics)
                                && canonicalTopics.Contains(topic)))
                        {
                            violations.Add(
                                $"{assemblyName} :: {type.Name}.{method.Name} subscribes to topic \"{topic}\" " +
                                $"but the event type short name is \"{eventShortName}\" and no exact canonical exception is registered.");
                        }
                    }
                }
            }
        }

        Assert.True(
            checkedCount >= 40,
            $"Expected to reflect >= 40 [CapSubscribe] methods across consumer assemblies but found {checkedCount}. " +
            "An assembly may have failed to load or the reflection shape changed.");

        Assert.True(
            violations.Count == 0,
            "CapSubscribe topics must equal the event short name (netcorepal publishes typeof(T).Name):\n  "
                + string.Join("\n  ", violations));
    }

    private static bool ImplementsCapSubscribe(Type type) =>
        type.GetInterfaces().Any(i => string.Equals(i.Name, "ICapSubscribe", StringComparison.Ordinal));

    private static IReadOnlyList<string> ReadCapSubscribeTopics(MethodInfo method)
    {
        var topics = new List<string>();
        foreach (var attribute in method.GetCustomAttributes())
        {
            if (!string.Equals(attribute.GetType().Name, "CapSubscribeAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            // CapSubscribeAttribute exposes the topic via its "Name" property.
            if (attribute.GetType().GetProperty("Name")?.GetValue(attribute) is string value)
                topics.Add(value);
        }

        return topics;
    }

    private static Assembly LoadAssembly(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (Exception)
        {
            return Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, name + ".dll"));
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
    }
}
