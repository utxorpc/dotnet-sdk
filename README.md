<div align="center">
  <h1 style="font-size: 3em;">UTxORPC.Sdk | .NET</h1>
  <h4>A gRPC interface for UTxO Blockchains</h4>
</div>
<div align="center">
  
  ![Forks](https://img.shields.io/github/forks/utxorpc/dotnet-sdk.svg?style=social) 
  ![Stars](https://img.shields.io/github/stars/utxorpc/dotnet-sdk.svg?style=social) 
  ![Contributors](https://img.shields.io/github/contributors/utxorpc/dotnet-sdk.svg) 
  ![Issues](https://img.shields.io/github/issues/utxorpc/dotnet-sdk.svg) 
  ![Issues Closed](https://img.shields.io/github/issues-closed/utxorpc/dotnet-sdk.svg) 

  <a href="https://www.nuget.org/packages/Utxorpc.Sdk/">
    <img src="https://img.shields.io/nuget/v/Utxorpc.Sdk.svg" alt="NuGet">
  </a>
</div>


The `Utxorpc.Sdk` provides a .NET interface for interacting with UTxO-based blockchains via gRPC. It simplifies the process of fetching blocks, following chain tips, and more, allowing developers to easily integrate blockchain data into their applications.

### Features

- **FetchBlockAsync**: Retrieve blocks by hash and index.
- **FollowTipAsync**: Stream real-time updates as new blocks are applied, undone, or when the chain is reset.
- **Future Expansion**: Additional methods will be provided for watching transactions, submitting transactions, querying data, and more.

### Installation

To install the SDK, use the following command in the NuGet Package Manager:

```bash
dotnet add package Utxorpc.Sdk --version 1.0.0-alpha
```

Alternatively, you can install it via the NuGet Package Manager in Visual Studio.

### Usage

#### SyncServiceClient Example

The `SyncServiceClient` allows you to fetch blocks and follow chain tips with ease. Below is an example of how to use it:

```csharp
using Utxorpc.Sdk;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;

var headers = new Dictionary<string, string>
{
    { "dmtr-api-key", "dmtr_utxorpc1vc0m93rynmltysttwm7ns9m3n5cklws6" },
};

var client = new SyncServiceClient(
    url: "https://preview.utxorpc-v0.demeter.run",
    headers
);

await foreach (NextResponse? response in client.FollowTipAsync(
    new BlockRef
    (
        "b977e548f3364b114505f3311a10f89e5f5cf47e815765bce6750a5de48e5951",
        58717900
    )))
{
    Console.WriteLine("___________________");
    switch (response.Action)
    {
        case NextResponseAction.Apply:
            Block? applyBlock = response.AppliedBlock;
            if (applyBlock is not null)
            {
                Console.WriteLine($"Apply Block: {applyBlock.Hash} Slot: {applyBlock.Slot}");
                Console.WriteLine($"Cbor: {Convert.ToHexString(applyBlock.NativeBytes ?? [])}");
            }
            break;
        case NextResponseAction.Undo:
            Block? undoBlock = response.UndoneBlock;
            if (undoBlock is not null)
            {
                Console.WriteLine($"Undo Block: {undoBlock.Hash} Slot: {undoBlock.Slot}");
            }
            break;
        case NextResponseAction.Reset:
            BlockRef? resetRef = response.ResetRef;
            if (resetRef is not null)
            {
                Console.WriteLine($"Reset to Block: {resetRef.Hash} Slot: {resetRef.Index}");
            }
            break;
    }
    Console.WriteLine("___________________");
}


```

### Roadmap

- **WatchServiceClient**: Implementation for watching transactions.
- **SubmitServiceClient**: Methods for submitting and monitoring transactions.
- **QueryServiceClient**: Interface for querying blockchain data and parameters.

### Contributing

Contributions are welcome! Please fork the repository and submit a pull request. For major changes, please open an issue first to discuss what you would like to change.



