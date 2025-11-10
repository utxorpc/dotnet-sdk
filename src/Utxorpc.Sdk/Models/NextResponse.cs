using SyncBlockRef = Utxorpc.V1alpha.Sync.BlockRef;
using Utxorpc.Sdk.Models.Enums;
namespace Utxorpc.Sdk.Models;
public record NextResponse(
    NextResponseAction Action,
    Block? AppliedBlock = null,
    Block? UndoneBlock = null,
    BlockRef? ResetRef = null,
    BlockRef? Tip = null
);