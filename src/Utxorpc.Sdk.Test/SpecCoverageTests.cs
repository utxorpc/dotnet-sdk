using System.Xml.Linq;
using System.Reflection;
using Google.Protobuf.Reflection;
using Grpc.Core;

namespace Utxorpc.Sdk.Test;

/// <summary>Detects generated service clients and RPCs missing from the SDK surface.</summary>
[Trait("Category", "Unit")]
public sealed class SpecCoverageTests
{
    internal static Type[] GeneratedClients()
    {
        Type[] clients = typeof(Utxorpc.V1Beta.Sync.SyncService).Assembly.GetTypes()
            .Where(type => type.Namespace is { } ns && (ns.StartsWith("Utxorpc.V1Alpha.", StringComparison.Ordinal)
                || ns.StartsWith("Utxorpc.V1Beta.", StringComparison.Ordinal)))
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
                    MethodInfo[] overloads = methods.Where(method => method.Name == ShortName(rpc.Name) && method.GetParameters()[0].ParameterType == client).ToArray();
                    Assert.NotEmpty(overloads);
                    foreach (MethodInfo method in overloads)
                    {
                        ParameterInfo? mask = method.GetParameters().SingleOrDefault(parameter => parameter.Name == "fieldMask");
                        Assert.Equal(rpc.InputType.FindFieldByName("field_mask") is not null, mask is not null);
                        if (mask is null) continue;
                        Assert.Equal(typeof(Google.Protobuf.WellKnownTypes.FieldMask), mask.ParameterType);
                        Assert.True(mask.HasDefaultValue);
                        Assert.Null(mask.DefaultValue);
                        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(mask).ReadState);
                    }
                }
            }
            index++;
        }
        Assert.Equal(signatures[0].Select(signature => signature.Replace("V1Alpha", "V1Beta", StringComparison.Ordinal)).Order(StringComparer.Ordinal), signatures[1].Order(StringComparer.Ordinal));
    }

    /// <summary>Requires exactly one documented mask target for each masked request in both versions.</summary>
    [Fact]
    public void EveryMaskedRequestIsInTheReadme()
    {
        string readme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "README.md"));
        string section = readme.Split("## Field masks and bytes", StringSplitOptions.None)[1].Split("## Cardano inputs", StringSplitOptions.None)[0];
        Dictionary<(string Version, string Request), string> rows = section.Split('\n')
            .Where(line => line.StartsWith("| v1", StringComparison.Ordinal))
            .Select(line => line.Split('|').Skip(1).Take(3).Select(cell => cell.Trim().Trim('`')).ToArray())
            .ToDictionary(cells => (cells[0], cells[1]), cells => cells[2]);
        MethodDescriptor[] masked = MaskedMethods().ToArray();
        Assert.Equal(masked.Length, rows.Count);
        foreach (MethodDescriptor rpc in masked)
        {
            string version = rpc.InputType.ClrType.Namespace!.Split('.')[1];
            string prefix = $"Utxorpc.{version}.";
            Assert.True(rows.TryGetValue((version.ToLowerInvariant(), rpc.InputType.ClrType.FullName![prefix.Length..]), out string? target),
                $"Missing mask row for {rpc.InputType.FullName}");
            if (rpc.Name is "WatchTx" or "WatchMempool")
            {
                Assert.Equal("Not named by the Spec", target);
                continue;
            }
            MessageDescriptor expected = rpc.Service.Name == "SyncService"
                ? rpc.InputType.File.MessageTypes.Single(message => message.Name == "AnyChainBlock") : rpc.OutputType;
            MessageDescriptor actual = Assert.Single(rpc.InputType.File.MessageTypes, message => message.ClrType.FullName == prefix + target);
            Assert.Same(expected, actual);
        }
    }

    /// <summary>Requires complete caller-facing documentation, including extension-member inheritdoc targets.</summary>
    [Fact]
    public void EveryPublicCallIsDocumented()
    {
        XDocument xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Utxorpc.Sdk.xml"));
        Dictionary<string, XElement> members = xml.Descendants("member").ToDictionary(element => (string)element.Attribute("name")!);
        Type[] types = typeof(UtxorpcClient).Assembly.GetTypes().Where(type => type.Namespace == "Utxorpc.Sdk"
            && ((type.Name.StartsWith("V1", StringComparison.Ordinal) && type.Name.EndsWith("Extensions", StringComparison.Ordinal))
                || type == typeof(UtxorpcServiceCollectionExtensions) || type == typeof(ServerStreamingCallExtensions))).ToArray();
        IEnumerable<MethodBase> methods = types.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)).Cast<MethodBase>()
            .Concat(typeof(UtxorpcClient).GetConstructors());
        int checkedMembers = 0;
        foreach (MethodBase method in methods)
        {
            string prefix = "M:" + method.DeclaringType!.FullName + "." + (method.IsConstructor ? "#ctor" : method.Name);
            if (method.IsGenericMethod) prefix += "``" + method.GetGenericArguments().Length;
            ParameterInfo[] parameters = method.GetParameters();
            string id = prefix + (parameters.Length == 0 ? "" : "(" + string.Join(",", parameters.Select(parameter => TypeName(parameter.ParameterType))) + ")");
            Assert.True(members.TryGetValue(id, out XElement? entry), $"Missing XML documentation: {id}");
            XElement documented = entry!;
            while (documented.Element("inheritdoc") is XElement inherited) documented = members[(string)inherited.Attribute("cref")!];
            Assert.False(string.IsNullOrWhiteSpace(documented.Element("summary")?.Value), id);
            Assert.True(documented.Elements("remarks").Count() <= 1, $"Multiple remarks elements: {id}");
            Assert.Equal(parameters.Select(parameter => parameter.Name).Order(StringComparer.Ordinal), documented.Elements("param").Select(element => (string?)element.Attribute("name")).Order(StringComparer.Ordinal));
            if (!method.IsConstructor) Assert.False(string.IsNullOrWhiteSpace(documented.Element("returns")?.Value), id);
            checkedMembers++;
        }
        Assert.True(checkedMembers >= 110, $"Checked only {checkedMembers} members.");
        static string TypeName(Type type) => type.IsByRef ? TypeName(type.GetElementType()!) + "@"
            : type.IsGenericParameter ? "``" + type.GenericParameterPosition
            : type.IsGenericType ? type.GetGenericTypeDefinition().FullName!.Split('`')[0].Replace('+', '.') + "{" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + "}"
            : type.FullName!.Replace('+', '.');
    }

    internal static HashSet<Type> MaskedRequestTypes(string version) => MaskedMethods()
        .Where(rpc => rpc.InputType.ClrType.Namespace!.Split('.')[1] == version).Select(rpc => rpc.InputType.ClrType).ToHashSet();

    private static IEnumerable<MethodDescriptor> MaskedMethods() => GeneratedClients().SelectMany(client => Descriptor(client).Methods)
        .Where(rpc => rpc.InputType.FindFieldByName("field_mask") is not null);

    internal static ServiceDescriptor Descriptor(Type client) => (ServiceDescriptor)client.DeclaringType!
        .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static string ShortName(string name) => name switch
    {
        "SearchUtxos" => "SearchAllUtxosAsync",
        "DumpHistory" => "DumpAllHistoryAsync",
        _ => name + "Async"
    };
}
