namespace TransactionProcessor.SystemSetupTool.identityserverconfig
{
    using System;
    using System.Collections.Generic;

    public class IdentityServerConfiguration
    {
        public string managementclientsecret { get; set; }
        public string managementclientid { get; set; }
        public List<IdentityResource> identityresources { get; set; }
        public List<ApiResource> apiresources { get; set; }
        public List<Client> clients { get; set; }
        public List<ApiScope> apiscopes { get; set; }
        public List<String> roles { get; set; }
    }
}