using Xunit;
using Utxorpc.Sdk;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;

namespace Utxorpc.Sdk.Test;

public class QueryServiceClientTests
{
    private const string DOLOS_URL = "http://localhost:50051";
    private readonly QueryServiceClient _client;

    public QueryServiceClientTests()
    {
        _client = new QueryServiceClient(DOLOS_URL);
    }

    [Fact]
    public async Task ReadParams_ShouldReturnChainParameters()
    {
        var response = await _client.ReadParamsAsync(null);

        Console.WriteLine("\n=== ReadParams Response ===");
        Console.WriteLine($"LedgerTip Slot: {response.LedgerTip?.Slot}");
        Console.WriteLine($"LedgerTip Hash: {Convert.ToHexString(response.LedgerTip?.Hash ?? [])}");
        
        if (response.Values?.Params is Utxorpc.V1alpha.Cardano.PParams cardanoParams)
        {
            Console.WriteLine("\nCardano Protocol Parameters:");
            Console.WriteLine($"  Protocol Version: {cardanoParams.ProtocolVersion?.Major}.{cardanoParams.ProtocolVersion?.Minor}");
            Console.WriteLine($"  Min Fee Coefficient: {cardanoParams.MinFeeCoefficient}");
            Console.WriteLine($"  Min Fee Constant: {cardanoParams.MinFeeConstant}");
            Console.WriteLine($"  Max Tx Size: {cardanoParams.MaxTxSize}");
            Console.WriteLine($"  Max Block Body Size: {cardanoParams.MaxBlockBodySize}");
            Console.WriteLine($"  Coins Per UTxO Byte: {cardanoParams.CoinsPerUtxoByte}");
            Console.WriteLine($"  Pool Deposit: {cardanoParams.PoolDeposit}");
            Console.WriteLine($"  Key Deposit: {cardanoParams.StakeKeyDeposit}");
        }

        Assert.NotNull(response);
        Assert.NotNull(response.Values);
        Assert.NotNull(response.Values.Params);
        Assert.NotNull(response.LedgerTip);
    }

    [Fact]
    public async Task ReadUtxos_WithValidTxoRef_ShouldReturnUtxo()
    {
        var txHash = Convert.FromBase64String("n6GJYXlgCNoeWLf+EpkCpXjctW2l1FjFsSRIF4rSGo0=");
        var index = 0u;

        var txoRef = new TxoRef(txHash, index);
        var response = await _client.ReadUtxosAsync([txoRef], null);

        Console.WriteLine("\n=== ReadUtxos Response ===");
        Console.WriteLine($"Items Count: {response.Items?.Count ?? 0}");
        Console.WriteLine($"LedgerTip Slot: {response.LedgerTip?.Slot}");
        Console.WriteLine($"LedgerTip Hash: {Convert.ToHexString(response.LedgerTip?.Hash ?? [])}");
        
        foreach (var item in response.Items ?? [])
        {
            Console.WriteLine($"\nUTXO TxRef: {Convert.ToHexString(item.TxoRef?.Hash ?? [])}#{item.TxoRef?.Index}");
            Console.WriteLine($"Native Bytes Length: {item.NativeBytes?.Length ?? 0}");
            if (item.ParsedState is Utxorpc.V1alpha.Cardano.TxOutput cardanoOutput)
            {
                Console.WriteLine($"Address: {Convert.ToBase64String(cardanoOutput.Address?.ToByteArray() ?? [])}");
                Console.WriteLine($"Value: {cardanoOutput.Coin} lovelace");
                Console.WriteLine($"Assets Count: {cardanoOutput.Assets?.Count ?? 0}");
            }
        }

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
    }

    [Fact]
    public async Task ReadUtxos_WithMultipleTxoRefs_ShouldReturnUtxos()
    {
        var txoRefs = new[]
        {
            new TxoRef(Convert.FromBase64String("n6GJYXlgCNoeWLf+EpkCpXjctW2l1FjFsSRIF4rSGo0="), 0),
            new TxoRef(Convert.FromBase64String("rJsJlkeNYBRjjLvr9GR7UEXZWluuWvsm9Ut7OYiT6ik="), 0)
        };

        var response = await _client.ReadUtxosAsync(txoRefs, null);

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
        Assert.NotNull(response.LedgerTip);
    }

