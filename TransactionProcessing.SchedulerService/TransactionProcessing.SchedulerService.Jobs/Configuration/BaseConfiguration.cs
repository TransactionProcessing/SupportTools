﻿using System;

namespace TransactionProcessing.SchedulerService.Jobs.Configuration;

public record BaseConfiguration(
    String ClientId,
    String ClientSecret,
    String FileProcessorApi,
    String SecurityService,
    String TestHostApi,
    String TransactionProcessorApi);