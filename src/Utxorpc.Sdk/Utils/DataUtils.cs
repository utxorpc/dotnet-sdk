using Google.Protobuf;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using BlockRef = Utxorpc.Sdk.Models.BlockRef;
using SpecSyncBlockRef = Utxorpc.V1alpha.Sync.BlockRef;
using SpecWatchBlockRef = Utxorpc.V1alpha.Watch.BlockRef;
using Block = Utxorpc.Sdk.Models.Block;
using SpecSync = Utxorpc.V1alpha.Sync;
using SpecWatchTxResponse = Utxorpc.V1alpha.Watch.WatchTxResponse;
using SpecWatch = Utxorpc.V1alpha.Watch;
using WatchTxResponse = Utxorpc.Sdk.Models.WatchTxResponse;
using SpecSubmitTxResponse = Utxorpc.V1alpha.Submit.SubmitTxResponse;
using SubmitTxResponse = Utxorpc.Sdk.Models.SubmitTxResponse;
using SpecWaitForTxResponse = Utxorpc.V1alpha.Submit.WaitForTxResponse;
using WaitForTxResponse = Utxorpc.Sdk.Models.WaitForTxResponse;
using SpecWatchMempoolResponse = Utxorpc.V1alpha.Submit.WatchMempoolResponse;
using WatchMempoolResponse = Utxorpc.Sdk.Models.WatchMempoolResponse;
using SpecTxInMempool = Utxorpc.V1alpha.Submit.TxInMempool;
using TxInMempool = Utxorpc.Sdk.Models.TxInMempool;
using SpecStage = Utxorpc.V1alpha.Submit.Stage;
using Stage = Utxorpc.Sdk.Models.Enums.Stage;
using SpecTxoRef = Utxorpc.V1alpha.Query.TxoRef;
using TxoRef = Utxorpc.Sdk.Models.TxoRef;
using SpecChainPoint = Utxorpc.V1alpha.Query.ChainPoint;
using ChainPoint = Utxorpc.Sdk.Models.ChainPoint;
using SpecAnyUtxoData = Utxorpc.V1alpha.Query.AnyUtxoData;
using AnyUtxoData = Utxorpc.Sdk.Models.AnyUtxoData;
using SpecAnyChainParams = Utxorpc.V1alpha.Query.AnyChainParams;
using AnyChainParams = Utxorpc.Sdk.Models.AnyChainParams;
using SpecReadUtxosResponse = Utxorpc.V1alpha.Query.ReadUtxosResponse;
using ReadUtxosResponse = Utxorpc.Sdk.Models.ReadUtxosResponse;
using SpecSearchUtxosResponse = Utxorpc.V1alpha.Query.SearchUtxosResponse;
using SearchUtxosResponse = Utxorpc.Sdk.Models.SearchUtxosResponse;
using SpecReadParamsResponse = Utxorpc.V1alpha.Query.ReadParamsResponse;
using ReadParamsResponse = Utxorpc.Sdk.Models.ReadParamsResponse;
using SpecCardano = Utxorpc.V1alpha.Cardano;

namespace Utxorpc.Sdk.Utils;

public static class DataUtils
{
    // Block conversion methods
    public static Block? FromAnyChainBlock(SpecSync.AnyChainBlock? anyChainBlock)
    {
        if (anyChainBlock?.Cardano != null)
        {
            SpecCardano.Block cardanoBlock = anyChainBlock.Cardano;
            return new Block(
                Convert.ToHexString(cardanoBlock.Header.Hash.ToByteArray()),
                cardanoBlock.Header.Slot,
                anyChainBlock.NativeBytes.ToByteArray()
            );
        }
        return null;
    }

    // BlockRef conversion methods
    public static BlockRef? FromSyncBlockRef(SpecSyncBlockRef? syncBlockRef)
    {
        if (syncBlockRef == null) return null;
        
        return new BlockRef(
            Convert.ToHexString(syncBlockRef.Hash.ToByteArray()),
            syncBlockRef.Index
        );
    }

