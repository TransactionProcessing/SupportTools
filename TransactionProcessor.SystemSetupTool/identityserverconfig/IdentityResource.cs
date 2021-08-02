namespace TransactionProcessor.SystemSetupTool.identityserverconfig
{
    using System.Collections.Generic;

    public class IdentityResource
    {
        public string name { get; set; }
        public string description { get; set; }
        public string displayName { get; set; }
        public bool emphasize { get; set; }
        public bool required { get; set; }
        public bool showInDiscoveryDocument { get; set; }
        public List<string> claims { get; set; }
    }
}