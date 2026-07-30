using System;
using System.Collections.Generic;

namespace TransactionProcessor.SystemSetupTool.fileprofileconfig;

public sealed class FileProcessingOptions
{
    public List<FileProfile> FileProfiles { get; init; } = [];
}

public sealed class FileProfile
{
    /// <summary>
    /// Unique identifier for the file profile.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Friendly name of the profile.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Directory to monitor for incoming files.
    /// </summary>
    public required string ListeningDirectory { get; init; }

    /// <summary>
    /// Request type that should be created for each record.
    /// </summary>
    public required string RequestType { get; init; }

    /// <summary>
    /// Name of the operator associated with the profile.
    /// </summary>
    public required string OperatorName { get; init; }

    /// <summary>
    /// Line terminator used in the file (e.g. "\n" or "\r\n").
    /// </summary>
    public required string LineTerminator { get; init; }

    /// <summary>
    /// Name of the class responsible for parsing the file.
    /// </summary>
    public required string FileFormatHandler { get; init; }
}