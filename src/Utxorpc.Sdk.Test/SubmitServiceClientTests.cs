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

public class SubmitServiceClientTests
{
    private const string DOLOS_URL = "http://localhost:50051";
    private const string TEST_MNEMONIC = "february next piano since banana hurdle tide soda reward hood luggage bronze polar veteran fold doctor melt usual rose coral mask interest army clump";
    private const string RECEIVER_ADDRESS = "addr_test1qpflhll6k7cqz2qezl080uv2szr5zwlqxsqakj4z5ldlpts4j8f56k6tyu5dqj5qlhgyrw6jakenfkkt7fd2y7rhuuuquqeeh5";
    private const string BLOCKFROST_API_KEY = "previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC";
    private const ulong AMOUNT_TO_SEND = 6_000_000UL; // 6 ADA
    
    private readonly SubmitServiceClient _client;

    public SubmitServiceClientTests()
    {
        _client = new SubmitServiceClient(DOLOS_URL);
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
        var header = new AddressHeader(AddressType.Base, NetworkType.Testnet);
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
    public async Task SubmitTx()
    {
        // Arrange
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        
        // Act
        var response = await _client.SubmitTxAsync([tx]);
        
        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Refs);
        Assert.Single(response.Refs);
        Assert.Equal(32, response.Refs[0].Length);
    }

    [Fact]
    public async Task WaitForTx()
    {
        // Arrange - First submit a transaction to wait for
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        
        // Submit the transaction first
        var submitResponse = await _client.SubmitTxAsync([tx]);
        var txHash = submitResponse.Refs[0];
        
        var txoRef = new TxoRef(txHash, null);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // 60 seconds timeout

        // Act
        var stages = new List<WaitForTxResponse>();
        await foreach (var stage in _client.WaitForTxAsync([txoRef], cts.Token))
        {
            stages.Add(stage);
            if (stages.Count >= 2) break;
        }

        // Assert
        Assert.True(stages.Count >= 1);
        
        var firstStage = stages[0];
        Assert.NotNull(firstStage);
        Assert.NotNull(firstStage.Ref);
        Assert.Equal(txHash, firstStage.Ref);
        Assert.True(firstStage.Stage >= Stage.Acknowledged);
    }

