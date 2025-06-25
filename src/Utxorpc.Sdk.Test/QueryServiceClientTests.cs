using Xunit;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using Utxorpc.V1alpha.Cardano;

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
    public async Task ReadParams()
    {
        // Act
        var response = await _client.ReadParamsAsync(null);

        // Assert basic response structure
        Assert.NotNull(response);
        Assert.NotNull(response.Values);
        Assert.NotNull(response.Values.Params);
        Assert.NotNull(response.LedgerTip);

        // Ensure it's Cardano parameters
        var cardanoParams = (PParams)response.Values.Params;

        // Assert all expected parameter values for Preview testnet
        Assert.Equal(4310u, cardanoParams.CoinsPerUtxoByte);
        Assert.Equal(16384u, cardanoParams.MaxTxSize);
        Assert.Equal(44u, cardanoParams.MinFeeCoefficient);
        Assert.Equal(155381u, cardanoParams.MinFeeConstant);
        Assert.Equal(90112u, cardanoParams.MaxBlockBodySize);
        Assert.Equal(1100u, cardanoParams.MaxBlockHeaderSize);
        Assert.Equal(2000000u, cardanoParams.StakeKeyDeposit);
        Assert.Equal(500000000u, cardanoParams.PoolDeposit);
        Assert.Equal(0u, cardanoParams.PoolRetirementEpochBound);
        Assert.Equal(500u, cardanoParams.DesiredNumberOfPools);
        Assert.Equal(170000000u, cardanoParams.MinPoolCost);
        Assert.Equal(5000u, cardanoParams.MaxValueSize);
        Assert.Equal(150u, cardanoParams.CollateralPercentage);
        Assert.Equal(3u, cardanoParams.MaxCollateralInputs);

        // Protocol version
        Assert.NotNull(cardanoParams.ProtocolVersion);
        Assert.Equal(9u, cardanoParams.ProtocolVersion.Major);
        Assert.Equal(0u, cardanoParams.ProtocolVersion.Minor);

        // Rational parameters
        Assert.NotNull(cardanoParams.PoolInfluence);
        Assert.Equal(5033165, cardanoParams.PoolInfluence.Numerator);
        Assert.Equal(16777216, (int)cardanoParams.PoolInfluence.Denominator);

        Assert.NotNull(cardanoParams.MonetaryExpansion);
        Assert.Equal(6442451, cardanoParams.MonetaryExpansion.Numerator);
        Assert.Equal(2147483648L, cardanoParams.MonetaryExpansion.Denominator);

        Assert.NotNull(cardanoParams.TreasuryExpansion);
        Assert.Equal(13421773, cardanoParams.TreasuryExpansion.Numerator);
        Assert.Equal(67108864, (int)cardanoParams.TreasuryExpansion.Denominator);

        // Prices
        Assert.NotNull(cardanoParams.Prices);
        Assert.NotNull(cardanoParams.Prices.Steps);
        Assert.Equal(721, cardanoParams.Prices.Steps.Numerator);
        Assert.Equal(10000000, (int)cardanoParams.Prices.Steps.Denominator);

        Assert.NotNull(cardanoParams.Prices.Memory);
        Assert.Equal(577, cardanoParams.Prices.Memory.Numerator);
        Assert.Equal(10000, (int)cardanoParams.Prices.Memory.Denominator);

        // Execution units
        Assert.NotNull(cardanoParams.MaxExecutionUnitsPerTransaction);
        Assert.Equal(10000000000u, cardanoParams.MaxExecutionUnitsPerTransaction.Steps);
        Assert.Equal(14000000u, cardanoParams.MaxExecutionUnitsPerTransaction.Memory);

        Assert.NotNull(cardanoParams.MaxExecutionUnitsPerBlock);
        Assert.Equal(20000000000u, cardanoParams.MaxExecutionUnitsPerBlock.Steps);
        Assert.Equal(62000000u, cardanoParams.MaxExecutionUnitsPerBlock.Memory);

        // Script ref cost
        Assert.NotNull(cardanoParams.MinFeeScriptRefCostPerByte);
        Assert.Equal(15, cardanoParams.MinFeeScriptRefCostPerByte.Numerator);
        Assert.Equal(1, (int)cardanoParams.MinFeeScriptRefCostPerByte.Denominator);

        // Governance parameters
        Assert.Equal(365u, cardanoParams.CommitteeTermLimit);
        Assert.Equal(30u, cardanoParams.GovernanceActionValidityPeriod);
        Assert.Equal(100000000000u, cardanoParams.GovernanceActionDeposit);
        Assert.Equal(500000000u, cardanoParams.DrepDeposit);
        Assert.Equal(20u, cardanoParams.DrepInactivityPeriod);

        // Voting thresholds
        Assert.NotNull(cardanoParams.PoolVotingThresholds);
        Assert.NotNull(cardanoParams.PoolVotingThresholds.Thresholds);
        Assert.Equal(5, cardanoParams.PoolVotingThresholds.Thresholds.Count);
        foreach (var threshold in cardanoParams.PoolVotingThresholds.Thresholds)
        {
            Assert.Equal(51, threshold.Numerator);
            Assert.Equal(100u, threshold.Denominator);
        }

        Assert.NotNull(cardanoParams.DrepVotingThresholds);
        Assert.NotNull(cardanoParams.DrepVotingThresholds.Thresholds);
        Assert.Equal(10, cardanoParams.DrepVotingThresholds.Thresholds.Count);

        // Cost models
        Assert.NotNull(cardanoParams.CostModels);
        Assert.NotNull(cardanoParams.CostModels.PlutusV1);
        Assert.NotNull(cardanoParams.CostModels.PlutusV1.Values);
        Assert.Contains(100788u, cardanoParams.CostModels.PlutusV1.Values);
        Assert.Contains(420u, cardanoParams.CostModels.PlutusV1.Values);
        Assert.Contains(1u, cardanoParams.CostModels.PlutusV1.Values);
        Assert.Contains(1000u, cardanoParams.CostModels.PlutusV1.Values);

        Assert.NotNull(cardanoParams.CostModels.PlutusV2);
        Assert.NotNull(cardanoParams.CostModels.PlutusV2.Values);
        Assert.Contains(100788u, cardanoParams.CostModels.PlutusV2.Values);

        Assert.NotNull(cardanoParams.CostModels.PlutusV3);
        Assert.NotNull(cardanoParams.CostModels.PlutusV3.Values);
        Assert.Contains(100788u, cardanoParams.CostModels.PlutusV3.Values);
    }

    [Fact]
    public async Task ReadUtxosByOutputRef()
    {
        // Arrange
        var txHash = Convert.FromHexString("9874bdf4ad47b2d30a2146fc4ba1f94859e58e772683e75001aca6e85de7690d");
        var outputIndex = 0u;
        var txoRef = new TxoRef(txHash, outputIndex);

        // Act
        var utxos = await _client.ReadUtxosAsync([txoRef], null);

        // Assert
        Assert.NotNull(utxos);
        Assert.NotNull(utxos.Items);
        Assert.Single(utxos.Items);
        Assert.NotNull(utxos.LedgerTip);

        var utxo = utxos.Items[0];

        // Verify the UTXO reference matches what we requested
        Assert.NotNull(utxo.TxoRef);
        Assert.Equal(txHash, utxo.TxoRef.Hash);
        Assert.Equal(outputIndex, utxo.TxoRef.Index);

        // Verify native bytes
        Assert.NotNull(utxo.NativeBytes);
        Assert.Equal("82583900729c67d0de8cde3c0afc768fb0fcb1596e8cfcbf781b553efcd228813b7bb577937983e016d4e8429ff48cf386d6818883f9e88b62a804e01a05f5e100",
            Convert.ToHexString(utxo.NativeBytes).ToLower());

        // Verify parsed state
        Assert.NotNull(utxo.ParsedState);
        var cardanoOutput = Assert.IsType<TxOutput>(utxo.ParsedState);
        Assert.NotNull(cardanoOutput.Address);
        Assert.True(cardanoOutput.Coin > 0);
    }

    [Fact]
    public async Task SearchUtxosByAddress()
    {
        // Arrange
        var testAddressHex = "0053fbfffab7b001281917de77f18a8087413be03401db4aa2a7dbf0ae1591d34d5b4b2728d04a80fdd041bb52edb334dacbf25aa27877e738";
        var addressBytes = Convert.FromHexString(testAddressHex);
        var predicate = new AddressPredicate(addressBytes, AddressSearchType.ExactAddress);

        // Act
        var utxos = await _client.SearchUtxosAsync(predicate, maxItems: 10, fieldMask: null);

        // Assert
        Assert.NotNull(utxos);
        Assert.NotNull(utxos.Items);
        Assert.NotNull(utxos.LedgerTip);
        Assert.True(utxos.Items.Count > 0, "Should find at least one UTXO");

        // Verify all returned UTXOs belong to the searched address
        foreach (var utxo in utxos.Items)
        {
            Assert.NotNull(utxo.ParsedState);
            var cardanoOutput = Assert.IsType<TxOutput>(utxo.ParsedState);
            Assert.NotNull(cardanoOutput.Address);

            // Verify the address matches what we searched for
            var utxoAddressBytes = cardanoOutput.Address.ToByteArray();
            Assert.Equal(addressBytes, utxoAddressBytes);
        }
    }

    [Fact]
    public async Task SearchUtxosByPaymentPart()
    {
        // Arrange
        var paymentCredHex = "53fbfffab7b001281917de77f18a8087413be03401db4aa2a7dbf0ae";
        var paymentCredBytes = Convert.FromHexString(paymentCredHex);
        var predicate = new AddressPredicate(paymentCredBytes, AddressSearchType.PaymentPart);

        // Act
        var utxos = await _client.SearchUtxosAsync(predicate, maxItems: 5, fieldMask: null);

        // Assert
        Assert.NotNull(utxos);
        Assert.NotNull(utxos.Items);
        Assert.NotNull(utxos.LedgerTip);
        Assert.True(utxos.Items.Count > 0, "Should find at least one UTXO");

        // Verify all returned UTXOs have the correct payment credential
        foreach (var utxo in utxos.Items)
        {
            Assert.NotNull(utxo.ParsedState);
            var cardanoOutput = Assert.IsType<TxOutput>(utxo.ParsedState);
            Assert.NotNull(cardanoOutput.Address);

            var utxoAddressBytes = cardanoOutput.Address.ToByteArray();
            // Payment credential is bytes 1-28 (after network byte)
            var utxoPaymentCred = utxoAddressBytes.Skip(1).Take(28).ToArray();

            Assert.Equal(paymentCredBytes, utxoPaymentCred);
        }
    }

    [Fact]
    public async Task SearchUtxosByDelegationPart()
    {
        // Arrange
        var delegationCredHex = "1591d34d5b4b2728d04a80fdd041bb52edb334dacbf25aa27877e738";
        var delegationCredBytes = Convert.FromHexString(delegationCredHex);
        var predicate = new AddressPredicate(delegationCredBytes, AddressSearchType.DelegationPart);

        // Act
        var utxos = await _client.SearchUtxosAsync(predicate, maxItems: 5, fieldMask: null);

        // Assert
        Assert.NotNull(utxos);
        Assert.NotNull(utxos.Items);
        Assert.NotNull(utxos.LedgerTip);
        Assert.True(utxos.Items.Count > 0, "Should find at least one UTXO");

        // Verify all returned UTXOs have the correct delegation credential
        foreach (var utxo in utxos.Items)
        {
            Assert.NotNull(utxo.ParsedState);
            var cardanoOutput = Assert.IsType<TxOutput>(utxo.ParsedState);
            Assert.NotNull(cardanoOutput.Address);

            var utxoAddressBytes = cardanoOutput.Address.ToByteArray();
            // Base address should be 57 bytes: 1 (network) + 28 (payment) + 28 (delegation)
            Assert.Equal(57, utxoAddressBytes.Length);

            // Delegation credential is bytes 29-56 (last 28 bytes)
            var utxoDelegationCred = utxoAddressBytes.Skip(29).Take(28).ToArray();

            Assert.Equal(delegationCredBytes, utxoDelegationCred);
        }
    }

    [Fact]
    public async Task SearchUtxosByPolicyID()
    {
        // Arrange
        var policyIdHex = "047e0f912c4260fe66ae271e5ae494dcd5f79635bbbb1386be195f4e";
        var policyIdBytes = Convert.FromHexString(policyIdHex);
        var predicate = new AssetPredicate(policyIdBytes, AssetSearchType.PolicyId);

        // Act
        var utxos = await _client.SearchUtxosAsync(predicate, maxItems: 10, fieldMask: null);

        // Assert
        Assert.NotNull(utxos);
        Assert.NotNull(utxos.Items);
        Assert.NotNull(utxos.LedgerTip);
        Assert.True(utxos.Items.Count > 0, "Should find at least one UTXO");

        // Verify all returned UTXOs contain the searched policy ID
        foreach (var utxo in utxos.Items)
        {
            Assert.NotNull(utxo.ParsedState);
            var cardanoOutput = Assert.IsType<TxOutput>(utxo.ParsedState);
            Assert.NotNull(cardanoOutput.Assets);
            Assert.True(cardanoOutput.Assets.Count > 0, "UTXO should contain assets");

            // Check that at least one asset group has the expected policy ID
            var hasExpectedPolicy = cardanoOutput.Assets.Any(assetGroup =>
                assetGroup.PolicyId.Span.SequenceEqual(policyIdBytes));

            Assert.True(hasExpectedPolicy,
                "UTXO should contain at least one asset with the searched policy ID");
        }
    }

    [Fact]
    public async Task SearchUtxosByAsset()
    {
        // Arrange
        var policyIdHex = "047e0f912c4260fe66ae271e5ae494dcd5f79635bbbb1386be195f4e";
        var assetNameHex = "414c4c45594b41545a3030303630"; // "ALLEYKATZ00060"
        var assetBytes = Convert.FromHexString(policyIdHex + assetNameHex);
        var predicate = new AssetPredicate(assetBytes, AssetSearchType.AssetName);

        // Act
        var utxos = await _client.SearchUtxosAsync(predicate, maxItems: 10, fieldMask: null);

        // Assert
        Assert.NotNull(utxos);
        Assert.NotNull(utxos.Items);
        Assert.NotNull(utxos.LedgerTip);
        Assert.True(utxos.Items.Count > 0, "Should find at least one UTXO");

        // Parse the expected values
        var expectedPolicyId = Convert.FromHexString(policyIdHex);
        var expectedAssetName = Convert.FromHexString(assetNameHex);

        // Verify all returned UTXOs contain the exact asset
        foreach (var utxo in utxos.Items)
        {
            Assert.NotNull(utxo.ParsedState);
            var cardanoOutput = Assert.IsType<TxOutput>(utxo.ParsedState);
            Assert.NotNull(cardanoOutput.Assets);

            // Find the asset group with our policy ID
            var assetGroup = cardanoOutput.Assets.FirstOrDefault(ag =>
                ag.PolicyId.Span.SequenceEqual(expectedPolicyId));

            Assert.NotNull(assetGroup);
            Assert.NotNull(assetGroup.Assets);

            // Check that this group contains our asset name
            var hasExpectedAsset = assetGroup.Assets.Any(asset =>
                asset.Name.Span.SequenceEqual(expectedAssetName));

            Assert.True(hasExpectedAsset,
                "UTXO should contain the exact asset (policy ID + name) that was searched");
        }
    }
}