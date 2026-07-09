namespace TransactionProcessing.MerchantFileProcessor.Services;

public sealed record FileProcessingStatusSnapshot(bool ProcessingCompleted,
                                                  IReadOnlyList<FileProcessingLineStatusSnapshot> Lines);

public sealed record FileProcessingLineStatusSnapshot(int LineNumber,
                                                      string? LineData,
                                                      string ProcessingStatus,
                                                      string? RejectionReason,
                                                      Guid? TransactionId);
