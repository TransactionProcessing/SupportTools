namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class FileProfileOptions {
    public string FileProfileId { get; init; } = string.Empty;

    public string FileProcessorFileProfileId { get; init; } = string.Empty;

    public Guid GetFileProcessorFileProfileGuid() => Guid.Parse(this.FileProcessorFileProfileId);

    public string Format { get; init; } = FileProfileFormats.Delimited;

    public string FileExtension { get; init; } = "csv";

    public string? FileNamePattern { get; init; }

    public string? ContentType { get; init; }

    public DelimitedFileProfileOptions Delimited { get; init; } = new();

    public JsonFileProfileOptions Json { get; init; } = new();

    public List<FileFieldOptions> Fields { get; init; } = [];
}