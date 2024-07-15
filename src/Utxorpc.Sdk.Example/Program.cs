using Google.Protobuf;
using Utxorpc.Sdk;
using Utxorpc.V1alpha.Sync;

var utxoRpcClient = new UtxorpcClient("http://localhost:50051");
var chainSyncClient = utxoRpcClient.SyncClient;

var request = new FetchBlockRequest();
request.Ref.Add(
    new BlockRef
    {
        Hash = ByteString.CopyFrom(
            Convert.FromHexString("34c65aba4b299113a488b74e2efe3a3dd272d25b470d25f374b2c693d4386535")
        ),
        Index = 54131816
    }
);


var response = await chainSyncClient.FetchBlockAsync(request);
var blockHash = Convert.ToHexString(response.Block[0].Cardano.Header.Hash.ToByteArray());
var blockSlot = response.Block[0].Cardano.Header.Slot;
Console.WriteLine($"Block: {blockHash} Slot: {blockSlot} Block Data: {response.Block[0].Cardano.Body.Tx.Count}");