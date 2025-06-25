using Xunit;
using Utxorpc.Sdk.Models;
using Utxorpc.Sdk.Models.Enums;

namespace Utxorpc.Sdk.Test;

public class SyncServiceClientTests
{
    private const string DOLOS_URL = "http://localhost:50051";
    private readonly SyncServiceClient _client;

    public SyncServiceClientTests()
    {
        _client = new SyncServiceClient(DOLOS_URL);
    }

    [Fact]
    public async Task FollowTip()
    {
        // Arrange
        var startPoint = new BlockRef(
            "6d1b288746ce3be63dcf68af9783282a0795c4d22eda4f5daef195f6034ccfc4",
            84194200UL
        );

        // Act - Get first few events
        var events = new List<NextResponse>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var response in _client.FollowTipAsync(startPoint, cancellationToken: cts.Token))
        {
            events.Add(response);
            if (events.Count >= 2) break; // Get first 2 events
        }

        // Assert
        Assert.True(events.Count >= 1, "Should receive at least one event");

        // First event should be a reset
        var firstEvent = events[0];
        Assert.Equal(NextResponseAction.Reset, firstEvent.Action);
        Assert.NotNull(firstEvent.ResetRef);
        Assert.Equal(84194200UL, firstEvent.ResetRef.Index);
        Assert.Equal("6d1b288746ce3be63dcf68af9783282a0795c4d22eda4f5daef195f6034ccfc4",
            firstEvent.ResetRef.Hash.ToLower());

        // If we got a second event, it should be an apply
        if (events.Count > 1)
        {
            var secondEvent = events[1];
            Assert.Equal(NextResponseAction.Apply, secondEvent.Action);
            Assert.NotNull(secondEvent.AppliedBlock);
            Assert.NotNull(secondEvent.AppliedBlock.Slot);
            Assert.True(secondEvent.AppliedBlock.Slot > 84194200UL);
        }
    }

    [Fact]
    public async Task ReadTip()
    {
        // Act
        var tip = await _client.ReadTipAsync();

        // Assert
        Assert.NotNull(tip);
        Assert.NotNull(tip.Hash);
        Assert.True(tip.Hash.Length > 0);
        Assert.True(tip.Index > 1);
        Assert.Equal(64, tip.Hash.Length); // Block hash should be 64 hex characters (32 bytes)
    }

    [Fact]
    public async Task FetchBlock()
    {
        // Arrange
        var blockRef = new BlockRef(
            "6d1b288746ce3be63dcf68af9783282a0795c4d22eda4f5daef195f6034ccfc4",
            84194200UL
        );

        // Act
        var block = await _client.FetchBlockAsync(blockRef);

        // Assert
        Assert.NotNull(block);
        Assert.NotNull(block.Slot);
        Assert.Equal(84194200UL, block.Slot);
        Assert.NotNull(block.Hash);
        Assert.Equal("6d1b288746ce3be63dcf68af9783282a0795c4d22eda4f5daef195f6034ccfc4",
            block.Hash.ToLower());

        // Native bytes should exist
        Assert.NotNull(block.NativeBytes);
    }

    [Fact]
    public async Task FetchHistory()
    {
        // Arrange
        var startRef = new BlockRef(
            "6d1b288746ce3be63dcf68af9783282a0795c4d22eda4f5daef195f6034ccfc4",
            84194200UL
        );
        var maxItems = 5u;

        // Act
        var response = await _client.DumpHistoryAsync(startRef, maxItems);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Blocks);
        Assert.NotEmpty(response.Blocks);
        Assert.True(response.Blocks.Count <= maxItems, $"Should return at most {maxItems} blocks");

        // First block should match our start reference
        var firstBlock = response.Blocks[0];
        Assert.NotNull(firstBlock.Slot);
        Assert.Equal(84194200UL, firstBlock.Slot);
        Assert.NotNull(firstBlock.Hash);
        Assert.Equal("6d1b288746ce3be63dcf68af9783282a0795c4d22eda4f5daef195f6034ccfc4",
            firstBlock.Hash.ToLower());

        // Blocks should be in chronological order (oldest to newest)
        for (int i = 1; i < response.Blocks.Count; i++)
        {
            Assert.True(response.Blocks[i].Slot > response.Blocks[i - 1].Slot,
                "Blocks should be in chronological order");
        }
    }
}