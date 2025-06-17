using Utxorpc.Sdk.Models.Enums;
using Utxorpc.V1alpha.Cardano;
using Utxorpc.V1alpha.Query;
namespace Utxorpc.Sdk.Models;

public abstract record Predicate
{
    public abstract UtxoPredicate ToUtxoPredicate();
}

public record MatchPredicate(
    Action<AnyUtxoPattern> ConfigureMatch
) : Predicate
{
    public override UtxoPredicate ToUtxoPredicate()
    {
        UtxoPredicate predicate = new()
        {
            Match = new AnyUtxoPattern()
        };
        ConfigureMatch(predicate.Match);
        return predicate;
    }
}

public record NotPredicate(
    params Predicate[] Predicates
) : Predicate
{
    public override UtxoPredicate ToUtxoPredicate()
    {
        UtxoPredicate predicate = new();
        foreach (Predicate p in Predicates)
        {
            predicate.Not.Add(p.ToUtxoPredicate());
        }
        return predicate;
    }
}

public record AllOfPredicate(
    params Predicate[] Predicates
) : Predicate
{
    public override UtxoPredicate ToUtxoPredicate()
    {
        UtxoPredicate predicate = new();
        foreach (Predicate p in Predicates)
        {
            predicate.AllOf.Add(p.ToUtxoPredicate());
        }
        return predicate;
    }
}

public record AnyOfPredicate(
    params Predicate[] Predicates
) : Predicate
{
    public override UtxoPredicate ToUtxoPredicate()
    {
        UtxoPredicate predicate = new();
        foreach (Predicate p in Predicates)
        {
            predicate.AnyOf.Add(p.ToUtxoPredicate());
        }
        return predicate;
    }
}

public record AddressPredicate(
    byte[]? Address,
    AddressSearchType? AddressSearch
) : Predicate
{
    public override UtxoPredicate ToUtxoPredicate()
    {
        UtxoPredicate predicate = new()
        {
            Match = new AnyUtxoPattern()
        };

        TxOutputPattern cardanoPattern = new();
        AddressPattern addressPattern = new();
        
        switch (AddressSearch)
        {
            case AddressSearchType.ExactAddress:
                addressPattern.ExactAddress = Google.Protobuf.ByteString.CopyFrom(Address);
                break;
            case AddressSearchType.PaymentPart:
                addressPattern.PaymentPart = Google.Protobuf.ByteString.CopyFrom(Address);
                break;
            case AddressSearchType.DelegationPart:
                addressPattern.DelegationPart = Google.Protobuf.ByteString.CopyFrom(Address);
                break;
        }
        
        cardanoPattern.Address = addressPattern;
        predicate.Match.Cardano = cardanoPattern;
        
        return predicate;
    }
}

public record AssetPredicate(
    byte[]? Asset,
    AssetSearchType? AssetSearch
) : Predicate
{
    public override UtxoPredicate ToUtxoPredicate()
    {
        UtxoPredicate predicate = new()
        {
            Match = new AnyUtxoPattern()
        };

        TxOutputPattern cardanoPattern = new();
        AssetPattern assetPattern = new();
        
        switch (AssetSearch)
        {
            case AssetSearchType.PolicyId:
                assetPattern.PolicyId = Google.Protobuf.ByteString.CopyFrom(Asset);
                break;
            case AssetSearchType.AssetName:
                assetPattern.AssetName = Google.Protobuf.ByteString.CopyFrom(Asset);
                break;
        }
        
        cardanoPattern.Asset = assetPattern;
        predicate.Match.Cardano = cardanoPattern;
        
        return predicate;
    }
}