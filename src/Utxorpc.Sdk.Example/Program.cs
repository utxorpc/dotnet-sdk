using Utxorpc.Sdk;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;

// Configuration
const string SERVER_URL = "http://localhost:50051";

// Check for command line arguments
if (args.Length > 0)
{
    var command = args[0].ToLower();
    
    try
    {
        switch (command)
        {
            // Query Service
            case "readutxos":
                // Check if it's multiple UTXOs (contains colon) or single UTXO
                if (args[1].Contains(':') || (args.Length > 2 && args[2].Contains(':')))
                {
                    // Multiple UTXOs format
                    await TestQueryUtxosMulti(args.Skip(1).ToArray());
                }
                else
                {
                    // Single UTXO format
                    uint index = args.Length >= 3 && uint.TryParse(args[2], out var idx) ? idx : 0;
                    await TestQueryUtxos(args[1], index);
                }
                break;
                
            case "readparams":
                await TestQueryParams();
                break;
                
            case "searchutxos":
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: searchutxos <type> <value-hex>");
                    Console.WriteLine("Types: exact, payment, delegation, asset, policy");
                    Console.WriteLine("Example: searchutxos exact 0053FBFFFAB7B001281917DE77F18A8087413BE03401DB4AA2A7DBF0AE1591D34D5B4B2728D04A80FDD041BB52EDB334DACBF25AA27877E738");
                    return;
                }
                await TestSearchUtxos(args[1], args[2]);
                break;
                
            // Submit Service
            case "submittx":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: dotnet run -- submittx <tx-cbor-hex>");
                    return;
                }
                await TestSubmitTx(args[1]);
                break;
                
            case "waitfortx":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: dotnet run -- waitfortx <tx-hash-hex>");
                    return;
                }
                await TestWaitForTx(args[1]);
                break;
                
            case "watchmempool":
                await TestWatchMempool(args[1], args[2]);
                break;
                
            // Watch Service
            case "watchtx":
                await TestWatchTx(args[1], args[2]);
                break;
                
            // Sync Service
            case "readtip":
                await TestReadTip();
                break;
                
            case "fetchblock":
                await TestFetchBlock(uint.Parse(args[1]), args[2]);
                break;
                
            case "dumphistory":
                if (args.Length >= 4)
                {
                    // Format: dumphistory <index> <hash> <count>
                    await TestDumpHistory(ulong.Parse(args[1]), args[2], int.Parse(args[3]));
                }
                else if (args.Length >= 2)
                {
                    // Format: dumphistory <count>
                    await TestDumpHistory(null, null, int.Parse(args[1]));
                }
                break;
                
            case "followtip":
                if (args.Length >= 3)
                {
                    // Format: followtip <block-index> <block-hash-base64>
                    await TestFollowTip(ulong.Parse(args[1]), args[2]);
                }
                else
                {
                    // Follow from current tip
                    await TestFollowTip(null, null);
                }
                break;
            default:
                Console.WriteLine($"Unknown command: {command}");
                Console.WriteLine("\nAvailable commands:");
                Console.WriteLine("\n=== Query Service ===");
                Console.WriteLine("  readutxos <hash-hex> [index]      - Query UTxOs by transaction");
                Console.WriteLine("  readparams                        - Query chain parameters");
                Console.WriteLine("  searchutxos <type> <value-hex>    - Search UTxOs (types: exact, payment, delegation, asset, policy)");
                Console.WriteLine("\n=== Submit Service ===");
                Console.WriteLine("  submittx <cbor-hex>               - Submit a transaction");
                Console.WriteLine("  waitfortx <hash-hex>              - Wait for transaction confirmation");
                Console.WriteLine("  watchmempool <type> <value-hex>   - Watch mempool (address/payment/delegation/asset/policy)");
                Console.WriteLine("\n=== Watch Service ===");
                Console.WriteLine("  watchtx <type> <value-hex>        - Watch transactions (exact/payment/delegation/asset/policy)");
                Console.WriteLine("\n=== Sync Service ===");
                Console.WriteLine("  readtip                           - Read current chain tip");
                Console.WriteLine("  fetchblock <index> <hash-hex>     - Fetch specific block");
                Console.WriteLine("  dumphistory [count]               - Dump recent blocks");
                Console.WriteLine("  followtip [index hash-hex]        - Follow chain tip updates");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nError: {ex.Message}");
        if (ex is Grpc.Core.RpcException rpcEx)
        {
            Console.WriteLine($"gRPC Status: {rpcEx.StatusCode}");
        }
    }
}

