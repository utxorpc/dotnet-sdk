<div align="center">
  <h1 style="font-size: 3em;">Utxorpc.Sdk | .NET</h1>
  <h4>A gRPC interface for UTxO Blockchains</h4>
</div>
<div align="center">
  <a href="https://www.nuget.org/packages/Utxorpc.Sdk/">
    <img src="https://img.shields.io/nuget/v/Utxorpc.Sdk.svg" alt="NuGet">
  </a>
</div>

The `Utxorpc.Sdk` provides a .NET interface for interacting with UTxO-based blockchains via gRPC. It simplifies the process of fetching blocks, following chain tips, and more, allowing developers to easily integrate blockchain data into their applications.

### Features

- **Fetch Blocks**: Retrieve blocks by hash and index.
- **Follow Chain Tips**: Stream real-time updates as new blocks are applied, undone, or when the chain is reset.
- **Future Expansion**: Additional methods will be provided for watching transactions, submitting transactions, querying data, and more.

### Installation

To install the SDK, use the following command in the NuGet Package Manager:

```bash
dotnet add package Utxorpc.Sdk --version 0.7.0-alpha
```

Alternatively, you can install it via the NuGet Package Manager in Visual Studio.

### Usage

#### SyncServiceClient Example

The `SyncServiceClient` allows you to fetch blocks and follow chain tips with ease. Below is an example of how to use it:

```csharp
using Utxorpc.Sdk;
using Utxorpc.Sdk.Models;

var syncServiceClient = new SyncServiceClient("http://localhost:50051");

BlockRef blockRef = new BlockRef("1dace9bc646e9225251db04ff27397c199b04ec3f83c94cad28c438c3e7eeb50", 67823979);
Block? block = await syncServiceClient.FetchBlockAsync(blockRef);

if (block is not null)
{
    Console.WriteLine($"Block Hash: {block.Hash}");
    Console.WriteLine($"Slot: {block.Slot}");
    Console.WriteLine($"Native Bytes: {BitConverter.ToString(block.NativeBytes)}");
}
else
{
    Console.WriteLine("Block not found.");
}

```

### Roadmap

- **WatchServiceClient**: Implementation for watching transactions.
- **SubmitServiceClient**: Methods for submitting and monitoring transactions.
- **QueryServiceClient**: Interface for querying blockchain data and parameters.

### Contributing

Contributions are welcome! Please fork the repository and submit a pull request. For major changes, please open an issue first to discuss what you would like to change.

![Forks](https://img.shields.io/github/forks/utxorpc/dotnet-sdk.svg?style=social) 
![Stars](https://img.shields.io/github/stars/utxorpc/dotnet-sdk.svg?style=social) 
![Contributors](https://img.shields.io/github/contributors/utxorpc/dotnet-sdk.svg) 
![Issues](https://img.shields.io/github/issues/utxorpc/dotnet-sdk.svg) 
![Issues Closed](https://img.shields.io/github/issues-closed/utxorpc/dotnet-sdk.svg) 


