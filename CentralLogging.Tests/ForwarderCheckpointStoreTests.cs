using System.Text.Json;
using CentralLogging.Forwarder;
using Shouldly;

namespace CentralLogging.Tests;

public sealed class ForwarderCheckpointStoreTests
{
    [Fact]
    public async Task Offset_follows_file_identity_when_rotation_renames_the_file()
    {
        using TemporaryDirectory directory = new();
        String checkpointPath = Path.Combine(directory.Path, "checkpoint.json");
        ForwarderCheckpointStore store = new();

        await store.SaveOffsetAsync("C:/logs/current.json", "file-one", 143, checkpointPath, CancellationToken.None);
        Int64 offset = await store.GetOffsetAsync("C:/logs/current.json.1", "file-one", checkpointPath, CancellationToken.None);

        offset.ShouldBe(143);
    }

    [Fact]
    public async Task Saving_renamed_file_removes_its_old_path()
    {
        using TemporaryDirectory directory = new();
        String checkpointPath = Path.Combine(directory.Path, "checkpoint.json");
        ForwarderCheckpointStore store = new();

        await store.SaveOffsetAsync("C:/logs/current.json", "file-one", 143, checkpointPath, CancellationToken.None);
        await store.SaveOffsetAsync("C:/logs/current.json.1", "file-one", 143, checkpointPath, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(checkpointPath));
        document.RootElement.TryGetProperty("C:/logs/current.json", out _).ShouldBeFalse();
        document.RootElement.TryGetProperty("C:/logs/current.json.1", out _).ShouldBeTrue();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public String Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(this.Path);

        public void Dispose() => Directory.Delete(this.Path, recursive: true);
    }
}