// Direct SDK usage methods
async Task TestQueryUtxos(string txHashHex, uint index = 0)
{
    Console.WriteLine($"Querying UTxOs for tx: {txHashHex}, index: {index}");
    
    var client = new QueryServiceClient(SERVER_URL);
    var txoRefs = new[]
    {
        new TxoRef(
            Hash: Convert.FromHexString(txHashHex),
            Index: index
        )
    };
    
    var response = await client.ReadUtxosAsync(txoRefs, null);
    
    Console.WriteLine($"Found {response.Items.Count} UTxO(s)");
    
    int utxoIndex = 0;
    foreach (var utxo in response.Items)
    {
        utxoIndex++;
        Console.WriteLine($"\n=== UTXO #{utxoIndex} ===");
        
        if (utxo.TxoRef != null)
        {
            Console.WriteLine($"Transaction Reference:");
            Console.WriteLine($"  Hash (Base64): {Convert.ToBase64String(utxo.TxoRef.Hash ?? Array.Empty<byte>())}");
            Console.WriteLine($"  Hash (Hex): {Convert.ToHexString(utxo.TxoRef.Hash ?? Array.Empty<byte>())}");
            Console.WriteLine($"  Output Index: {utxo.TxoRef.Index}");
        }
        
        Console.WriteLine($"\nNative Bytes: {utxo.NativeBytes?.Length ?? 0} bytes");
        
        if (utxo.ParsedState != null)
        {
            Console.WriteLine($"\nParsed State: Available");
            var type = utxo.ParsedState.GetType();
            Console.WriteLine($"  Type: {type.FullName}");
            
            // Check if it's Cardano TxOutput
            if (utxo.ParsedState is Utxorpc.V1alpha.Cardano.TxOutput cardanoOutput)
            {
                Console.WriteLine("\nCardano UTXO Details:");
                
                // Address
                if (cardanoOutput.Address != null)
                {
                    var addressBytes = cardanoOutput.Address.ToByteArray();
                    Console.WriteLine($"  Address:");
                    Console.WriteLine($"    Base64: {Convert.ToBase64String(addressBytes)}");
                    Console.WriteLine($"    Hex: {Convert.ToHexString(addressBytes)}");
                }
                
                // Coin (ADA value)
                Console.WriteLine($"  Coin: {cardanoOutput.Coin.Int} lovelace ({cardanoOutput.Coin.Int / 1_000_000.0:F6} ADA)");
                
                // Assets
                if (cardanoOutput.Assets != null && cardanoOutput.Assets.Count > 0)
                {
                    Console.WriteLine($"  Assets: {cardanoOutput.Assets.Count} policy ID(s)");
                    int policyIndex = 0;
                    foreach (var policyGroup in cardanoOutput.Assets)
                    {
                        policyIndex++;
                        Console.WriteLine($"    Policy #{policyIndex}:");
                        Console.WriteLine($"      PolicyId (Base64): {Convert.ToBase64String(policyGroup.PolicyId.ToByteArray())}");
                        Console.WriteLine($"      PolicyId (Hex): {Convert.ToHexString(policyGroup.PolicyId.ToByteArray())}");
                        
                        if (policyGroup.Assets != null && policyGroup.Assets.Count > 0)
                        {
                            Console.WriteLine($"      Assets under this policy: {policyGroup.Assets.Count}");
                            foreach (var asset in policyGroup.Assets)
                            {
                                var nameBytes = asset.Name.ToByteArray();
                                Console.WriteLine($"        Asset:");
                                Console.WriteLine($"          Name (Base64): {Convert.ToBase64String(nameBytes)}");
                                Console.WriteLine($"          Name (Hex): {Convert.ToHexString(nameBytes)}");
                                try
                                {
                                    Console.WriteLine($"          Name (Text): {System.Text.Encoding.UTF8.GetString(nameBytes)}");
                                }
                                catch { }
                                Console.WriteLine($"          Amount: {asset.OutputCoin}");
                            }
                        }
                    }
                }
                
                // Datum
                if (cardanoOutput.Datum != null)
                {
                    Console.WriteLine("  Datum:");
                    if (cardanoOutput.Datum.Hash != null)
                    {
                        Console.WriteLine($"    Hash: {Convert.ToBase64String(cardanoOutput.Datum.Hash.ToByteArray())}");
                        Console.WriteLine($"    Hash (Hex): {Convert.ToHexString(cardanoOutput.Datum.Hash.ToByteArray())}");
                    }
                    if (cardanoOutput.Datum.OriginalCbor != null)
                    {
                        var cborBytes = cardanoOutput.Datum.OriginalCbor.ToByteArray();
                        Console.WriteLine($"    Original CBOR: {Convert.ToBase64String(cborBytes)}");
                        Console.WriteLine($"    CBOR Size: {cborBytes.Length} bytes");
                    }
                    if (cardanoOutput.Datum.Payload != null)
                    {
                        Console.WriteLine($"    Payload Type: {cardanoOutput.Datum.Payload.GetType().Name}");
                        // You could add more detailed payload parsing here
                    }
                }
                
                // Script Reference
                if (cardanoOutput.Script != null)
                {
                    Console.WriteLine("  Script Reference:");
                    var scriptType = cardanoOutput.Script.GetType().Name;
                    Console.WriteLine($"    Type: {scriptType}");
                }
            }
            else
            {
                // Fallback to reflection for non-Cardano chains
                Console.WriteLine("\nUsing reflection to display all properties:");
                var properties = type.GetProperties();
                foreach (var prop in properties)
                {
                    try
                    {
                        var value = prop.GetValue(utxo.ParsedState);
                        if (value != null)
                        {
                            if (value is Google.Protobuf.ByteString byteString)
                            {
                                var bytes = byteString.ToByteArray();
                                Console.WriteLine($"  {prop.Name}: {Convert.ToBase64String(bytes)} (hex: {Convert.ToHexString(bytes)}");
                            }
                            else if (value is System.Collections.IEnumerable enumerable && !(value is string))
                            {
                                var count = enumerable.Cast<object>().Count();
                                Console.WriteLine($"  {prop.Name}: {count} item(s)");
                            }
                            else
                            {
                                Console.WriteLine($"  {prop.Name}: {value}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  {prop.Name}: <error reading: {ex.Message}>");
                    }
                }
            }
        }
        else
        {
            Console.WriteLine($"\nParsed State: Not available");
        }
    }
    
    if (response.LedgerTip != null)
    {
        Console.WriteLine($"\n=== Ledger Tip ===");
        Console.WriteLine($"  Slot: {response.LedgerTip.Slot}");
        Console.WriteLine($"  Hash: {Convert.ToHexString(response.LedgerTip.Hash ?? [])}");
    }
}

async Task TestQueryUtxosMulti(string[] utxoSpecs)
{
    Console.WriteLine($"Querying {utxoSpecs.Length} UTxO(s)...");
    
    var client = new QueryServiceClient(SERVER_URL);
    var txoRefs = new List<TxoRef>();
    
    foreach (var spec in utxoSpecs)
    {
        var parts = spec.Split(':');
        if (parts.Length != 2)
        {
            Console.WriteLine($"Invalid UTXO format: {spec}. Expected format: <tx-hash-hex>:<index>");
            continue;
        }
        
        if (!uint.TryParse(parts[1], out var index))
        {
            Console.WriteLine($"Invalid index in: {spec}. Index must be a number.");
            continue;
        }
        
        try
        {
            var txHash = Convert.FromHexString(parts[0]);
            txoRefs.Add(new TxoRef(txHash, index));
        }
        catch (FormatException)
        {
            Console.WriteLine($"Invalid hex hash in: {spec}");
            continue;
        }
    }
    
    if (txoRefs.Count == 0)
    {
        Console.WriteLine("No valid UTxOs to query.");
        return;
    }
    
    var response = await client.ReadUtxosAsync(txoRefs.ToArray(), null);
    
    Console.WriteLine($"\nFound {response.Items.Count} UTxO(s)");
    
    int utxoIndex = 0;
    foreach (var utxo in response.Items)
    {
        utxoIndex++;
        Console.WriteLine($"\n--- UTXO #{utxoIndex} ---");
        
        // TxoRef
        if (utxo.TxoRef != null)
        {
            Console.WriteLine($"TxoRef: {Convert.ToHexString(utxo.TxoRef.Hash ?? [])}#{utxo.TxoRef.Index}");
        }
        
        Console.WriteLine($"\nNative Bytes: {utxo.NativeBytes?.Length ?? 0} bytes");
        
        if (utxo.ParsedState != null)
        {
            Console.WriteLine($"\nParsed State: Available");
            var type = utxo.ParsedState.GetType();
            Console.WriteLine($"  Type: {type.FullName}");
            
            // Check if it's Cardano TxOutput
            if (utxo.ParsedState is Utxorpc.V1alpha.Cardano.TxOutput cardanoOutput)
            {
                Console.WriteLine("\nCardano UTXO Details:");
                
                // Address
                if (cardanoOutput.Address != null)
                {
                    var addressBytes = cardanoOutput.Address.ToByteArray();
                    Console.WriteLine($"  Address:");
                    Console.WriteLine($"    Base64: {Convert.ToBase64String(addressBytes)}");
                    Console.WriteLine($"    Hex: {Convert.ToHexString(addressBytes)}");
                }
                
                // Coin (ADA value)
                Console.WriteLine($"  Coin: {cardanoOutput.Coin.Int} lovelace ({cardanoOutput.Coin.Int / 1_000_000.0:F6} ADA)");
                
                // Assets
                if (cardanoOutput.Assets != null && cardanoOutput.Assets.Count > 0)
                {
                    Console.WriteLine($"  Assets: {cardanoOutput.Assets.Count} policy ID(s)");
                    int policyIndex = 0;
                    foreach (var policyGroup in cardanoOutput.Assets)
                    {
                        policyIndex++;
                        Console.WriteLine($"    Policy #{policyIndex}:");
                        Console.WriteLine($"      PolicyId (Base64): {Convert.ToBase64String(policyGroup.PolicyId.ToByteArray())}");
                        Console.WriteLine($"      PolicyId (Hex): {Convert.ToHexString(policyGroup.PolicyId.ToByteArray())}");
                        
                        if (policyGroup.Assets != null && policyGroup.Assets.Count > 0)
                        {
                            Console.WriteLine($"      Assets under this policy: {policyGroup.Assets.Count}");
                            foreach (var asset in policyGroup.Assets)
                            {
                                var nameBytes = asset.Name.ToByteArray();
                                Console.WriteLine($"        Asset:");
                                Console.WriteLine($"          Name (Base64): {Convert.ToBase64String(nameBytes)}");
                                Console.WriteLine($"          Name (Hex): {Convert.ToHexString(nameBytes)}");
                                try
                                {
                                    Console.WriteLine($"          Name (Text): {System.Text.Encoding.UTF8.GetString(nameBytes)}");
                                }
                                catch { }
                                Console.WriteLine($"          Amount: {asset.OutputCoin}");
                            }
                        }
                    }
                }
                
                // Datum
                if (cardanoOutput.Datum != null)
                {
                    Console.WriteLine("  Datum:");
                    if (cardanoOutput.Datum.Hash != null)
                    {
                        Console.WriteLine($"    Hash: {Convert.ToBase64String(cardanoOutput.Datum.Hash.ToByteArray())}");
                        Console.WriteLine($"    Hash (Hex): {Convert.ToHexString(cardanoOutput.Datum.Hash.ToByteArray())}");
                    }
                    if (cardanoOutput.Datum.OriginalCbor != null)
                    {
                        var cborBytes = cardanoOutput.Datum.OriginalCbor.ToByteArray();
                        Console.WriteLine($"    Original CBOR: {Convert.ToBase64String(cborBytes)}");
                        Console.WriteLine($"    CBOR Size: {cborBytes.Length} bytes");
                    }
                    if (cardanoOutput.Datum.Payload != null)
                    {
                        Console.WriteLine($"    Payload Type: {cardanoOutput.Datum.Payload.GetType().Name}");
                        // You could add more detailed payload parsing here
                    }
                }
                
                // Script Reference
                if (cardanoOutput.Script != null)
                {
                    Console.WriteLine("  Script Reference:");
                    var scriptType = cardanoOutput.Script.GetType().Name;
                    Console.WriteLine($"    Type: {scriptType}");
                    // You could add more script details based on the type
                }
            }
            else
            {
                // For non-Cardano or unknown types, use reflection
                Console.WriteLine("\nGeneric UTXO Details (via reflection):");
                var type2 = utxo.ParsedState.GetType();
                foreach (var prop in type2.GetProperties())
                {
                    try
                    {
                        var value = prop.GetValue(utxo.ParsedState);
                        if (value != null)
                        {
                            // Handle byte arrays specially
                            if (value is byte[] bytes)
                            {
                                Console.WriteLine($"  {prop.Name}: {Convert.ToBase64String(bytes)} (Base64)");
                                Console.WriteLine($"  {prop.Name}: {Convert.ToHexString(bytes)} (Hex)");
                            }
                            else
                            {
                                Console.WriteLine($"  {prop.Name}: {value}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  {prop.Name}: <error reading: {ex.Message}>");
                    }
                }
            }
        }
        else
        {
            Console.WriteLine($"\nParsed State: Not available");
        }
    }
    
    if (response.LedgerTip != null)
    {
        Console.WriteLine($"\n=== Ledger Tip ===");
        Console.WriteLine($"  Slot: {response.LedgerTip.Slot}");
        Console.WriteLine($"  Hash: {Convert.ToHexString(response.LedgerTip.Hash ?? [])}");
    }
}

async Task TestQueryParams()
{
    Console.WriteLine("Querying chain parameters...");
    
    var client = new QueryServiceClient(SERVER_URL);
    var response = await client.ReadParamsAsync(null);
    
    if (response.Values?.Params != null)
    {
        Console.WriteLine("\nChain parameters retrieved successfully");
        Console.WriteLine($"Type: {response.Values.Params.GetType().FullName}");
        
        // Check if it's Cardano parameters
        if (response.Values.Params is Utxorpc.V1alpha.Cardano.PParams cardanoParams)
        {
            Console.WriteLine("\n=== Cardano Protocol Parameters ===");
            
            // Basic fees
            Console.WriteLine("\nFee Parameters:");
            Console.WriteLine($"  Min Fee Coefficient: {cardanoParams.MinFeeCoefficient}");
            Console.WriteLine($"  Min Fee Constant: {cardanoParams.MinFeeConstant}");
            Console.WriteLine($"  Coins Per UTxO Byte: {cardanoParams.CoinsPerUtxoByte}");
            
            // Size limits
            Console.WriteLine("\nSize Limits:");
            Console.WriteLine($"  Max Tx Size: {cardanoParams.MaxTxSize}");
            Console.WriteLine($"  Max Block Body Size: {cardanoParams.MaxBlockBodySize}");
            Console.WriteLine($"  Max Block Header Size: {cardanoParams.MaxBlockHeaderSize}");
            Console.WriteLine($"  Max Value Size: {cardanoParams.MaxValueSize}");
            
            // Stake parameters
            Console.WriteLine("\nStake Parameters:");
            Console.WriteLine($"  Stake Key Deposit: {cardanoParams.StakeKeyDeposit.Int / 1_000_000.0:F2} ADA");
            Console.WriteLine($"  Pool Deposit: {cardanoParams.PoolDeposit.Int / 1_000_000.0:F2} ADA");
            Console.WriteLine($"  Min Pool Cost: {cardanoParams.MinPoolCost.Int / 1_000_000.0:F2} ADA");
            Console.WriteLine($"  Desired Number of Pools: {cardanoParams.DesiredNumberOfPools}");
            
            // Collateral
            Console.WriteLine("\nCollateral Parameters:");
            Console.WriteLine($"  Collateral Percentage: {cardanoParams.CollateralPercentage}%");
            Console.WriteLine($"  Max Collateral Inputs: {cardanoParams.MaxCollateralInputs}");
            
            // Protocol version
            if (cardanoParams.ProtocolVersion != null)
            {
                Console.WriteLine($"\nProtocol Version: {cardanoParams.ProtocolVersion.Major}.{cardanoParams.ProtocolVersion.Minor}");
            }
            
            // Execution units
            if (cardanoParams.MaxExecutionUnitsPerTransaction != null)
            {
                Console.WriteLine("\nExecution Units Per Transaction:");
                Console.WriteLine($"  Steps: {cardanoParams.MaxExecutionUnitsPerTransaction.Steps}");
                Console.WriteLine($"  Memory: {cardanoParams.MaxExecutionUnitsPerTransaction.Memory}");
            }
            
            if (cardanoParams.MaxExecutionUnitsPerBlock != null)
            {
                Console.WriteLine("\nExecution Units Per Block:");
                Console.WriteLine($"  Steps: {cardanoParams.MaxExecutionUnitsPerBlock.Steps}");
                Console.WriteLine($"  Memory: {cardanoParams.MaxExecutionUnitsPerBlock.Memory}");
            }
            
            // Script costs
            if (cardanoParams.Prices != null)
            {
                Console.WriteLine("\nScript Execution Prices:");
                Console.WriteLine($"  Steps: {cardanoParams.Prices.Steps?.Numerator}/{cardanoParams.Prices.Steps?.Denominator}");
                Console.WriteLine($"  Memory: {cardanoParams.Prices.Memory?.Numerator}/{cardanoParams.Prices.Memory?.Denominator}");
            }
            
            // Cost models
            if (cardanoParams.CostModels != null)
            {
                Console.WriteLine("\nCost Models:");
                if (cardanoParams.CostModels.PlutusV1 != null)
                    Console.WriteLine($"  Plutus V1: {cardanoParams.CostModels.PlutusV1.Values.Count} parameters");
                if (cardanoParams.CostModels.PlutusV2 != null)
                    Console.WriteLine($"  Plutus V2: {cardanoParams.CostModels.PlutusV2.Values.Count} parameters");
                if (cardanoParams.CostModels.PlutusV3 != null)
                    Console.WriteLine($"  Plutus V3: {cardanoParams.CostModels.PlutusV3.Values.Count} parameters");
            }
            
            // Governance (Conway era)
            Console.WriteLine("\nGovernance Parameters:");
            Console.WriteLine($"  Committee Term Limit: {cardanoParams.CommitteeTermLimit} epochs");
            Console.WriteLine($"  Governance Action Validity Period: {cardanoParams.GovernanceActionValidityPeriod} epochs");
            Console.WriteLine($"  Governance Action Deposit: {cardanoParams.GovernanceActionDeposit.Int / 1_000_000.0:F2} ADA");
            Console.WriteLine($"  DRep Deposit: {cardanoParams.DrepDeposit.Int / 1_000_000.0:F2} ADA");
            Console.WriteLine($"  DRep Inactivity Period: {cardanoParams.DrepInactivityPeriod} epochs");
            
            // Voting thresholds
            if (cardanoParams.PoolVotingThresholds != null && cardanoParams.PoolVotingThresholds.Thresholds.Count > 0)
            {
                Console.WriteLine("\nPool Voting Thresholds:");
                foreach (var threshold in cardanoParams.PoolVotingThresholds.Thresholds)
                {
                    Console.WriteLine($"  {threshold.Numerator}/{threshold.Denominator}");
                }
            }
        }
        else
        {
            // Fallback to reflection for non-Cardano chains
            Console.WriteLine("\nUsing reflection to display parameters:");
            var type = response.Values.Params.GetType();
            foreach (var prop in type.GetProperties())
            {
                try
                {
                    var value = prop.GetValue(response.Values.Params);
                    if (value != null)
                    {
                        Console.WriteLine($"  {prop.Name}: {value}");
                    }
                }
                catch { }
            }
        }
    }
    else
    {
        Console.WriteLine("No parameters returned in response");
    }
    
    if (response.LedgerTip != null)
    {
        Console.WriteLine($"\n=== Ledger Tip ===");
        Console.WriteLine($"  Slot: {response.LedgerTip.Slot}");
        Console.WriteLine($"  Hash (Base64): {Convert.ToBase64String(response.LedgerTip.Hash ?? Array.Empty<byte>())}");
        Console.WriteLine($"  Hash (Hex): {Convert.ToHexString(response.LedgerTip.Hash ?? Array.Empty<byte>())}");
    }
}

async Task TestSearchUtxos(string searchType, string valueHex)
{
    Console.WriteLine($"Searching UTxOs by {searchType}: {valueHex}");
    
    var client = new QueryServiceClient(SERVER_URL);
    var valueBytes = Convert.FromHexString(valueHex);
    
    Predicate predicate;
    
    switch (searchType.ToLower())
    {
        case "exact":
            predicate = new AddressPredicate(valueBytes, AddressSearchType.ExactAddress);
            break;
            
        case "payment":
            predicate = new AddressPredicate(valueBytes, AddressSearchType.PaymentPart);
            break;
            
        case "delegation":
            predicate = new AddressPredicate(valueBytes, AddressSearchType.DelegationPart);
            break;
            
        case "asset":
            predicate = new AssetPredicate(valueBytes, AssetSearchType.AssetName);
            break;
            
        case "policy":
            predicate = new AssetPredicate(valueBytes, AssetSearchType.PolicyId);
            break;
            
        default:
            Console.WriteLine($"Unknown search type: {searchType}");
            Console.WriteLine("Valid types: address, payment, delegation, asset, policy");
            return;
    }
    
    var response = await client.SearchUtxosAsync(predicate, maxItems: 10, fieldMask: null);
    
    Console.WriteLine($"\nFound {response.Items.Count} UTxO(s)");
    
    // Display first few UTXOs
    foreach (var utxo in response.Items.Take(5))
    {
        if (utxo.TxoRef != null)
        {
            Console.WriteLine($"\n  Tx: {Convert.ToHexString(utxo.TxoRef.Hash ?? Array.Empty<byte>()).Substring(0, 32)}...");
            Console.WriteLine($"  Index: {utxo.TxoRef.Index}");
        }
        
        if (utxo.ParsedState is Utxorpc.V1alpha.Cardano.TxOutput cardanoOutput)
        {
            Console.WriteLine($"  Value: {cardanoOutput.Coin.Int / 1_000_000.0:F6} ADA");
            
            // Show assets if searching for assets
            if ((searchType.ToLower() == "asset" || searchType.ToLower() == "policy") && 
                cardanoOutput.Assets != null && cardanoOutput.Assets.Count > 0)
            {
                Console.WriteLine($"  Assets: {cardanoOutput.Assets.Count} policy ID(s)");
            }
        }
    }
    
    if (response.Items.Count > 5)
    {
        Console.WriteLine($"\n... and {response.Items.Count - 5} more UTxO(s)");
    }
    
    // Display ledger tip
    if (response.LedgerTip != null)
    {
        Console.WriteLine($"\n=== Ledger Tip ===");
        Console.WriteLine($"  Slot: {response.LedgerTip.Slot}");
        Console.WriteLine($"  Hash: {Convert.ToHexString(response.LedgerTip.Hash ?? [])}");
    }
    
    if (!string.IsNullOrEmpty(response.NextToken))
    {
        Console.WriteLine($"\nMore results available. Next token: {response.NextToken}");
    }
}

async Task TestSubmitTx(string txCborHex)
{
    Console.WriteLine("Submitting transaction...");
    
    var client = new SubmitServiceClient(SERVER_URL);
    var txBytes = Convert.FromHexString(txCborHex);
    var tx = new Tx(txBytes);
    
    var response = await client.SubmitTxAsync(new[] { tx });
    
    if (response?.Refs != null && response.Refs.Count > 0)
    {
        Console.WriteLine("Transaction submitted successfully!");
        foreach (var txRef in response.Refs)
        {
            Console.WriteLine($"  Tx Hash: {Convert.ToBase64String(txRef)}");
            Console.WriteLine($"  Tx Hash (Hex): {Convert.ToHexString(txRef)}");
        }
    }
}

async Task TestWaitForTx(string txHashHex)
{
    Console.WriteLine($"Waiting for transaction: {txHashHex}");
    
    var client = new SubmitServiceClient(SERVER_URL);
    var txHash = Convert.FromHexString(txHashHex);
    var txoRefs = new[] { new TxoRef(Hash: txHash, Index: null) };
    
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    
    await foreach (var response in client.WaitForTxAsync(txoRefs, cts.Token))
    {
        Console.WriteLine($"Stage: {response.Stage}");
        if (response.Stage == Stage.Confirmed)
        {
            Console.WriteLine("Transaction confirmed!");
            break;
        }
    }
}

async Task TestWatchTx(string searchType, string valueHex)
{
    Console.WriteLine($"Watching transactions for {searchType}: {valueHex}");
    Console.WriteLine("Press Ctrl+C to stop...\n");
    
    var client = new WatchServiceClient(SERVER_URL);
    var valueBytes = Convert.FromHexString(valueHex);
    Predicate predicate = searchType.ToLower() switch
    {
        "exact" => new AddressPredicate(valueBytes, AddressSearchType.ExactAddress),
        "payment" => new AddressPredicate(valueBytes, AddressSearchType.PaymentPart),
        "delegation" => new AddressPredicate(valueBytes, AddressSearchType.DelegationPart),
        "asset" => new AssetPredicate(valueBytes, AssetSearchType.AssetName),
        "policy" => new AssetPredicate(valueBytes, AssetSearchType.PolicyId),
        _ => throw new ArgumentException($"Unknown search type: {searchType}"),
    };
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
    int count = 0;
    
    await foreach (var response in client.WatchTxAsync(predicate, null, null, cts.Token))
    {
        count++;
        Console.WriteLine($"Transaction #{count}:");
        
        if (response?.ParsedState is Utxorpc.V1alpha.Cardano.Tx cardanoTx)
        {
            Console.WriteLine($"  Hash: {Convert.ToBase64String(cardanoTx.Hash?.ToByteArray() ?? Array.Empty<byte>())}");
            Console.WriteLine($"  Inputs: {cardanoTx.Inputs?.Count ?? 0}");
            Console.WriteLine($"  Outputs: {cardanoTx.Outputs?.Count ?? 0}");
            Console.WriteLine($"  Fee: {cardanoTx.Fee}");
            Console.WriteLine($"  Successful: {cardanoTx.Successful}");
        }
        
        if (count >= 5) break;
    }
    
    Console.WriteLine($"\nTotal transactions: {count}");
}

async Task TestWatchMempool(string searchType, string valueHex)
{
    Console.WriteLine($"Watching mempool for {searchType}: {valueHex}");
    Console.WriteLine("Press Ctrl+C to stop...\n");
    
    var client = new SubmitServiceClient(SERVER_URL);
    var valueBytes = Convert.FromHexString(valueHex);
    
    Predicate predicate;
    
    switch (searchType.ToLower())
    {
        case "address":
        case "exact":
            predicate = new AddressPredicate(valueBytes, AddressSearchType.ExactAddress);
            break;
            
        case "payment":
            predicate = new AddressPredicate(valueBytes, AddressSearchType.PaymentPart);
            break;
            
        case "delegation":
            predicate = new AddressPredicate(valueBytes, AddressSearchType.DelegationPart);
            break;
            
        case "asset":
            predicate = new AssetPredicate(valueBytes, AssetSearchType.AssetName);
            break;
            
        case "policy":
            predicate = new AssetPredicate(valueBytes, AssetSearchType.PolicyId);
            break;
            
        default:
            Console.WriteLine($"Unknown search type: {searchType}");
            Console.WriteLine("Valid types: address, payment, delegation, asset, policy");
            return;
    }
    
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    int count = 0;
    
    await foreach (var response in client.WatchMempoolAsync(predicate, null, cts.Token))
    {
        count++;
        Console.WriteLine($"\nMempool event #{count} at {DateTime.Now:HH:mm:ss}:");
        
        if (response?.Tx != null)
        {
            Console.WriteLine($"  Stage: {response.Tx.Stage}");
            Console.WriteLine($"  Native Bytes: {response.Tx.NativeBytes?.Length ?? 0} bytes");
            if (response.Tx.Ref != null)
            {
                Console.WriteLine($"  Tx Hash: {Convert.ToHexString(response.Tx.Ref)}");
            }
            
            // Show parsed transaction details if available
            if (response.Tx.ParsedState != null && response.Tx.ParsedState is AnyUtxoData utxoData)
            {
                if (utxoData.ParsedState is Utxorpc.V1alpha.Cardano.TxOutput cardanoOutput)
                {
                    Console.WriteLine($"  Value: {cardanoOutput.Coin.Int / 1_000_000.0:F6} ADA");
                    
                    // Show assets if watching for assets
                    if ((searchType.ToLower() == "asset" || searchType.ToLower() == "policy") && 
                        cardanoOutput.Assets != null && cardanoOutput.Assets.Count > 0)
                    {
                        Console.WriteLine($"  Assets: {cardanoOutput.Assets.Count} policy ID(s)");
                    }
                }
            }
        }
    }
    
    Console.WriteLine($"\nTotal events: {count}");
}

// Sync Service methods
async Task TestReadTip()
{
    Console.WriteLine("Reading chain tip...");
    
    var client = new SyncServiceClient(SERVER_URL);
    var tip = await client.ReadTipAsync();
    
    if (tip != null)
    {
        Console.WriteLine("\n=== Current Chain Tip ===");
        Console.WriteLine($"  Hash: {tip.Hash}");
        Console.WriteLine($"  Height: {tip.Height}");
    }
    else
    {
        Console.WriteLine("No tip returned");
    }
}

async Task TestFetchBlock(uint blockIndex, string blockHashHex)
{
    Console.WriteLine($"Fetching block at index: {blockIndex}, hash: {blockHashHex}");
    
    var client = new SyncServiceClient(SERVER_URL);
    var blockRef = new BlockRef(blockHashHex, blockIndex);
    
    var block = await client.FetchBlockAsync(blockRef);
    
    if (block != null)
    {
        Console.WriteLine("\n=== Block Details ===");
        Console.WriteLine($"  Hash: {block.Hash}");
        Console.WriteLine($"  Slot: {block.Slot}");
        Console.WriteLine($"  Native Bytes: {block.NativeBytes?.Length ?? 0} bytes");
        
        if (block.NativeBytes != null && block.NativeBytes.Length > 0)
        {
            // Show CBOR structure preview
            var preview = block.NativeBytes.Take(100).ToArray();
            Console.WriteLine($"  CBOR Preview: {Convert.ToHexString(preview)}...");
        }
    }
    else
    {
        Console.WriteLine("Block not found");
    }
}

async Task TestDumpHistory(ulong? startIndex, string? startHashHex, int count)
{
    Console.WriteLine($"Dumping {count} blocks from history...");
    
    var client = new SyncServiceClient(SERVER_URL);
    
    BlockRef? startToken = null;
    if (startIndex.HasValue && !string.IsNullOrEmpty(startHashHex))
    {
        startToken = new BlockRef(startHashHex, startIndex.Value);
        Console.WriteLine($"Starting from block index: {startIndex}, hash: {startHashHex}");
    }
    
    var response = await client.DumpHistoryAsync(startToken: startToken, maxItems: (uint)count);
    
    Console.WriteLine($"\nFound {response.Blocks.Count} block(s)");
    
    int blockNum = 0;
    foreach (var block in response.Blocks)
    {
        blockNum++;
        Console.WriteLine($"\n=== Block #{blockNum} ===");
        Console.WriteLine($"  Hash: {block.Hash}");
        Console.WriteLine($"  Slot: {block.Slot}");
        Console.WriteLine($"  Size: {block.NativeBytes?.Length ?? 0} bytes");
        
        // Show first few bytes of block data if available
        if (block.NativeBytes != null && block.NativeBytes.Length > 0)
        {
            var preview = block.NativeBytes.Take(32).ToArray();
            Console.WriteLine($"  Data preview: {Convert.ToHexString(preview)}...");
        }
    }
    
    if (response.NextToken != null)
    {
        Console.WriteLine($"\nMore blocks available. Next token:");
        Console.WriteLine($"  Height: {response.NextToken.Height}");
        Console.WriteLine($"  Hash: {response.NextToken.Hash}");
        Console.WriteLine($"\nTo continue, run:");
        Console.WriteLine($"  dotnet run -- dumphistory {response.NextToken.Height} {response.NextToken.Hash} {count}");
    }
}

async Task TestFollowTip(ulong? blockIndex, string? blockHashHex)
{
    Console.WriteLine("Following chain tip updates (30 second timeout)...");
    Console.WriteLine("This will show Apply/Undo/Reset events as the chain progresses.\n");
    
    var client = new SyncServiceClient(SERVER_URL);
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    
    // Create intersection point if provided
    BlockRef? intersectPoint = null;
    if (!string.IsNullOrEmpty(blockHashHex) && blockIndex.HasValue)
    {
        intersectPoint = new BlockRef(blockHashHex, blockIndex.Value);
        Console.WriteLine($"Starting from intersection: {blockHashHex} at index {blockIndex}");
    }
    else
    {
        Console.WriteLine("Following from current tip (no intersection point)");
    }
    
    int eventCount = 0;
    
    try
    {
        await foreach (var response in client.FollowTipAsync(blockRef: intersectPoint, cts.Token))
        {
            eventCount++;
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.WriteLine($"\n[{timestamp}] Event #{eventCount}: {response.Action}");
            
            switch (response.Action)
            {
                case NextResponseAction.Apply:
                    if (response.AppliedBlock != null)
                    {
                        Console.WriteLine($"  Applied Block:");
                        Console.WriteLine($"    Hash: {response.AppliedBlock.Hash}");
                        Console.WriteLine($"    Slot: {response.AppliedBlock.Slot}");
                        Console.WriteLine($"    Size: {response.AppliedBlock.NativeBytes?.Length ?? 0} bytes");
                    }
                    break;
                    
                case NextResponseAction.Undo:
                    if (response.UndoneBlock != null)
                    {
                        Console.WriteLine($"  Undone Block:");
                        Console.WriteLine($"    Hash: {response.UndoneBlock.Hash}");
                        Console.WriteLine($"    Slot: {response.UndoneBlock.Slot}");
                    }
                    break;
                    
                case NextResponseAction.Reset:
                    if (response.ResetRef != null)
                    {
                        Console.WriteLine($"  Reset to:");
                        Console.WriteLine($"    Hash: {response.ResetRef.Hash}");
                        Console.WriteLine($"    Height: {response.ResetRef.Height}");
                    }
                    break;
            }
            
            // Stop after 10 events to avoid overwhelming output
            if (eventCount >= 10)
            {
                Console.WriteLine("\nStopping after 10 events...");
                break;
            }
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\nTimeout reached (30 seconds)");
    }
    
    Console.WriteLine($"\nTotal events observed: {eventCount}");
}

