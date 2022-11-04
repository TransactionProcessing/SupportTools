using EstateManagement.Client;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using SecurityService.Client;

namespace TransactionProcessing.SchedulerService.Jobs.Tests
{
    using Moq;

    public class UnitTest1
    {
        [Fact]
        public void Test1() {
            GenerateFileUploadsJob j = new GenerateFileUploadsJob();
        }
    }

    public class TestGenerateFileUploadsBootstrapper : BaseBoostrapper
    {
        private Mock<ISecurityServiceClient> securityServiceClient;

        private Mock<IEstateClient> estateClient;

        public TestGenerateFileUploadsBootstrapper() {
            this.securityServiceClient = new Mock<ISecurityServiceClient>();
            this.estateClient = new Mock<IEstateClient>();

        }
        public override void ConfigureServiceAdditional(IJobExecutionContext jobExecutionContext)
        {
            //this.Services.AddSingleton<ISecurityServiceClient, SecurityServiceClient>();
            //this.Services.AddSingleton<IEstateClient, EstateClient>();

            this.Services.AddSingleton<Func<String, String>>(container => serviceName => { return jobExecutionContext.MergedJobDataMap.GetString(serviceName); });
            this.Services.AddSingleton<ISecurityServiceClient>(this.securityServiceClient.Object);
            this.Services.AddSingleton<IEstateClient>(this.estateClient.Object);
        }
    }
}