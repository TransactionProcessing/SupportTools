using System;
using System.Collections.Generic;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record FileUploadJobConfig(String ClientId,
                                  String ClientSecret,
                                  String EstateManagementApi,
                                  String FileProcessorApi,
                                  String SecurityService,
                                  String TestHostApi,
                                  String TransactionProcessorApi, Guid EstateId, Guid MerchantId, List<String> ContractNames, Guid UserId) : BaseConfiguration(ClientId, ClientSecret, EstateManagementApi, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);