    [Fact]
    public async Task SearchUtxos_ByAddress_ShouldReturnResults()
    {
        var addressBytes = Convert.FromBase64String("AFP7//q3sAEoGRfed/GKgIdBO+A0AdtKoqfb8K4VkdNNW0snKNBKgP3QQbtS7bM02svyWqJ4d+c4"); 
        var predicate = new AddressPredicate(addressBytes, AddressSearchType.ExactAddress);

        var response = await _client.SearchUtxosAsync(predicate, maxItems: 10, fieldMask: null);

        Console.WriteLine("\n=== SearchUtxos (By Address) Response ===");
        Console.WriteLine($"Items Count: {response.Items?.Count ?? 0}");
        Console.WriteLine($"Next Token: {response.NextToken ?? "null"}");
        Console.WriteLine($"LedgerTip Slot: {response.LedgerTip?.Slot}");
        Console.WriteLine($"LedgerTip Hash: {Convert.ToHexString(response.LedgerTip?.Hash ?? [])}");
        
        int count = 0;
        foreach (var item in response.Items?.Take(3) ?? [])
        {
            count++;
            Console.WriteLine($"\nUTXO #{count}:");
            Console.WriteLine($"  TxRef: {Convert.ToHexString(item.TxoRef?.Hash ?? [])}#{item.TxoRef?.Index}");
            if (item.ParsedState is Utxorpc.V1alpha.Cardano.TxOutput cardanoOutput)
            {
                Console.WriteLine($"  Value: {cardanoOutput.Coin} lovelace");
            }
        }

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
        Assert.NotNull(response.LedgerTip);
    }

    [Fact]
    public async Task SearchUtxos_ByPaymentCredential_ShouldReturnResults()
    {
        var paymentCredBytes = Convert.FromBase64String("U/v/+rewASgZF9538YqAh0E74DQB20qip9vwrg==");
        var predicate = new AddressPredicate(paymentCredBytes, AddressSearchType.PaymentPart);

        var response = await _client.SearchUtxosAsync(predicate, maxItems: 5, fieldMask: null);

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
    }

    [Fact]
    public async Task SearchUtxos_ByDelegationPart_ShouldReturnResults()
    {
        var delegationCredBytes = Convert.FromBase64String("FZHTTVtLJyjQSoD90EG7Uu2zNNrL8lqieHfnOA==");
        var predicate = new AddressPredicate(delegationCredBytes, AddressSearchType.DelegationPart);

        var response = await _client.SearchUtxosAsync(predicate, maxItems: 5, fieldMask: null);

        Console.WriteLine("\n=== SearchUtxos (By Delegation Part) Response ===");
        Console.WriteLine($"Items Count: {response.Items?.Count ?? 0}");
        Console.WriteLine($"LedgerTip Slot: {response.LedgerTip?.Slot}");

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
    }

    [Fact]
    public async Task SearchUtxos_ByPolicyId_ShouldReturnResults()
    {
        var policyIdBytes = Convert.FromBase64String("BH4PkSxCYP5mriceWuSU3NX3ljW7uxOGvhlfTg==");
        var predicate = new AssetPredicate(policyIdBytes, AssetSearchType.PolicyId);

        var response = await _client.SearchUtxosAsync(predicate, maxItems: 10, fieldMask: null);

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
    }

    [Fact]
    public async Task SearchUtxos_ByAssetName_ShouldReturnResults()
    {
        var assetBytes = Convert.FromBase64String("BH4PkSxCYP5mriceWuSU3NX3ljW7uxOGvhlfTkFMTEVZS0FUWjAwMDYw");
        var predicate = new AssetPredicate(assetBytes, AssetSearchType.AssetName);

        var response = await _client.SearchUtxosAsync(predicate, maxItems: 10, fieldMask: null);

        Console.WriteLine("\n=== SearchUtxos (By Asset Name) Response ===");
        Console.WriteLine($"Items Count: {response.Items?.Count ?? 0}");
        Console.WriteLine($"LedgerTip Slot: {response.LedgerTip?.Slot}");
        
        foreach (var item in response.Items?.Take(2) ?? [])
        {
            if (item.ParsedState is Utxorpc.V1alpha.Cardano.TxOutput cardanoOutput && cardanoOutput.Assets != null)
            {
                Console.WriteLine($"UTxO has {cardanoOutput.Assets.Count} asset policy groups");
            }
        }

        Assert.NotNull(response);
        Assert.NotNull(response.Items);
    }

    [Fact]
    public async Task SearchUtxos_WithPagination_ShouldHandleNextToken()
    {
        var addressBytes = Convert.FromBase64String("cGFnaW5hdGlvbmFkZHJlc3MxMjM0NTY3ODkw");
        var predicate = new AddressPredicate(addressBytes, AddressSearchType.ExactAddress);

        var firstPage = await _client.SearchUtxosAsync(predicate, maxItems: 2, fieldMask: null);
        
        Assert.NotNull(firstPage);
        
        if (!string.IsNullOrEmpty(firstPage.NextToken))
        {
            var secondPage = await _client.SearchUtxosAsync(
                predicate, 
                maxItems: 2, 
                fieldMask: null, 
                start_token: firstPage.NextToken
            );
            
            Assert.NotNull(secondPage);
            Assert.NotNull(secondPage.Items);
        }
    }

    [Fact]
    public async Task Client_ShouldHandleConnectionErrors_Gracefully()
    {
        var badClient = new QueryServiceClient("http://localhost:9999");
        
        await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
        {
            await badClient.ReadParamsAsync(null);
        });
    }
}