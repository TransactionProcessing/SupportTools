using System;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record SettlementJobConfig(String ClientId,
                                  String ClientSecret,
                                  String EstateManagementApi,
                                  String FileProcessorApi,
                                  String SecurityService,
                                  String TestHostApi,
                                  String TransactionProcessorApi, Guid EstateId) : BaseConfiguration(ClientId, ClientSecret, EstateManagementApi, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);