using System;
using System.Collections.Generic;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record TransactionJobConfig(String ClientId,
                                   String ClientSecret,
                                   String FileProcessorApi,
                                   String SecurityService,
                                   String TestHostApi,
                                   String TransactionProcessorApi, Guid EstateId, Guid MerchantId, Boolean IsLogon, List<String> ContractNames) : BaseConfiguration(ClientId, ClientSecret, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);