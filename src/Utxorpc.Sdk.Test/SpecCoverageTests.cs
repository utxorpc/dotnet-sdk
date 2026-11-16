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
            ServiceDescriptor service = Descriptor(client);
            foreach (MethodDescriptor rpc in service.Methods)
            {
                string name = ShortName(rpc.Name);
                Assert.True(methods.Any(method => method.Name == name
                    && method.GetParameters() is [ParameterInfo receiver, ..] && receiver.ParameterType == client),
                    $"Missing short method for {service.FullName}/{rpc.Name}");
            }
        }
    }
    /// <summary>Requires cancellation and field-mask parameters to follow the Spec and agree in both versions.</summary>
    [Fact]
    public void ShortMethodsMatchTheSpecInBothVersions()
    {
        List<string>[] signatures = [new(), new()];
        int index = 0;
        foreach (string version in new[] { "V1Alpha", "V1Beta" })
        {
            MethodInfo[] methods = typeof(UtxorpcClient).Assembly.GetTypes()
                .Where(type => type.Namespace == "Utxorpc.Sdk" && type.Name.StartsWith(version, StringComparison.Ordinal) && type.Name.EndsWith("Extensions", StringComparison.Ordinal))
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(method => method.GetParameters() is [ParameterInfo first, ..] && first.Name == "client").ToArray();
            foreach (MethodInfo method in methods)
            {
                ParameterInfo token = method.GetParameters()[^1];
                Assert.Equal(typeof(CancellationToken), token.ParameterType);
                Assert.Equal("cancellationToken", token.Name);
                Assert.True(token.HasDefaultValue);
                if (method.Name != "ReadStateAsync") signatures[index].Add(method.Name + "(" + string.Join(",", method.GetParameters().Skip(1)
                    .Select(parameter => $"{parameter.ParameterType}:{parameter.Name}:{parameter.HasDefaultValue}:{parameter.DefaultValue}")) + ")");
            }
            foreach (Type client in GeneratedClients().Where(type => type.Namespace!.Split('.')[1] == version))
            {
                foreach (MethodDescriptor rpc in Descriptor(client).Methods)
                {
                    MethodInfo method = Assert.Single(methods, method => method.Name == ShortName(rpc.Name) && method.GetParameters()[0].ParameterType == client);
                    ParameterInfo? mask = method.GetParameters().SingleOrDefault(parameter => parameter.Name == "fieldMask");
                    Assert.Equal(rpc.InputType.FindFieldByName("field_mask") is not null, mask is not null);
                    if (mask is null) continue;
                    Assert.Equal(typeof(Google.Protobuf.WellKnownTypes.FieldMask), mask.ParameterType);
                    Assert.True(mask.HasDefaultValue);
                    Assert.Null(mask.DefaultValue);
                    Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(mask).ReadState);
                }
            }
            index++;
        }
        Assert.Equal(signatures[0].Select(signature => signature.Replace("V1Alpha", "V1Beta", StringComparison.Ordinal)).Order(StringComparer.Ordinal), signatures[1].Order(StringComparer.Ordinal));
    }

    internal static HashSet<Type> MaskedRequestTypes(string version) => GeneratedClients()
        .Where(client => client.Namespace!.Split('.')[1] == version).SelectMany(client => Descriptor(client).Methods)
        .Where(rpc => rpc.InputType.FindFieldByName("field_mask") is not null).Select(rpc => rpc.InputType.ClrType).ToHashSet();

    private static ServiceDescriptor Descriptor(Type client) => (ServiceDescriptor)client.DeclaringType!
        .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static string ShortName(string name) => name switch
    {
        "SearchUtxos" => "SearchAllUtxosAsync",
        "DumpHistory" => "DumpAllHistoryAsync",
        _ => name + "Async"
    };
}
