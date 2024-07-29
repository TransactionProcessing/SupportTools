using System;
using System.Collections.Generic;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record TransactionJobConfig(String ClientId,
                                   String ClientSecret,
                                   String EstateManagementApi,
                                   String FileProcessorApi,
                                   String SecurityService,
                                   String TestHostApi,
                                   String TransactionProcessorApi, Guid EstateId, Guid MerchantId, Boolean IsLogon, List<String> ContractNames) : BaseConfiguration(ClientId, ClientSecret, EstateManagementApi, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);