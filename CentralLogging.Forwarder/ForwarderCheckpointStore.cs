using System.Text.Json;

namespace CentralLogging.Forwarder;

public sealed class ForwarderCheckpointStore
{
    private readonly SemaphoreSlim Gate = new(1, 1);
    private Dictionary<String, FileCheckpoint>? Checkpoints;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Int64> GetOffsetAsync(String path,
                                            String fileIdentity,
                                            String checkpointPath,
                                            CancellationToken cancellationToken)
    {
        await this.Gate.WaitAsync(cancellationToken);
        try
        {
            await this.LoadAsync(checkpointPath, cancellationToken);
            Dictionary<String, FileCheckpoint> checkpoints = this.Checkpoints!;
            if (checkpoints.TryGetValue(path, out FileCheckpoint? atPath))
            {
                if (String.IsNullOrEmpty(atPath.FileIdentity)) return 0;
                if (atPath.FileIdentity == fileIdentity) return atPath.ByteOffset;
            }

            return checkpoints.Values.FirstOrDefault(entry => entry.FileIdentity == fileIdentity)?.ByteOffset ?? 0;
        }
        finally
        {
            this.Gate.Release();
        }
    }

    public async Task SaveOffsetAsync(String path,
                                      String fileIdentity,
                                      Int64 offset,
                                      String checkpointPath,
                                      CancellationToken cancellationToken)
    {
        await this.Gate.WaitAsync(cancellationToken);
        try
        {
            await this.LoadAsync(checkpointPath, cancellationToken);
            Dictionary<String, FileCheckpoint> checkpoints = this.Checkpoints!;
            foreach (String oldPath in checkpoints.Where(pair => pair.Value.FileIdentity == fileIdentity &&
                                                                       !String.Equals(pair.Key, path, StringComparison.OrdinalIgnoreCase))
                                                         .Select(pair => pair.Key).ToArray())
                checkpoints.Remove(oldPath);

            checkpoints[path] = new FileCheckpoint { FileIdentity = fileIdentity, ByteOffset = offset };
            await this.PersistAsync(checkpointPath, cancellationToken);
        }
        finally
        {
            this.Gate.Release();
        }
    }

    public async Task RemoveUnseenAsync(HashSet<String> seenFileIdentities,
                                        HashSet<String> discoveredPaths,
                                        String checkpointPath,
                                        CancellationToken cancellationToken)
    {
        await this.Gate.WaitAsync(cancellationToken);
        try
        {
            await this.LoadAsync(checkpointPath, cancellationToken);
            Dictionary<String, FileCheckpoint> checkpoints = this.Checkpoints!;
            String[] stalePaths = checkpoints.Where(pair =>
                    String.IsNullOrEmpty(pair.Value.FileIdentity)
                        ? !discoveredPaths.Contains(pair.Key)
                        : !seenFileIdentities.Contains(pair.Value.FileIdentity))
                .Select(pair => pair.Key)
                .ToArray();

            if (stalePaths.Length == 0) return;
            foreach (String stalePath in stalePaths) checkpoints.Remove(stalePath);
            await this.PersistAsync(checkpointPath, cancellationToken);
        }
        finally
        {
            this.Gate.Release();
        }
    }

    private async Task PersistAsync(String checkpointPath,
                                    CancellationToken cancellationToken)
    {
        String fullPath = Path.GetFullPath(checkpointPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        String temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream,
                                                     this.Checkpoints,
                                                     SerializerOptions,
                                                     cancellationToken);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task LoadAsync(String checkpointPath,
                                 CancellationToken cancellationToken)
    {
        if (this.Checkpoints != null) return;

        this.Checkpoints = new Dictionary<String, FileCheckpoint>(StringComparer.OrdinalIgnoreCase);
        String fullPath = Path.GetFullPath(checkpointPath);
        if (!File.Exists(fullPath)) return;

        await using FileStream stream = File.OpenRead(fullPath);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out Int64 legacyOffset))
            {
                this.Checkpoints[property.Name] = new FileCheckpoint { ByteOffset = legacyOffset };
                continue;
            }

            FileCheckpoint? entry = property.Value.Deserialize<FileCheckpoint>(SerializerOptions);
            if (entry != null) this.Checkpoints[property.Name] = entry;
        }
    }

    public sealed class FileCheckpoint
    {
        public FileCheckpoint() { }

        public String FileIdentity { get; init; } = "";
        public Int64 ByteOffset { get; init; }
    }
}
