using Google.Protobuf;
using Utxorpc.V1alpha.Cardano;
using Utxorpc.V1alpha.Sync;

namespace Utxorpc.Sdk.Models;
public record Block(
    string Hash, 
    ulong Slot, 
    byte[] NativeBytes
);