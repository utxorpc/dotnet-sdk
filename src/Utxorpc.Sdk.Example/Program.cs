using Utxorpc.Sdk;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using Google.Protobuf.WellKnownTypes;

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
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: dotnet run -- readutxos <tx-hash-base64> [index]");
                    Console.WriteLine("   OR: dotnet run -- readutxos <tx-hash-base64>:<index> [<tx-hash-base64>:<index> ...]");
                    Console.WriteLine("");
                    Console.WriteLine("Examples:");
                    Console.WriteLine("  Single UTXO: dotnet run -- readutxos abc123 0");
                    Console.WriteLine("  Multiple UTXOs: dotnet run -- readutxos abc123:0 def456:1 ghi789:0");
                    return;
                }
                
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
                    Console.WriteLine("Usage: dotnet run -- searchutxos <type> <value-base64>");
                    Console.WriteLine("");
                    Console.WriteLine("Address search types:");
                    Console.WriteLine("  address    - Search by exact address match");
                    Console.WriteLine("  payment    - Search by payment credential");
                    Console.WriteLine("  delegation - Search by delegation part");
                    Console.WriteLine("");
                    Console.WriteLine("Asset search types:");
                    Console.WriteLine("  asset      - Search by asset (policy:name in base64)");
                    Console.WriteLine("  policy     - Search by policy ID only");
                    Console.WriteLine("");
                    Console.WriteLine("Examples:");
                    Console.WriteLine("  dotnet run -- searchutxos address AFP7//q3sAEoGRfed/GKgIdBO+A0AdtKoqfb8K4VkdNNW0snKNBKgP3QQbtS7bM02svyWqJ4d+c4");
                    Console.WriteLine("  dotnet run -- searchutxos payment U/v/+rfwASgZF9539higgH1BO+A0AdtKoqfb");
                    Console.WriteLine("  dotnet run -- searchutxos delegation 8K4VkdNNW0snKNBKgP3QQbtS7bM02svyWqJ4");
                    return;
                }
                await TestSearchUtxos(args[1], args[2]);
                break;
                
                
            default:
                Console.WriteLine($"Unknown command: {command}");
                Console.WriteLine("\nAvailable commands:");
                Console.WriteLine("\n=== Query Service ===");
                Console.WriteLine("  readutxos <hash> [index]          - Query UTxOs by transaction");
                Console.WriteLine("  readparams                        - Query chain parameters");
                Console.WriteLine("  searchutxos <addr>                - Search UTxOs by address");
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
else
{
    Console.WriteLine("Usage: dotnet run -- <command> [args]");
    Console.WriteLine("\n=== Query Service ===");
    Console.WriteLine("  readutxos <hash> [index]          - Query single UTxO by transaction");
    Console.WriteLine("  readutxos <hash>:<idx> [...]      - Query multiple UTxOs");
    Console.WriteLine("  readparams                        - Query chain parameters");
    Console.WriteLine("  searchutxos <type> <value>        - Search UTxOs (address/payment/delegation/asset/policy)");
}

// Direct SDK usage methods
async Task TestQueryUtxos(string txHashBase64, uint index = 0)
{
    Console.WriteLine($"Querying UTxOs for tx: {txHashBase64}, index: {index}");
    
    var client = new QueryServiceClient(SERVER_URL);
    var txoRefs = new[]
    {
        new TxoRef(
            Hash: Convert.FromBase64String(txHashBase64),
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
                Console.WriteLine($"  Coin: {cardanoOutput.Coin} lovelace ({cardanoOutput.Coin / 1_000_000.0:F6} ADA)");
                
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
            Console.WriteLine($"Invalid UTXO format: {spec}. Expected format: <tx-hash-base64>:<index>");
            continue;
        }
        
        if (!uint.TryParse(parts[1], out var index))
        {
            Console.WriteLine($"Invalid index in: {spec}. Index must be a number.");
            continue;
        }
        
        try
        {
            var txHash = Convert.FromBase64String(parts[0]);
            txoRefs.Add(new TxoRef(txHash, index));
        }
        catch (FormatException)
        {
            Console.WriteLine($"Invalid base64 hash in: {spec}");
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
                Console.WriteLine($"  Coin: {cardanoOutput.Coin} lovelace ({cardanoOutput.Coin / 1_000_000.0:F6} ADA)");
                
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
            Console.WriteLine($"  Stake Key Deposit: {cardanoParams.StakeKeyDeposit / 1_000_000.0:F2} ADA");
            Console.WriteLine($"  Pool Deposit: {cardanoParams.PoolDeposit / 1_000_000.0:F2} ADA");
            Console.WriteLine($"  Min Pool Cost: {cardanoParams.MinPoolCost / 1_000_000.0:F2} ADA");
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
            Console.WriteLine($"  Governance Action Deposit: {cardanoParams.GovernanceActionDeposit / 1_000_000.0:F2} ADA");
            Console.WriteLine($"  DRep Deposit: {cardanoParams.DrepDeposit / 1_000_000.0:F2} ADA");
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

async Task TestSearchUtxos(string searchType, string valueBase64)
{
    Console.WriteLine($"Searching UTxOs by {searchType}: {valueBase64}");
    
    var client = new QueryServiceClient(SERVER_URL);
    var valueBytes = Convert.FromBase64String(valueBase64);
    
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
            Console.WriteLine($"  Value: {cardanoOutput.Coin / 1_000_000.0:F6} ADA");
            
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




