using Xunit;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Words;
using ChrysalisAddress = Chrysalis.Cbor.Types.Cardano.Core.Common.Address;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Utxorpc.Sdk.Test;

[Collection("Sequential")]
public class WatchServiceClientTests : IAsyncLifetime
{
    private const string DOLOS_URL = "http://localhost:50051";
    private const string TEST_MNEMONIC = "february next piano since banana hurdle tide soda reward hood luggage bronze polar veteran fold doctor melt usual rose coral mask interest army clump";
    private const string RECEIVER_ADDRESS = "addr_test1qpum0jys999huwckh5wltaclznqpy2je34t8q8ms2sz74x4v465z8v23pjpnxk5hsxstueuejnmku4sfnxx729zdmqhs7tgy54";
    private const string BLOCKFROST_API_KEY = "previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC";
    private const int WATCH_TIMEOUT_SECONDS = 120; // 2 minutes
    
    private readonly WatchServiceClient _watchClient;
    private readonly SubmitServiceClient _submitClient;

    public WatchServiceClientTests()
    {
        _watchClient = new WatchServiceClient(DOLOS_URL);
        _submitClient = new SubmitServiceClient(DOLOS_URL);
    }
    
    public async Task InitializeAsync()
    {
        // No longer submitting a shared transaction
        // Each test will submit its own transaction
        await Task.CompletedTask;
    }
    
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    private static (PrivateKey paymentKey, WalletAddress senderAddress) DeriveKeysAndAddress(string mnemonic)
    {
        var mnemonicObj = Mnemonic.Restore(mnemonic, English.Words);
        var accountKey = mnemonicObj
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(1, DerivationType.HARD);
        
        var paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);
        var stakingKey = accountKey.Derive(RoleType.Staking).Derive(0);
        
        var pkPub = paymentKey.GetPublicKey();
        var skPub = stakingKey.GetPublicKey();
        var addressBody = HashUtil.Blake2b224(pkPub.Key).Concat(HashUtil.Blake2b224(skPub.Key)).ToArray();
        var header = new AddressHeader(AddressType.Base, NetworkType.Testnet);
        var senderAddress = new WalletAddress([header.ToByte(), .. addressBody]);
        
