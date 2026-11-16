using System.Reflection;
using Google.Protobuf.Reflection;
using Grpc.Core;

namespace Utxorpc.Sdk.Test;

/// <summary>Detects generated service clients and RPCs missing from the SDK surface.</summary>
[Trait("Category", "Unit")]
public sealed class SpecCoverageTests
{
    private static Type[] GeneratedClients()
    {
        Type[] clients = typeof(Utxorpc.V1Beta.Sync.SyncService).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.BaseType is { IsConstructedGenericType: true } parent
                && parent.GetGenericTypeDefinition() == typeof(ClientBase<>)).ToArray();
        Assert.NotEmpty(clients);
        return clients;
    }

    /// <summary>Requires a public version group and property for every generated service client.</summary>
    [Fact]
    public void EveryGeneratedServiceClientIsOffered()
    {
        foreach (Type client in GeneratedClients())
        {
            string version = client.Namespace!.Split('.')[1];
            PropertyInfo? group = typeof(UtxorpcClient).GetProperty(version);
            Assert.True(group is not null, $"Missing version group for {client.FullName}");
            Assert.True(group.PropertyType.GetProperties().Any(property => property.PropertyType == client),
                $"Missing generated service client: {client.FullName}");
        }
    }

    /// <summary>Requires an SDK extension for every RPC, including server streams and paging.</summary>
    [Fact]
    public void EveryRpcHasAShortMethod()
    {
        MethodInfo[] methods = typeof(UtxorpcClient).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)).ToArray();
        foreach (Type client in GeneratedClients())
        {
            ServiceDescriptor service = (ServiceDescriptor)client.DeclaringType!
                .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            foreach (MethodDescriptor rpc in service.Methods)
            {
                string name = rpc.Name switch
                {
                    "SearchUtxos" => "SearchAllUtxosAsync",
                    "DumpHistory" => "DumpAllHistoryAsync",
                    _ => rpc.Name + "Async"
                };
                Assert.True(methods.Any(method => method.Name == name
                    && method.GetParameters() is [ParameterInfo receiver, ..] && receiver.ParameterType == client),
                    $"Missing short method for {service.FullName}/{rpc.Name}");
            }
        }
    }
}
