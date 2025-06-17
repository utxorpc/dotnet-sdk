using Google.Protobuf;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;
using BlockRef = Utxorpc.Sdk.Models.BlockRef;
using SpecSyncBlockRef = Utxorpc.V1alpha.Sync.BlockRef;
using SpecWatchBlockRef = Utxorpc.V1alpha.Watch.BlockRef;
using Block = Utxorpc.Sdk.Models.Block;
using Utxorpc.V1alpha.Sync;
using SpecWatchTxResponse = Utxorpc.V1alpha.Watch.WatchTxResponse;


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


    // Watch service conversion methods
    public static WatchTxResponse FromSpecWatchTxResponse(SpecWatchTxResponse specResponse)
    {
        WatchTxAction action;
        V1alpha.Watch.AnyChainTx? tx;
        
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
                V1alpha.Watch.AnyChainTx.ChainOneofCase.Cardano => tx.Cardano,
                _ => throw new InvalidOperationException($"Unsupported chain type: {tx.ChainCase}"),
            };
        }
        
        return new WatchTxResponse(action, raw, parsedState);
    }
}