    [Fact]
    public async Task WatchMempoolByAddress()
    {
        // Arrange - Submit a transaction to watch for
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        
        // Get the receiver address bytes for watching
        var receiverAddr = new WalletAddress(RECEIVER_ADDRESS);
        var addressPredicate = new AddressPredicate(receiverAddr.ToBytes(), AddressSearchType.ExactAddress);
        
        // Start watching for the address BEFORE submitting
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchMempoolResponse>();
            await foreach (var mempoolEvent in _client.WatchMempoolAsync(addressPredicate, fieldMask: null, cts.Token))
            {
                events.Add(mempoolEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });
        
        // Give the watcher time to start
        await Task.Delay(1000);
        
        // Submit the transaction
        var submitResponse = await _client.SubmitTxAsync([tx]);

        // Act - Wait for the watcher to detect the transaction
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent.Tx);
        Assert.NotNull(firstEvent.Tx?.Ref);
        Assert.True(firstEvent.Tx?.Stage >= Stage.Acknowledged);
    }

    [Fact]
    public async Task WatchMempoolByPaymentPart()
    {
        // Arrange - Submit a transaction to watch for
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        
        // Get the payment credential from receiver address
        var receiverAddr = new WalletAddress(RECEIVER_ADDRESS);
        var paymentCredBytes = receiverAddr.ToBytes().Skip(1).Take(28).ToArray(); // Extract payment credential
        var paymentPredicate = new AddressPredicate(paymentCredBytes, AddressSearchType.PaymentPart);
        
        // Start watching for the payment credential BEFORE submitting
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchMempoolResponse>();
            await foreach (var mempoolEvent in _client.WatchMempoolAsync(paymentPredicate, fieldMask: null, cts.Token))
            {
                events.Add(mempoolEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });
        
        // Give the watcher time to start
        await Task.Delay(1000);
        
        // Submit the transaction
        var submitResponse = await _client.SubmitTxAsync([tx]);

        // Act - Wait for the watcher to detect the transaction
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent.Tx);
        Assert.NotNull(firstEvent.Tx?.Ref);
        Assert.True(firstEvent.Tx?.Stage >= Stage.Acknowledged);
    }

    [Fact]
    public async Task WatchMempoolByDelegationPart()
    {
        // Arrange - Submit a transaction to watch for
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        
        // Get the delegation credential from receiver address
        var receiverAddr = new WalletAddress(RECEIVER_ADDRESS);
        var delegationCredBytes = receiverAddr.ToBytes().Skip(29).Take(28).ToArray(); // Extract delegation credential
        var delegationPredicate = new AddressPredicate(delegationCredBytes, AddressSearchType.DelegationPart);
        
        // Start watching for the delegation credential BEFORE submitting
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchMempoolResponse>();
            await foreach (var mempoolEvent in _client.WatchMempoolAsync(delegationPredicate, fieldMask: null, cts.Token))
            {
                events.Add(mempoolEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });
        
        // Give the watcher time to start
        await Task.Delay(1000);
        
        // Submit the transaction
        var submitResponse = await _client.SubmitTxAsync([tx]);

        // Act - Wait for the watcher to detect the transaction
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent.Tx);
        Assert.NotNull(firstEvent.Tx?.Ref);
        Assert.True(firstEvent.Tx?.Stage >= Stage.Acknowledged);
    }

    [Fact]
    public async Task WatchMempoolByPolicyId()
    {
        // Arrange
        var policyIdHex = "8b05e87a51c1d4a0fa888d2bb14dbc25e8c343ea379a171b63aa84a0";
        var policyIdBytes = Convert.FromHexString(policyIdHex);
        var assetPredicate = new AssetPredicate(policyIdBytes, AssetSearchType.PolicyId);
        
        // Start watching for the policy ID
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchMempoolResponse>();
            await foreach (var mempoolEvent in _client.WatchMempoolAsync(assetPredicate, fieldMask: null, cts.Token))
            {
                events.Add(mempoolEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });

        // Give the watcher time to start
        await Task.Delay(100);
        
        // Submit a transaction
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        await _client.SubmitTxAsync([tx]);

        // Act - Wait for events
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent.Tx);
        Assert.NotNull(firstEvent.Tx?.Ref);
        Assert.True(firstEvent.Tx?.Stage >= Stage.Acknowledged);
    }

    [Fact]
    public async Task WatchMempoolByAsset()
    {
        // Arrange
        var assetHex = "8b05e87a51c1d4a0fa888d2bb14dbc25e8c343ea379a171b63aa84a0434e4354";
        var assetBytes = Convert.FromHexString(assetHex);
        var assetPredicate = new AssetPredicate(assetBytes, AssetSearchType.AssetName);
        
        // Start watching for the asset
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watchTask = Task.Run(async () =>
        {
            var events = new List<WatchMempoolResponse>();
            await foreach (var mempoolEvent in _client.WatchMempoolAsync(assetPredicate, fieldMask: null, cts.Token))
            {
                events.Add(mempoolEvent);
                if (events.Count >= 1) break;
            }
            return events;
        });

        // Give the watcher time to start
        await Task.Delay(100);
        
        // Submit a transaction
        var txCbor = await BuildTransactionAsync(TEST_MNEMONIC, AMOUNT_TO_SEND, RECEIVER_ADDRESS);
        var tx = new Tx(txCbor);
        await _client.SubmitTxAsync([tx]);

        // Act - Wait for events
        var events = await watchTask;

        // Assert
        Assert.True(events.Count > 0, "Should find at least one transaction event");
        var firstEvent = events[0];
        Assert.NotNull(firstEvent.Tx);
        Assert.NotNull(firstEvent.Tx?.Ref);
        Assert.True(firstEvent.Tx?.Stage >= Stage.Acknowledged);
    }
}