        return (paymentKey, senderAddress);
    }

    private static void BuildAndFinalizeTransaction(
        TransactionBuilder txBuilder, 
        List<ResolvedInput> utxos, 
        PostAlonzoTransactionOutput output, 
        WalletAddress senderAddress)
    {
        // Find a suitable fee input (>= 5 ADA and pure ADA)
        ResolvedInput? feeInput = null;
        foreach (var utxo in utxos.OrderByDescending(e => e.Output.Amount().Lovelace()))
        {
            if (utxo.Output.Amount().Lovelace() >= 5_000_000UL && utxo.Output.Amount() is Lovelace)
            {
                feeInput = utxo;
                break;
            }
        }
        
        if (feeInput is not null)
        {
            utxos.Remove(feeInput);
            txBuilder.AddInput(feeInput.Outref);
        }
        
        // Use coin selection for the output
        var coinSelectionResult = CoinSelectionUtil.LargestFirstAlgorithm(utxos, [output.Amount]);
        
        foreach (var consumed_input in coinSelectionResult.Inputs)
        {
            txBuilder.AddInput(consumed_input.Outref);
        }
        
        // Calculate change
        ulong feeInputLovelace = feeInput?.Output.Amount()!.Lovelace() ?? 0;
        Lovelace lovelaceChange = new(coinSelectionResult.LovelaceChange + feeInputLovelace);
        
        Value changeValue = lovelaceChange;
        if (coinSelectionResult.AssetsChange.Count > 0)
        {
            changeValue = new LovelaceWithMultiAsset(lovelaceChange, new MultiAssetOutput(coinSelectionResult.AssetsChange));
        }
        
        var changeOutput = new PostAlonzoTransactionOutput(
            new ChrysalisAddress(senderAddress.ToBytes()),
            changeValue,
            null,
            null
        );
        
        txBuilder
            .AddOutput(output)
            .AddOutput(changeOutput, true)
            .CalculateFee([]);
    }

    private async Task<byte[]> BuildAndSubmitTransactionWithAssetAsync(string mnemonic, string receiverAddress)
    {
        var (paymentKey, senderAddress) = DeriveKeysAndAddress(mnemonic);
        
        // Use Blockfrost to get UTXOs and protocol parameters
        var provider = new Blockfrost(BLOCKFROST_API_KEY);
        var utxos = await provider.GetUtxosAsync(senderAddress.ToBech32());
        var pparams = await provider.GetParametersAsync();
        
        // Find UTXOs with assets
        Dictionary<byte[], Dictionary<byte[], ulong>>? assetsToSend = null;
        foreach (var utxo in utxos)
        {
            if (utxo.Output.Amount() is LovelaceWithMultiAsset multiAsset)
            {
                var assets = multiAsset.MultiAsset();
                if (assets.Count > 0)
                {
                    // Take the first asset we find
                    var firstPolicy = assets.First();
                    var policyId = firstPolicy.Key;
                    var tokenBundle = firstPolicy.Value;
                    
                    // Get the first asset from this policy
                    var tokenDict = tokenBundle.Value;
                    if (tokenDict.Count > 0)
                    {
                        var firstAsset = tokenDict.First();
                        var assetName = firstAsset.Key;
                        var amount = firstAsset.Value;
                        
                        // Send 1 unit of this asset (or all if less than 1)
                        var assetAmountToSend = Math.Min(1UL, amount);
                        
                        assetsToSend = new Dictionary<byte[], Dictionary<byte[], ulong>>
                        {
                            [policyId] = new Dictionary<byte[], ulong>
                            {
                                [assetName] = assetAmountToSend
                            }
                        };
                        break;
                    }
                }
            }
        }
        
        if (assetsToSend == null)
        {
            throw new InvalidOperationException("No assets found in wallet");
        }
        
        var txBuilder = TransactionBuilder.Create(pparams);
        
        // Define output with assets
        var receiver = new WalletAddress(receiverAddress);
        
        // Create the asset structure for output
        var outputTokenBundle = new Dictionary<byte[], TokenBundleOutput>();
        foreach (var (policyId, assets) in assetsToSend)
        {
            outputTokenBundle[policyId] = new TokenBundleOutput(assets);
        }
        
        // Minimum ADA required when sending assets (usually 1.5-2 ADA)
        var minAdaForAssets = 2_000_000UL;
        var outputValue = new LovelaceWithMultiAsset(
            new Lovelace(minAdaForAssets),
            new MultiAssetOutput(outputTokenBundle)
        );
        
        var output = new PostAlonzoTransactionOutput(
            new ChrysalisAddress(receiver.ToBytes()),
            outputValue,
            null,
            null
        );

        // Build and finalize transaction
        BuildAndFinalizeTransaction(txBuilder, utxos, output, senderAddress);
        
        // Build and sign transaction
        var unsignedTx = txBuilder.Build();
        var signedTx = unsignedTx.Sign(paymentKey);
        
        // Serialize to CBOR
        var cbor = CborSerializer.Serialize(signedTx);
        
        // Submit the transaction via our submit client
        var tx = new Tx(cbor);
        await _submitClient.SubmitTxAsync([tx]);
        
        return cbor;
    }
    

    [Fact]
    public async Task WatchTxForAddress()
    {
        // Arrange
        var receiverAddr = new WalletAddress(RECEIVER_ADDRESS);
        var addressBytes = receiverAddr.ToBytes();
        var addressPredicate = new AddressPredicate(addressBytes, AddressSearchType.ExactAddress);
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(WATCH_TIMEOUT_SECONDS));
        
        // Start watching before submitting
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchTxResponse>();
            await foreach (var txEvent in _watchClient.WatchTxAsync(addressPredicate, cancellationToken: cts.Token))
            {
                events.Add(txEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });
        
        // Give the watcher time to start
        await Task.Delay(1000);
        
        // Submit transaction for this specific test
        await BuildAndSubmitTransactionWithAssetAsync(TEST_MNEMONIC, RECEIVER_ADDRESS);

        // Act - Wait for events
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent);
        Assert.Equal(WatchTxAction.Apply, firstEvent.Action);
        
        // Verify the transaction involves the watched address
        Assert.NotNull(firstEvent.ParsedState);
        if (firstEvent.ParsedState is Utxorpc.V1alpha.Cardano.Tx cardanoTx)
        {
            // Check outputs for the address
            Assert.NotNull(cardanoTx.Outputs);
            Assert.True(cardanoTx.Outputs.Count > 0, "Transaction should have outputs");
            
            var hasAddressInOutputs = cardanoTx.Outputs.Any(output => 
            {
                return output.Address?.ToByteArray().SequenceEqual(addressBytes) == true;
            });
            
            Assert.True(hasAddressInOutputs, "Watched address should be found in transaction outputs");
        }
    }
    
    [Fact]
    public async Task WatchTxForPaymentPart()
    {
        // Arrange
        var receiverAddr = new WalletAddress(RECEIVER_ADDRESS);
        // Correct payment credential for addr_test1qpum0jys999huwckh5wltaclznqpy2je34t8q8ms2sz74x4v465z8v23pjpnxk5hsxstueuejnmku4sfnxx729zdmqhs7tgy54
        var paymentCredBytes = Convert.FromHexString("79b7c890294b7e3b16bd1df5f71f14c0122a598d56701f705405ea9a");
        var paymentPredicate = new AddressPredicate(paymentCredBytes, AddressSearchType.PaymentPart);
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(WATCH_TIMEOUT_SECONDS));
        
        // Start watching before submitting
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchTxResponse>();
            await foreach (var txEvent in _watchClient.WatchTxAsync(paymentPredicate, cancellationToken: cts.Token))
            {
                events.Add(txEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });
        
        // Give the watcher time to start
        await Task.Delay(1000);
        
        // Submit transaction for this specific test
        await BuildAndSubmitTransactionWithAssetAsync(TEST_MNEMONIC, RECEIVER_ADDRESS);

        // Act - Wait for events
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent);
        Assert.Equal(WatchTxAction.Apply, firstEvent.Action);
        
        // Verify the transaction has the payment credential
        Assert.NotNull(firstEvent.ParsedState);
        if (firstEvent.ParsedState is Utxorpc.V1alpha.Cardano.Tx cardanoTx)
        {
            var foundPaymentCred = false;
            
            // Check outputs for payment credential
            if (cardanoTx.Outputs?.Count > 0)
            {
                foundPaymentCred = cardanoTx.Outputs.Any(output =>
                {
                    if (output.Address?.Length >= 29)
                    {
                        var outputPaymentCred = output.Address.ToByteArray().Skip(1).Take(28).ToArray();
                        return outputPaymentCred.SequenceEqual(paymentCredBytes);
                    }
                    return false;
                });
            }
            
            Assert.True(foundPaymentCred, "Transaction should contain an output with the watched payment credential");
        }
    }
    
    [Fact]
    public async Task WatchTxForDelegationPart()
    {
        // Arrange
        var receiverAddr = new WalletAddress(RECEIVER_ADDRESS);
        // Correct delegation credential for addr_test1qpum0jys999huwckh5wltaclznqpy2je34t8q8ms2sz74x4v465z8v23pjpnxk5hsxstueuejnmku4sfnxx729zdmqhs7tgy54
        var delegationCredBytes = Convert.FromHexString("acaea823b1510c83335a9781a0be679994f76e5609998de5144dd82f");
        var delegationPredicate = new AddressPredicate(delegationCredBytes, AddressSearchType.DelegationPart);
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(WATCH_TIMEOUT_SECONDS));
        
        // Start watching before submitting
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchTxResponse>();
            await foreach (var txEvent in _watchClient.WatchTxAsync(delegationPredicate, cancellationToken: cts.Token))
            {
                events.Add(txEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });
        
        // Give the watcher time to start
        await Task.Delay(1000);
        
        // Submit transaction for this specific test
        await BuildAndSubmitTransactionWithAssetAsync(TEST_MNEMONIC, RECEIVER_ADDRESS);

        // Act - Wait for events
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent);
        Assert.Equal(WatchTxAction.Apply, firstEvent.Action);
        
        // Verify the transaction has the delegation credential
        Assert.NotNull(firstEvent.ParsedState);
        if (firstEvent.ParsedState is Utxorpc.V1alpha.Cardano.Tx cardanoTx)
        {
            Assert.NotNull(cardanoTx.Outputs);
            Assert.True(cardanoTx.Outputs.Count > 0, "Transaction should have outputs");
            
            var foundDelegationCred = cardanoTx.Outputs.Any(output =>
            {
                if (output.Address?.Length >= 57) // Base addresses have both payment and delegation parts
                {
                    var outputDelegationCred = output.Address.ToByteArray().Skip(29).Take(28).ToArray();
                    return outputDelegationCred.SequenceEqual(delegationCredBytes);
                }
                return false;
            });
            
            Assert.True(foundDelegationCred, "Transaction should contain an output with the watched delegation credential");
        }
    }
    
    [Fact]
    public async Task WatchTxForPolicyId()
    {
        // Arrange
        var policyIdHex = "8b05e87a51c1d4a0fa888d2bb14dbc25e8c343ea379a171b63aa84a0";
        var policyIdBytes = Convert.FromHexString(policyIdHex);
        var assetPredicate = new AssetPredicate(policyIdBytes, AssetSearchType.PolicyId);
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(WATCH_TIMEOUT_SECONDS));
        
        // Start watching before submitting
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchTxResponse>();
            await foreach (var txEvent in _watchClient.WatchTxAsync(assetPredicate, cancellationToken: cts.Token))
            {
                events.Add(txEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });
        
        // Give the watcher time to start
        await Task.Delay(1000);
        
        // Submit transaction for this specific test
        await BuildAndSubmitTransactionWithAssetAsync(TEST_MNEMONIC, RECEIVER_ADDRESS);

        // Act - Wait for events
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent);
        Assert.Equal(WatchTxAction.Apply, firstEvent.Action);
        
        // Verify the transaction contains assets with the policy ID
        Assert.NotNull(firstEvent.ParsedState);
        if (firstEvent.ParsedState is Utxorpc.V1alpha.Cardano.Tx cardanoTx)
        {
            Assert.NotNull(cardanoTx.Outputs);
            Assert.True(cardanoTx.Outputs.Count > 0, "Transaction should have outputs");
            
            var foundPolicyId = cardanoTx.Outputs.Any(output =>
                output.Assets?.Any(asset =>
                    asset.PolicyId.ToByteArray().SequenceEqual(policyIdBytes)) == true);
            
            Assert.True(foundPolicyId, "Transaction should contain an output with assets having the watched policy ID");
        }
    }
    
    [Fact]
    public async Task WatchTxForAsset()
    {
        // Arrange
        var assetHex = "8b05e87a51c1d4a0fa888d2bb14dbc25e8c343ea379a171b63aa84a0434e4354";
        var assetBytes = Convert.FromHexString(assetHex);
        var assetPredicate = new AssetPredicate(assetBytes, AssetSearchType.AssetName);
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(WATCH_TIMEOUT_SECONDS));
        
        // Start watching before submitting
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchTxResponse>();
            await foreach (var txEvent in _watchClient.WatchTxAsync(assetPredicate, cancellationToken: cts.Token))
            {
                events.Add(txEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });
        
        // Give the watcher time to start
        await Task.Delay(1000);
        
        // Submit transaction for this specific test
        await BuildAndSubmitTransactionWithAssetAsync(TEST_MNEMONIC, RECEIVER_ADDRESS);

        // Act - Wait for events
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent);
        Assert.Equal(WatchTxAction.Apply, firstEvent.Action);
        
        // Verify the transaction contains the complete asset (policyId + assetName)
        Assert.NotNull(firstEvent.ParsedState);
        if (firstEvent.ParsedState is Utxorpc.V1alpha.Cardano.Tx cardanoTx)
        {
            Assert.NotNull(cardanoTx.Outputs);
            Assert.True(cardanoTx.Outputs.Count > 0, "Transaction should have outputs");
            
            // Extract policy ID and asset name from the complete asset bytes
            var policyIdBytes = assetBytes.Take(28).ToArray();
            var assetNameBytes = assetBytes.Skip(28).ToArray();
            
            var foundAsset = cardanoTx.Outputs.Any(output =>
                output.Assets?.Any(assetGroup =>
                {
                    // Check if this policy ID matches
                    if (assetGroup.PolicyId.ToByteArray().SequenceEqual(policyIdBytes))
                    {
                        // Now check if any asset in this group has the expected name
                        return assetGroup.Assets?.Any(asset =>
                            asset.Name?.ToByteArray().SequenceEqual(assetNameBytes) == true) == true;
                    }
                    return false;
                }) == true);
            
            Assert.True(foundAsset, "Transaction should contain an output with the watched asset (policyId + assetName)");
        }
    }
}