    public static BlockRef? FromWatchBlockRef(SpecWatchBlockRef? watchBlockRef)
    {
        if (watchBlockRef == null) return null;
        
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

    public static NextResponse CreateResetResponse(BlockRef? blockRef) => 
        new(NextResponseAction.Reset, ResetRef: blockRef);

    // Watch service conversion methods
    public static WatchTxResponse FromSpecWatchTxResponse(SpecWatchTxResponse specResponse)
    {
        WatchTxAction action;
        SpecWatch.AnyChainTx? tx;
        
        switch (specResponse.ActionCase)
        {
            case SpecWatchTxResponse.ActionOneofCase.Apply:
                action = WatchTxAction.Apply;
                tx = specResponse.Apply;
                break;
            case SpecWatchTxResponse.ActionOneofCase.Undo:
                action = WatchTxAction.Undo;
                tx = specResponse.Undo;
                break;
            default:
                throw new InvalidOperationException($"Unknown WatchTxResponse action: {specResponse.ActionCase}");
        }
        
        byte[]? raw = null;
        object? parsedState = null;
        
        if (tx is not null)
        {
            parsedState = tx.ChainCase switch
            {
                SpecWatch.AnyChainTx.ChainOneofCase.Cardano => tx.Cardano,
                _ => throw new InvalidOperationException($"Unsupported chain type: {tx.ChainCase}"),
            };
        }
        
        return new WatchTxResponse(action, raw, parsedState);
    }

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
        TxInMempool? tx = null;
        if (specResponse.Tx != null)
        {
            tx = FromSpecTxInMempool(specResponse.Tx);
        }
        
        return new WatchMempoolResponse(tx);
    }

    private static TxInMempool FromSpecTxInMempool(SpecTxInMempool specTx)
    {
        // Create AnyUtxoData from parsed state if available
        AnyUtxoData? parsedState = specTx.ParsedStateCase == SpecTxInMempool.ParsedStateOneofCase.Cardano 
            ? new AnyUtxoData(
                specTx.NativeBytes?.ToByteArray() ?? Array.Empty<byte>(),
                null,
                specTx.Cardano
            ) 
            : null;

        return new TxInMempool(
            specTx.Ref?.ToByteArray(),
            specTx.NativeBytes?.ToByteArray(),
            FromSpecStage(specTx.Stage),
            parsedState
        );
    }

    private static Stage FromSpecStage(SpecStage specStage) => specStage switch
    {
        SpecStage.Unspecified => Stage.Unspecified,
        SpecStage.Acknowledged => Stage.Acknowledged,
        SpecStage.Mempool => Stage.Mempool,
        SpecStage.Network => Stage.Network,
        SpecStage.Confirmed => Stage.Confirmed,
        _ => Stage.Unspecified
    };

    // Query conversion methods
    public static TxoRef? FromSpecTxoRef(SpecTxoRef? specTxoRef)
    {
        if (specTxoRef == null) return null;
        
        return new TxoRef(
            specTxoRef.Hash.ToByteArray(),
            specTxoRef.Index
        );
    }

    public static ChainPoint? FromSpecChainPoint(SpecChainPoint? specChainPoint)
    {
        if (specChainPoint == null) return null;
        
        return new ChainPoint(
            specChainPoint.Slot,
            specChainPoint.Hash.ToByteArray()
        );
    }

    public static AnyUtxoData? FromSpecAnyUtxoData(SpecAnyUtxoData? specUtxoData)
    {
        if (specUtxoData == null) return null;
        
        return new AnyUtxoData(
            specUtxoData.NativeBytes.ToByteArray(),
            FromSpecTxoRef(specUtxoData.TxoRef),
            specUtxoData.ParsedStateCase == SpecAnyUtxoData.ParsedStateOneofCase.Cardano ? specUtxoData.Cardano : null
        );
    }

    public static AnyChainParams? FromSpecAnyChainParams(SpecAnyChainParams? specChainParams)
    {
        if (specChainParams == null) return null;
        
        return new AnyChainParams(
            specChainParams.ParamsCase == SpecAnyChainParams.ParamsOneofCase.Cardano ? specChainParams.Cardano : null
        );
    }

    public static ReadUtxosResponse FromSpecReadUtxosResponse(SpecReadUtxosResponse specResponse)
    {
        List<AnyUtxoData> items = [.. specResponse.Items.Select(FromSpecAnyUtxoData).Where(x => x != null).Cast<AnyUtxoData>()];
        return new ReadUtxosResponse(
            items,
            FromSpecChainPoint(specResponse.LedgerTip)
        );
    }

    public static SearchUtxosResponse FromSpecSearchUtxosResponse(SpecSearchUtxosResponse specResponse)
    {
        List<AnyUtxoData> items = [.. specResponse.Items.Select(FromSpecAnyUtxoData).Where(x => x != null).Cast<AnyUtxoData>()];
        return new SearchUtxosResponse(
            items,
            FromSpecChainPoint(specResponse.LedgerTip),
            specResponse.NextToken
        );
    }

    public static ReadParamsResponse FromSpecReadParamsResponse(SpecReadParamsResponse specResponse)
    {
        return new ReadParamsResponse(
            FromSpecAnyChainParams(specResponse.Values),
            FromSpecChainPoint(specResponse.LedgerTip)
        );
    }
}
