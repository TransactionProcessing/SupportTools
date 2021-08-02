namespace TransactionProcessor.SystemSetupTool.identityserverconfig
{
    using System.Collections.Generic;

    public class Client
    {
        public string client_id { get; set; }
        public string secret { get; set; }
        public string client_name { get; set; }
        public string client_description { get; set; }
        public List<string> allowed_scopes { get; set; }
        public List<string> allowed_grant_types { get; set; }
        public List<string> client_redirect_uris { get; set; }
        public List<string> client_post_logout_redirect_uris { get; set; }
        public bool? require_consent { get; set; }
        public bool? allow_offline_access { get; set; }
    }
}