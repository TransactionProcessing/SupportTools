using System;
using System.Collections.Generic;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record MakeFloatCreditsJobConfig(String ClientId,
                                        String ClientSecret,
                                        String FileProcessorApi,
                                        String SecurityService,
                                        String TestHostApi,
                                        String TransactionProcessorApi,
                                        Guid EstateId,
                                        List<DepositAmount> DepositAmounts) : BaseConfiguration(ClientId, ClientSecret, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);


public record DepositAmount(Guid ContractId, Guid ProductId, Decimal Amount);