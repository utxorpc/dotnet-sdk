using Google.Protobuf;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using BlockRef = Utxorpc.Sdk.Models.BlockRef;
using SpecSyncBlockRef = Utxorpc.V1alpha.Sync.BlockRef;
using SpecWatchBlockRef = Utxorpc.V1alpha.Watch.BlockRef;
using Block = Utxorpc.Sdk.Models.Block;
using Utxorpc.V1alpha.Sync;
using SpecSubmitTxResponse = Utxorpc.V1alpha.Submit.SubmitTxResponse;
using SubmitTxResponse = Utxorpc.Sdk.Models.SubmitTxResponse;
using SpecWaitForTxResponse = Utxorpc.V1alpha.Submit.WaitForTxResponse;
using WaitForTxResponse = Utxorpc.Sdk.Models.WaitForTxResponse;
using SpecWatchMempoolResponse = Utxorpc.V1alpha.Submit.WatchMempoolResponse;
using WatchMempoolResponse = Utxorpc.Sdk.Models.WatchMempoolResponse;
using SpecWatchTxResponse = Utxorpc.V1alpha.Watch.WatchTxResponse;
using SpecTxInMempool = Utxorpc.V1alpha.Submit.TxInMempool;
using TxInMempool = Utxorpc.Sdk.Models.TxInMempool;
using SpecStage = Utxorpc.V1alpha.Submit.Stage;
using Stage = Utxorpc.Sdk.Models.Enums.Stage;

namespace Utxorpc.Sdk.Utils;

public static class DataUtils
{
    // Block conversion methods
    public static Block? FromAnyChainBlock(AnyChainBlock? anyChainBlock)
    {
        if (anyChainBlock?.Cardano != null)
        {
            var cardanoBlock = anyChainBlock.Cardano;
            return new Block(
                Convert.ToHexString(cardanoBlock.Header.Hash.ToByteArray()),
                cardanoBlock.Header.Slot,
                anyChainBlock.NativeBytes.ToByteArray()
            );
        }
        return null;
    }

    // BlockRef conversion methods
    public static BlockRef FromSyncBlockRef(SpecSyncBlockRef syncBlockRef)
    {
        return new BlockRef(
            Convert.ToHexString(syncBlockRef.Hash.ToByteArray()),
            syncBlockRef.Index
        );
    }

    public static BlockRef FromWatchBlockRef(SpecWatchBlockRef watchBlockRef)
    {
        return new BlockRef(
            Convert.ToHexString(watchBlockRef.Hash.ToByteArray()),
            watchBlockRef.Index
        );
    }

    public static SpecSyncBlockRef ToSyncBlockRef(BlockRef blockRef)
    {
        return new SpecSyncBlockRef
        {
            Hash = ByteString.CopyFrom(Convert.FromHexString(blockRef.Hash)),
            Index = blockRef.Index
        };
    }

    public static SpecWatchBlockRef ToWatchBlockRef(BlockRef blockRef)
    {
        return new SpecWatchBlockRef
        {
            Hash = ByteString.CopyFrom(Convert.FromHexString(blockRef.Hash)),
            Index = blockRef.Index
        };
    }

    // NextResponse creation methods
    public static NextResponse CreateApplyResponse(Block block) => 
        new(NextResponseAction.Apply, AppliedBlock: block);

    public static NextResponse CreateUndoResponse(Block block) => 
        new(NextResponseAction.Undo, UndoneBlock: block);

    public static NextResponse CreateResetResponse(BlockRef blockRef) => 
        new(NextResponseAction.Reset, ResetRef: blockRef);

    // Submit service conversion methods
    public static SubmitTxResponse FromSpecSubmitTxResponse(SpecSubmitTxResponse specResponse)
    {
        return new SubmitTxResponse(
            [.. specResponse.Ref.Select(r => r.ToByteArray())]
        );
    }

    public static WaitForTxResponse FromSpecWaitForTxResponse(SpecWaitForTxResponse specResponse)
    {
        return new WaitForTxResponse(
            specResponse.Ref?.ToByteArray(),
            FromSpecStage(specResponse.Stage)
        );
    }

    public static WatchMempoolResponse FromSpecWatchMempoolResponse(SpecWatchMempoolResponse specResponse)
    {
        return new WatchMempoolResponse(
            FromSpecTxInMempool(specResponse.Tx)
        );
    }

    public static TxInMempool? FromSpecTxInMempool(SpecTxInMempool? specTxInMempool)
    {
        if (specTxInMempool == null) return null;

        return new TxInMempool(
            specTxInMempool.Ref?.ToByteArray(),
            specTxInMempool.NativeBytes?.ToByteArray(),
            FromSpecStage(specTxInMempool.Stage),
            specTxInMempool.ParsedStateCase == SpecTxInMempool.ParsedStateOneofCase.Cardano 
                ? new AnyUtxoData(
                    specTxInMempool.NativeBytes?.ToByteArray() ?? Array.Empty<byte>(),
                    null,
                    specTxInMempool.Cardano
                ) 
                : null
        );
    }

    public static Stage FromSpecStage(SpecStage specStage)
    {
        return specStage switch
        {
            SpecStage.Unspecified => Stage.Unspecified,
            SpecStage.Acknowledged => Stage.Acknowledged,
            SpecStage.Mempool => Stage.Mempool,
            SpecStage.Network => Stage.Network,
            SpecStage.Confirmed => Stage.Confirmed,
            _ => Stage.Unspecified
        };
    }
}

