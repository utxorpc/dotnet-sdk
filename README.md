<div align="center">
  <h1 style="font-size: 3em;">UTxORPC.Sdk | .NET 🚀</h1>
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

### ✨ Features

- **gRPC Communication**: Utilizes **gRPC** for fast and efficient communication with blockchain nodes, offering high performance and low latency.
- **FetchBlockAsync**: Retrieve blocks by hash and index.
- **FollowTipAsync**: Stream real-time updates as new blocks are applied, undone, or when the chain is reset.
- **Future Expansion**: Additional methods will be provided for watching transactions, submitting transactions, querying data, and more.

### 📦 Installation

To install the SDK, use the following command in the NuGet Package Manager:

```bash
dotnet add package Utxorpc.Sdk --version 1.2.0-alpha
```

Alternatively, you can install it via the NuGet Package Manager in Visual Studio.

### 💻 Usage

#### SyncServiceClient Example

Below is an example of how to use the `SyncServiceClient` to fetch blocks and follow chain tips. I've included steps for each part of the process.

```cs
using Utxorpc.Sdk;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;

// Step 1: Create a SyncServiceClient instance
// Instantiate the SyncServiceClient with the URL to the UTxO RPC service (e.g., localhost for local testing).
var client = new SyncServiceClient(
    url: "http://localhost:50051" // Change to your UTxO RPC service URL
);

// Step 2: Follow chain tips by fetching real-time block updates
// This example demonstrates following the chain and reacting to block events like apply, undo, or reset.
await foreach (NextResponse? response in client.FollowTipAsync(
    new BlockRef
    (
        "b977e548f3364b114505f3311a10f89e5f5cf47e815765bce6750a5de48e5951", // Example block hash
        58717900 // Example block index (slot)
    )))
{
    // Step 3: Handle different types of responses
    switch (response.Action)
    {
        case NextResponseAction.Apply:
            Block? applyBlock = response.AppliedBlock;
            if (applyBlock is not null)
            {
                // Step 4: Handle Apply block (when a new block is added to the chain)
                Console.WriteLine($"Apply Block: {applyBlock.Hash} Slot: {applyBlock.Slot}");
            }
            break;

        case NextResponseAction.Undo:
            Block? undoBlock = response.UndoneBlock;
            if (undoBlock is not null)
            {
                // Step 5: Handle Undo block (when a block is removed from the chain)
                Console.WriteLine($"Undo Block: {undoBlock.Hash} Slot: {undoBlock.Slot}");
            }
            break;

        case NextResponseAction.Reset:
            BlockRef? resetRef = response.ResetRef;
            if (resetRef is not null)
            {
                // Step 6: Handle Chain reset (when the blockchain is reset to a previous state)
                Console.WriteLine($"Reset to Block: {resetRef.Hash} Slot: {resetRef.Index}");
            }
            break;
    }
}
```

### 🛠️ Roadmap

- **WatchServiceClient**: Implementation for watching transactions.
- **SubmitServiceClient**: Methods for submitting and monitoring transactions.
- **QueryServiceClient**: Interface for querying blockchain data and parameters.

### 🤝 Contributing

Contributions are welcome! Please fork the repository and submit a pull request. For major changes, please open an issue first to discuss what you would like to change.

### 💬 Join the Community

If you want to discuss UTxO RPC or get involved in the community, join the **TxPipe Discord**! There's a dedicated channel for UTxO RPC where you can connect with other developers, share ideas, and get support.

👉 [Join the TxPipe Discord here!](https://discord.gg/nbkJdPnKHm) 💬
