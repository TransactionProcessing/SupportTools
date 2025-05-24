using System;
using Microsoft.Extensions.Logging;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record ReplayParkedQueueJobConfig(String ClientId,
                                         String ClientSecret,
                                         String FileProcessorApi,
                                         String SecurityService,
                                         String TestHostApi,
                                         String TransactionProcessorApi, String EventStoreAddress) : BaseConfiguration(ClientId, ClientSecret, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);