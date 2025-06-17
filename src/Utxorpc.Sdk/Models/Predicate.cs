using Utxorpc.Sdk.Models.Enums;
using Utxorpc.V1alpha.Query;
using Utxorpc.V1alpha.Submit;
using Utxorpc.V1alpha.Watch;
using Utxorpc.V1alpha.Cardano;

namespace Utxorpc.Sdk.Models;

public abstract record Predicate
{
    public abstract UtxoPredicate ToUtxoPredicate();
    public abstract V1alpha.Submit.TxPredicate ToTxPredicate();
    public abstract V1alpha.Watch.TxPredicate ToWatchTxPredicate();
}

public record MatchPredicate(
    Action<AnyUtxoPattern> ConfigureMatch,
    Action<V1alpha.Submit.AnyChainTxPattern>? ConfigureTxMatch = null,
    Action<V1alpha.Watch.AnyChainTxPattern>? ConfigureWatchTxMatch = null
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
    
    public override V1alpha.Submit.TxPredicate ToTxPredicate()
    {
        V1alpha.Submit.TxPredicate predicate = new()
        {
            Match = new V1alpha.Submit.AnyChainTxPattern()
        };
        
        if (ConfigureTxMatch != null)
        {
            ConfigureTxMatch(predicate.Match);
        }
        else
        {
            throw new NotSupportedException("MatchPredicate requires ConfigureTxMatch to be set for TxPredicate conversion");
        }
        
        return predicate;
    }
    
    public override V1alpha.Watch.TxPredicate ToWatchTxPredicate()
    {
        V1alpha.Watch.TxPredicate predicate = new()
        {
            Match = new V1alpha.Watch.AnyChainTxPattern()
        };
        
        if (ConfigureWatchTxMatch != null)
        {
            ConfigureWatchTxMatch(predicate.Match);
        }
        else if (ConfigureTxMatch != null)
        {
            // Try to use Submit configuration as fallback if the structures are compatible
            throw new NotSupportedException("MatchPredicate requires ConfigureWatchTxMatch to be set for WatchTxPredicate conversion");
        }
        else
        {
            throw new NotSupportedException("MatchPredicate requires ConfigureWatchTxMatch to be set for WatchTxPredicate conversion");
        }
        
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
    
    public override V1alpha.Submit.TxPredicate ToTxPredicate()
    {
        V1alpha.Submit.TxPredicate predicate = new();
        foreach (Predicate p in Predicates)
        {
            predicate.Not.Add(p.ToTxPredicate());
        }
        return predicate;
    }
    
    public override V1alpha.Watch.TxPredicate ToWatchTxPredicate()
    {
        V1alpha.Watch.TxPredicate predicate = new();
        foreach (Predicate p in Predicates)
        {
            predicate.Not.Add(p.ToWatchTxPredicate());
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
    
    public override V1alpha.Submit.TxPredicate ToTxPredicate()
    {
        V1alpha.Submit.TxPredicate predicate = new();
        foreach (Predicate p in Predicates)
        {
            predicate.AllOf.Add(p.ToTxPredicate());
        }
        return predicate;
    }
    
    public override V1alpha.Watch.TxPredicate ToWatchTxPredicate()
    {
        V1alpha.Watch.TxPredicate predicate = new();
        foreach (Predicate p in Predicates)
        {
            predicate.AllOf.Add(p.ToWatchTxPredicate());
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
    
    public override V1alpha.Submit.TxPredicate ToTxPredicate()
    {
        V1alpha.Submit.TxPredicate predicate = new();
        foreach (Predicate p in Predicates)
        {
            predicate.AnyOf.Add(p.ToTxPredicate());
        }
        return predicate;
    }
    
    public override V1alpha.Watch.TxPredicate ToWatchTxPredicate()
    {
        V1alpha.Watch.TxPredicate predicate = new();
        foreach (Predicate p in Predicates)
        {
            predicate.AnyOf.Add(p.ToWatchTxPredicate());
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
    
    public override V1alpha.Submit.TxPredicate ToTxPredicate()
    {
        V1alpha.Submit.TxPredicate predicate = new()
        {
            Match = new V1alpha.Submit.AnyChainTxPattern
            {
                Cardano = new TxPattern()
            }
        };

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
        
        predicate.Match.Cardano.HasAddress = addressPattern;
        
        return predicate;
    }
    
    public override V1alpha.Watch.TxPredicate ToWatchTxPredicate()
    {
        V1alpha.Watch.TxPredicate predicate = new()
        {
            Match = new V1alpha.Watch.AnyChainTxPattern
            {
                Cardano = new TxPattern()
            }
        };

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
        
        predicate.Match.Cardano.HasAddress = addressPattern;
        
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
    
    public override V1alpha.Submit.TxPredicate ToTxPredicate()
    {
        V1alpha.Submit.TxPredicate predicate = new()
        {
            Match = new V1alpha.Submit.AnyChainTxPattern
            {
                Cardano = new TxPattern()
            }
        };

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
        
        predicate.Match.Cardano.MovesAsset = assetPattern;
        
        return predicate;
    }
    
    public override V1alpha.Watch.TxPredicate ToWatchTxPredicate()
    {
        V1alpha.Watch.TxPredicate predicate = new()
        {
            Match = new V1alpha.Watch.AnyChainTxPattern
            {
                Cardano = new TxPattern()
            }
        };

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
        
        predicate.Match.Cardano.MovesAsset = assetPattern;
        
        return predicate;
    }
}