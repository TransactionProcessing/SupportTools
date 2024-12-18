using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Requests;
using SecurityService.DataTransferObjects.Responses;
using Shared.Results;
using SimpleResults;
using TransactionProcessor.SystemSetupTool.identityserverconfig;

namespace TransactionProcessor.SystemSetupTool;

public class IdentityServerFunctions{
    private readonly ISecurityServiceClient SecurityServiceClient;

    private readonly IdentityServerConfiguration identityServerConfiguration;

    public IdentityServerFunctions(ISecurityServiceClient securityServiceClient, IdentityServerConfiguration configuration){
        this.SecurityServiceClient = securityServiceClient;
        this.identityServerConfiguration = configuration;
    }

    private async Task<Result> CreateRoles(CancellationToken cancellationToken) {
        Result<List<RoleDetails>> rolesResult = await this.SecurityServiceClient.GetRoles(cancellationToken);
        if (rolesResult.IsFailed)
            return ResultHelpers.CreateFailure(rolesResult);

        List<RoleDetails> roles = rolesResult.Data;
        if (roles == null)
            roles = new List<RoleDetails>();

        foreach (String role in this.identityServerConfiguration.roles)
        {
            if (roles.Any(r => r.RoleName == role))
                continue;
            Result createResult = await this.CreateRole(role, CancellationToken.None);
            if (createResult.IsFailed)
                return ResultHelpers.CreateFailure(createResult);
        }

        return Result.Success();
    }

    private async Task<Result> CreateApiResources(CancellationToken cancellationToken) {
        var apiResourcesResult = await this.SecurityServiceClient.GetApiResources(cancellationToken);
        if (apiResourcesResult.IsFailed)
            return ResultHelpers.CreateFailure(apiResourcesResult);

        var apiResources = apiResourcesResult.Data;
        if (apiResources == null)
            apiResources = new List<ApiResourceDetails>();
        foreach (ApiResource apiResource in this.identityServerConfiguration.apiresources)
        {
            if (apiResources.Any(a => a.Name == apiResource.name))
                continue;
            var createResult = await this.CreateApiResource(apiResource, CancellationToken.None);
            if (createResult.IsFailed)
                return ResultHelpers.CreateFailure(createResult);
        }
        return Result.Success();
    }

    private async Task<Result> CreateIdentityResources(CancellationToken cancellationToken) {
        var identityResourcesResult = await this.SecurityServiceClient.GetIdentityResources(cancellationToken);
        if (identityResourcesResult.IsFailed)
            return ResultHelpers.CreateFailure(identityResourcesResult);

        var identityResources = identityResourcesResult.Data;
        if (identityResources == null)
            identityResources = new List<IdentityResourceDetails>();

        foreach (IdentityResource identityResource in this.identityServerConfiguration.identityresources)
        {
            if (identityResources.Any(i => i.Name == identityResource.name))
                continue;
            var createResult = await this.CreateIdentityResource(identityResource, CancellationToken.None);
            if (createResult.IsFailed)
                return ResultHelpers.CreateFailure(createResult);
        }
        return Result.Success();
    }

    private async Task<Result> CreateClients(CancellationToken cancellationToken) {
        var clientsResult = await this.SecurityServiceClient.GetClients(cancellationToken);
        if (clientsResult.IsFailed)
            return ResultHelpers.CreateFailure(clientsResult);

        var clients = clientsResult.Data;
        if (clients == null)
            clients = new List<ClientDetails>();
        foreach (identityserverconfig.Client client in this.identityServerConfiguration.clients)
        {
            if (clients.Any(c => c.ClientId == client.client_id))
                continue;
            var createResult = await this.CreateClient(client, CancellationToken.None);
            if (createResult.IsFailed)
                return ResultHelpers.CreateFailure(createResult);
        }

        return Result.Success();
    }

