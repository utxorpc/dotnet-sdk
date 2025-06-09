using Utxorpc.Sdk;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using Google.Protobuf.WellKnownTypes;

var syncClient = new SyncServiceClient(
    url: "http://localhost:50051"
);

Console.WriteLine("=== UTxO RPC Sync Service Demo ===\n");

try
{
    // 1. Read the current tip of the blockchain
    Console.WriteLine("1. Reading current blockchain tip...");
    var tipRef = await syncClient.ReadTipAsync();
    if (tipRef is not null)
    {
        Console.WriteLine($"   Hash: {tipRef.Hash}");
        Console.WriteLine($"   Index: {tipRef.Index}");
    }
    else
    {
        Console.WriteLine("   Failed to read tip");
        return;
    }

    // 2. Fetch the tip block
    Console.WriteLine("\n2. Fetching tip block...");
    var tipBlock = await syncClient.FetchBlockAsync(tipRef);
    if (tipBlock is not null)
    {
        Console.WriteLine($"   Block Hash: {tipBlock.Hash}");
        Console.WriteLine($"   Block Slot: {tipBlock.Slot}");
        Console.WriteLine($"   Block Size: {tipBlock.NativeBytes?.Length ?? 0} bytes");
    }

    // 2.5. Test field mask - fetch only hash and slot (no native_bytes)
    Console.WriteLine("\n2.5. Testing field mask (hash + slot only)...");
    var fieldMask = new FieldMask();
    fieldMask.Paths.Add("cardano.header.hash");
    fieldMask.Paths.Add("cardano.header.slot");
    // Intentionally exclude "native_bytes" to test field masking
    
    var maskedBlock = await syncClient.FetchBlockAsync(tipRef, fieldMask);
    if (maskedBlock is not null)
    {
        Console.WriteLine($"   Masked Block Hash: {maskedBlock.Hash ?? "NULL"}");
        Console.WriteLine($"   Masked Block Slot: {maskedBlock.Slot?.ToString() ?? "NULL"}");
        Console.WriteLine($"   Masked Block Size: {maskedBlock.NativeBytes?.Length.ToString() ?? "NULL"} bytes");
        Console.WriteLine($"   Field mask working: {(maskedBlock.NativeBytes == null ? "YES" : "NO")}");
    }

    // 3. Dump recent history (last 5 blocks)
    Console.WriteLine("\n3. Dumping recent history (last 5 blocks)...");
    var historyResponse = await syncClient.DumpHistoryAsync(maxItems: 5);
    Console.WriteLine($"   Retrieved {historyResponse.Blocks.Count} blocks");
    foreach (var block in historyResponse.Blocks)
    {
        Console.WriteLine($"   - Block {block.Slot}: {block.Hash?[..16] ?? "NULL"}...");
    }
    if (historyResponse.NextToken is not null)
    {
        Console.WriteLine($"   Next token: {historyResponse.NextToken.Hash[..16]}...");
    }

    // 4. Follow tip for a few updates (with timeout)
    Console.WriteLine("\n4. Following tip for updates (10 second timeout)...");
    var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
    
    await foreach (var response in syncClient.FollowTipAsync(tipRef, cancellationToken))
    {
        switch (response.Action)
        {
            case NextResponseAction.Apply:
                if (response.AppliedBlock is not null)
                {
                    Console.WriteLine($"   APPLY: Block {response.AppliedBlock.Slot} - {response.AppliedBlock.Hash?[..16] ?? "NULL"}...");
                }
                break;
            case NextResponseAction.Undo:
                if (response.UndoneBlock is not null)
                {
                    Console.WriteLine($"   UNDO: Block {response.UndoneBlock.Slot} - {response.UndoneBlock.Hash?[..16] ?? "NULL"}...");
                }
                break;
            case NextResponseAction.Reset:
                if (response.ResetRef is not null)
                {
                    Console.WriteLine($"   RESET: To block {response.ResetRef.Index} - {response.ResetRef.Hash[..16]}...");
                }
                break;
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("   Tip following timed out after 10 seconds");
}
catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Cancelled)
{
    Console.WriteLine("   Tip following was cancelled");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine("\n=== Demo completed ===");