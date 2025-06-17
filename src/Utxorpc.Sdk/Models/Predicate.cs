using Utxorpc.Sdk.Models.Enums;

namespace Utxorpc.Sdk.Models;

public abstract record Predicate
{
    public abstract V1alpha.Query.UtxoPredicate ToUtxoPredicate();
}

public record MatchPredicate(
    Action<V1alpha.Query.AnyUtxoPattern> ConfigureMatch
) : Predicate
{
    public override V1alpha.Query.UtxoPredicate ToUtxoPredicate()
    {
        V1alpha.Query.UtxoPredicate predicate = new()
        {
            Match = new V1alpha.Query.AnyUtxoPattern()
        };
        ConfigureMatch(predicate.Match);
        return predicate;
    }
}

public record NotPredicate(
    params Predicate[] Predicates
) : Predicate
{
    public override V1alpha.Query.UtxoPredicate ToUtxoPredicate()
    {
        V1alpha.Query.UtxoPredicate predicate = new();
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
    public override V1alpha.Query.UtxoPredicate ToUtxoPredicate()
    {
        V1alpha.Query.UtxoPredicate predicate = new();
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
    public override V1alpha.Query.UtxoPredicate ToUtxoPredicate()
    {
        V1alpha.Query.UtxoPredicate predicate = new();
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
    public override V1alpha.Query.UtxoPredicate ToUtxoPredicate()
    {
        V1alpha.Query.UtxoPredicate predicate = new()
        {
            Match = new V1alpha.Query.AnyUtxoPattern()
        };

        V1alpha.Cardano.TxOutputPattern cardanoPattern = new();
        V1alpha.Cardano.AddressPattern addressPattern = new();
        
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
    public override V1alpha.Query.UtxoPredicate ToUtxoPredicate()
    {
        V1alpha.Query.UtxoPredicate predicate = new()
        {
            Match = new V1alpha.Query.AnyUtxoPattern()
        };

        V1alpha.Cardano.TxOutputPattern cardanoPattern = new();
        V1alpha.Cardano.AssetPattern assetPattern = new();
        
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