    private async Task<Result> CreateApiScopes(CancellationToken cancellationToken) {
        var apiScopesResult = await this.SecurityServiceClient.GetApiScopes(cancellationToken);
        if (apiScopesResult.IsFailed)
            return ResultHelpers.CreateFailure(apiScopesResult);
        var apiScopes = apiScopesResult.Data;
        if(apiScopes == null)
            apiScopes = new List<ApiScopeDetails>();
        foreach (ApiScope apiscope in this.identityServerConfiguration.apiscopes)
        {
            if (apiScopes.Any(a => a.Name== apiscope.name))
                continue;
            var createResult = await this.CreateApiScope(apiscope, CancellationToken.None);
            if (createResult.IsFailed)
                return ResultHelpers.CreateFailure(createResult);
        }
        return Result.Success();
    }

    public async Task<Result> CreateConfig(CancellationToken cancellationToken) {

        Result createRolesResult = await this.CreateRoles(cancellationToken);
        if (createRolesResult.IsFailed)
            return createRolesResult;

        Result createApiResourcesResult = await this.CreateApiResources(cancellationToken);
        if (createApiResourcesResult.IsFailed)
            return createApiResourcesResult;

        Result createIdentityResourcesResult = await this.CreateIdentityResources(cancellationToken);
        if (createIdentityResourcesResult.IsFailed)
            return createIdentityResourcesResult;

        Result createClientsResult = await this.CreateClients(cancellationToken);
        if (createClientsResult.IsFailed)
            return createClientsResult;

        Result createApiScopesResult = await this.CreateApiScopes(cancellationToken);
        if (createApiScopesResult.IsFailed)
            return createApiScopesResult;
        
        return Result.Success();
    }

    private async Task<Result> CreateRole(String role, CancellationToken cancellationToken){
            
        CreateRoleRequest createRoleRequest = new() {
            RoleName = role
        };

        return await this.SecurityServiceClient.CreateRole(createRoleRequest, cancellationToken);
    }

    private async Task<Result> CreateApiScope(ApiScope apiscope,
                                              CancellationToken cancellationToken)
    {
        CreateApiScopeRequest createApiScopeRequest = new CreateApiScopeRequest
        {
            Description = apiscope.description,
            DisplayName = apiscope.display_name,
            Name = apiscope.name
        };

        return await this.SecurityServiceClient.CreateApiScope(createApiScopeRequest, cancellationToken);
    }

    private async Task<Result> CreateIdentityResource(IdentityResource identityResource,
                                                      CancellationToken cancellationToken)
    {
        CreateIdentityResourceRequest createIdentityResourceRequest = new CreateIdentityResourceRequest
        {
            Claims = identityResource.claims,
            Description = identityResource.description,
            DisplayName = identityResource.displayName,
            Emphasize = identityResource.emphasize,
            Name = identityResource.name,
            Required = identityResource.required,
            ShowInDiscoveryDocument = identityResource.showInDiscoveryDocument
        };

        return await this.SecurityServiceClient.CreateIdentityResource(createIdentityResourceRequest, cancellationToken);
    }

    private async Task<Result> CreateClient(identityserverconfig.Client client, CancellationToken cancellationToken)
    {
        CreateClientRequest createClientRequest = new CreateClientRequest
        {
            AllowOfflineAccess = client.allow_offline_access.GetValueOrDefault(false),
            AllowedGrantTypes = client.allowed_grant_types,
            AllowedScopes = client.allowed_scopes,
            ClientDescription = client.client_description,
            ClientId = client.client_id,
            ClientName = client.client_name,
            ClientPostLogoutRedirectUris = client.client_post_logout_redirect_uris,
            ClientRedirectUris = client.client_redirect_uris,
            RequireConsent = client.require_consent.GetValueOrDefault(false),
            Secret = client.secret
        };
        return await this.SecurityServiceClient.CreateClient(createClientRequest, cancellationToken);
    }

    private async Task<Result> CreateApiResource(ApiResource apiResource,
                                                 CancellationToken cancellationToken)
    {
        CreateApiResourceRequest createApiResourceRequest = new CreateApiResourceRequest
        {
            Secret = apiResource.secret,
            Description = apiResource.description,
            DisplayName = apiResource.display_name,
            Name = apiResource.name,
            Scopes = apiResource.scopes,
            UserClaims = apiResource.user_claims
        };

        return await this.SecurityServiceClient.CreateApiResource(createApiResourceRequest, cancellationToken);
    }
}