namespace Utxorpc.Sdk;

/// <summary>Says what a failed short method means for the caller.</summary>
public enum UtxorpcErrorKind
{
    /// <summary>Any other failure; read the status code and detail.</summary>
    Other,
    /// <summary>The server did not accept the transaction.</summary>
    InvalidTx,
    /// <summary>The server rejected the request's content.</summary>
    InvalidRequest,
    /// <summary>The server does not know the transaction, block or other item asked for.</summary>
    NotFound,
    /// <summary>The server does not offer this call.</summary>
    Unsupported,
    /// <summary>The server could not be reached or the connection broke; trying again may work.</summary>
    Unavailable,
    /// <summary>The call's deadline or the client's default timeout passed.</summary>
    Timeout,
}
