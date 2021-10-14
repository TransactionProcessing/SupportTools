namespace TransactionProcessor.SystemSetupTool.identityserverconfig
{
    using System.Collections.Generic;

    public class ApiResource
    {
        public string secret { get; set; }
        public string name { get; set; }
        public string display_name { get; set; }
        public string description { get; set; }
        public List<string> scopes { get; set; }
        public List<string> user_claims { get; set; }
    }
}