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

public class WatchServiceClientTests
{
    private const string DOLOS_URL = "http://localhost:50051";
    private const string TEST_MNEMONIC = "february next piano since banana hurdle tide soda reward hood luggage bronze polar veteran fold doctor melt usual rose coral mask interest army clump";
    private const string RECEIVER_ADDRESS = "addr_test1qpflhll6k7cqz2qezl080uv2szr5zwlqxsqakj4z5ldlpts4j8f56k6tyu5dqj5qlhgyrw6jakenfkkt7fd2y7rhuuuquqeeh5";
    private const string BLOCKFROST_API_KEY = "previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC";
    private const ulong AMOUNT_TO_SEND = 6_000_000UL; // 6 ADA
    
    private readonly WatchServiceClient _watchClient;
    private readonly SubmitServiceClient _submitClient;

    public WatchServiceClientTests()
    {
        _watchClient = new WatchServiceClient(DOLOS_URL);
        _submitClient = new SubmitServiceClient(DOLOS_URL);
    }

    private static async Task<byte[]> BuildTransactionAsync(string mnemonic, ulong amountToSend, string receiverAddress)
    {
        // Derive keys from mnemonic
        var mnemonicObj = Mnemonic.Restore(mnemonic, English.Words);
        var accountKey = mnemonicObj
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);
        
        var paymentKey = accountKey.Derive(RoleType.ExternalChain).Derive(0);
        var stakingKey = accountKey.Derive(RoleType.Staking).Derive(0);
        
        // Create sender address
        var pkPub = paymentKey.GetPublicKey();
        var skPub = stakingKey.GetPublicKey();
        var addressBody = HashUtil.Blake2b224(pkPub.Key).Concat(HashUtil.Blake2b224(skPub.Key)).ToArray();
        var header = new AddressHeader(AddressType.BasePayment, NetworkType.Testnet);
        var senderAddress = new WalletAddress([header.ToByte(), .. addressBody]);
        
        // Use Blockfrost to get UTXOs and protocol parameters
        var provider = new Blockfrost(BLOCKFROST_API_KEY);
        var utxos = await provider.GetUtxosAsync(senderAddress.ToBech32());
        var pparams = await provider.GetParametersAsync();
        var txBuilder = TransactionBuilder.Create(pparams);
        
        // Define output
        var receiver = new WalletAddress(receiverAddress);
        var output = new PostAlonzoTransactionOutput(
            new ChrysalisAddress(receiver.ToBytes()),
            new Lovelace(amountToSend),
            null,
            null
        );
        
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
        
        // Build and sign transaction
        var unsignedTx = txBuilder.Build();
        var signedTx = unsignedTx.Sign(paymentKey);
        
        // Serialize to CBOR
        return CborSerializer.Serialize(signedTx);
    }

    [Fact]
    public async Task WatchTxForAddress()
    {
        // Arrange
        var receiverAddr = new WalletAddress(RECEIVER_ADDRESS);
        var addressBytes = receiverAddr.ToBytes();
        var addressPredicate = new AddressPredicate(addressBytes, AddressSearchType.ExactAddress);
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        
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
        await Task.Delay(100);
        
        // Submit a transaction to the watched address
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        await _submitClient.SubmitTxAsync([tx]);

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
                output.Address?.ToByteArray().SequenceEqual(addressBytes) == true);
            
            Assert.True(hasAddressInOutputs, "Watched address should be found in transaction outputs");
        }
    }

    [Fact]
    public async Task WatchTxForPaymentPart()
    {
        // Arrange
        var receiverAddr = new WalletAddress(RECEIVER_ADDRESS);
        var paymentCredBytes = receiverAddr.ToBytes().Skip(1).Take(28).ToArray(); // Extract payment credential
        var paymentPredicate = new AddressPredicate(paymentCredBytes, AddressSearchType.PaymentPart);
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        
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
        await Task.Delay(100);
        
        // Submit a transaction
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        await _submitClient.SubmitTxAsync([tx]);

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
        var delegationCredBytes = receiverAddr.ToBytes().Skip(29).Take(28).ToArray(); // Extract delegation credential
        var delegationPredicate = new AddressPredicate(delegationCredBytes, AddressSearchType.DelegationPart);
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        
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
        await Task.Delay(100);
        
        // Submit a transaction
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        await _submitClient.SubmitTxAsync([tx]);

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
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        
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
        await Task.Delay(100);
        
        // Submit a transaction
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        await _submitClient.SubmitTxAsync([tx]);

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
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        
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
        await Task.Delay(100);
        
        // Submit a transaction
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        await _submitClient.SubmitTxAsync([tx]);

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