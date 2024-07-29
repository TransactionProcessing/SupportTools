using System;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record BaseConfiguration(
    String ClientId,
    String ClientSecret,
    String EstateManagementApi,
    String FileProcessorApi,
    String SecurityService,
    String TestHostApi,
    String TransactionProcessorApi);