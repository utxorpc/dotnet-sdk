using Utxorpc.Sdk.Models.Enums;

namespace Utxorpc.Sdk.Models;

public abstract record Predicate;

public record AddressPredicate(
    byte[]? Address,
    AddressSearchType? AddressSearch
) : Predicate;

public record AssetPredicate(
    byte[]? Asset,
    AssetSearchType? AssetSearch
) : Predicate;