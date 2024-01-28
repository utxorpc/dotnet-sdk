using Google.Protobuf;
using Grpc.Net.Client;
using Utxorpc.Sync.V1;

var channel = GrpcChannel.ForAddress("http://localhost:50051");
var chainSyncClient = new ChainSyncService.ChainSyncServiceClient(channel);

var request = new FetchBlockRequest();
request.Ref.Add(
    new BlockRef
    {
        Hash = ByteString.CopyFrom(
            Convert.FromHexString("3fc0126deefb17acda42ed941f8e61eaaf58ca72a386f831ccfb0daeb61e6d42")
        ),
        Index = 39822377
    }
);

var response = await chainSyncClient.FetchBlockAsync(request);
var blockHash = Convert.ToHexString(response.Block[0].Cardano.Header.Hash.ToByteArray());
var blockSlot = response.Block[0].Cardano.Header.Slot;
Console.WriteLine($"Block: {blockHash} Slot: {blockSlot}");