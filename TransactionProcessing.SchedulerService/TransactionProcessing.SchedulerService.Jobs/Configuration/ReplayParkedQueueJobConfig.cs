using System;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record ReplayParkedQueueJobConfig(String ClientId,
                                         String ClientSecret,
                                         String EstateManagementApi,
                                         String FileProcessorApi,
                                         String SecurityService,
                                         String TestHostApi,
                                         String TransactionProcessorApi, String EventStoreAddress) : BaseConfiguration(ClientId, ClientSecret, EstateManagementApi, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);