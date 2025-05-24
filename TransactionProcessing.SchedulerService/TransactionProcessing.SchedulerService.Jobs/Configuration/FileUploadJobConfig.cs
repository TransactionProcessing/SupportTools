using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record FileUploadJobConfig(String ClientId,
                                  String ClientSecret,
                                  String FileProcessorApi,
                                  String SecurityService,
                                  String TestHostApi,
                                  String TransactionProcessorApi, Guid EstateId, Guid MerchantId, List<String> ContractNames, Guid UserId) : BaseConfiguration(ClientId, ClientSecret, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);