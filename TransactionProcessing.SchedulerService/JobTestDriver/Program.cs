namespace JobTestDriver
{
    using MessagingService.Client;
    using TransactionProcessing.SchedulerService.Jobs;

    internal class Program{
        static async Task Main(string[] args){
            //List<(String groupName, String streamName, Int64 parkedMessageCount)>? info = await Jobs.GetParkedQueueInformation("esdb://admin:changeit@192.168.0.133:2113?tls=false&tlsVerifyCert=false", CancellationToken.None);
            //foreach ((String groupName, String streamName, Int64 parkedMessageCount) infoItem in info){
            //    Console.WriteLine($"Group: {infoItem.groupName} Stream: {infoItem.streamName} Parked Count: {infoItem.parkedMessageCount}");
            //}

            //List<(Guid, String, DateTime, String)>? incompleteFiles = await Jobs.GetIncompleteFileList("server=192.168.0.133;user id=sa;password=Sc0tland;database=EstateReportingReadModel<estateid>;Encrypt=True;TrustServerCertificate=True", CancellationToken.None);
            //foreach ((Guid, String, DateTime, String) incompleteFile in incompleteFiles){
            //    Console.WriteLine($"FileId: {incompleteFile.Item1} Location: {incompleteFile.Item2} Rcvd: {incompleteFile.Item3} Merchant: {incompleteFile.Item4}");
            //}
            HttpClient client = new HttpClient();
            IMessagingServiceClient messagingServiceClient = new MessagingServiceClient(delegate(String s){ return "http://127.0.0.1:5006";}, client);
            String accessToken = "eyJhbGciOiJSUzI1NiIsImtpZCI6IjQzOUMxRDk5MDUwQTYyMDhEM0U5M0JFMjlBQUJBNzI5IiwidHlwIjoiYXQrand0In0.eyJpc3MiOiJodHRwczovLzEyNy4wLjAuMTo1MDAxIiwibmJmIjoxNjgzMTA2NDE2LCJpYXQiOjE2ODMxMDY0MTYsImV4cCI6MTY4MzExMDAxNiwiYXVkIjpbImVzdGF0ZU1hbmFnZW1lbnQiLCJlc3RhdGVSZXBvcnRpbmciLCJmaWxlUHJvY2Vzc29yIiwibWVzc2FnaW5nU2VydmljZSIsInRyYW5zYWN0aW9uUHJvY2Vzc29yIiwidHJhbnNhY3Rpb25Qcm9jZXNzb3JBQ0wiLCJ2b3VjaGVyTWFuYWdlbWVudCIsImh0dHBzOi8vMTI3LjAuMC4xOjUwMDEvcmVzb3VyY2VzIl0sInNjb3BlIjpbImVzdGF0ZU1hbmFnZW1lbnQiLCJlc3RhdGVSZXBvcnRpbmciLCJmaWxlUHJvY2Vzc29yIiwibWVzc2FnaW5nU2VydmljZSIsInRyYW5zYWN0aW9uUHJvY2Vzc29yIiwidHJhbnNhY3Rpb25Qcm9jZXNzb3JBQ0wiLCJ2b3VjaGVyTWFuYWdlbWVudCJdLCJjbGllbnRfaWQiOiJzZXJ2aWNlQ2xpZW50IiwianRpIjoiMDZBMUI4NzYyRjFGNDJGNkIwMzM5RTYwRTk2MkVDQkUifQ.G5pFWRJF430ZZxnGO_yIxEC6Zj81LRr3HNq6d8V9EV4Pswp5YO7hZ867Ln4mjnrYag4lGI4cpT5S6646r9KNdZMiLOsdQs2LEJPuUjEdVADIwm8rcdqT8OX-sC6uGA6VL0bMmYWQXw1E8d4kax444I6jeNquLjpoWVD1BDp9L1zzC6e_k9W7Fc9MQOogOqO82TXrBl9nkpBbmJQ0HDiub2yVUTUKLwkCeRfBDlyeU8tyNE7kH6IGdHIL_WYUtiiRYjBJ2PNLzTtrXQk4rqw6GB-25K2qcgP5FO0MI675tAkuPKI0DaySXHnAjYssW8wZYy0tkaJL0OIlmOUe-9jM6g";
            await Jobs.SendSupportEmail(DateTime.Now,
                                        accessToken,
                                        "esdb://admin:changeit@192.168.0.133:2113?tls=false&tlsVerifyCert=false",
                                        "server=192.168.0.133;user id=sa;password=Sc0tland;database=EstateReportingReadModel<estateid>;Encrypt=True;TrustServerCertificate=True",
                                        new List<String>{
                                                          "435613ac-a468-47a3-ac4f-649d89764c22"
                                                      },
                                        messagingServiceClient,
                                        CancellationToken.None);

        }
    }
}