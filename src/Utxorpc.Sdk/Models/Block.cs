namespace Utxorpc.Sdk.Models;

public record Block(
    string? Hash,        // Nullable - field mask aware
    ulong? Slot,         // Nullable - field mask aware
    byte[]? NativeBytes  // Nullable - field mask aware
);