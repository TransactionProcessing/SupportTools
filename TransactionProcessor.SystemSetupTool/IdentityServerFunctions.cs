using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecurityService.Client;
using SecurityService.DataTransferObjects;
using TransactionProcessing.UnionResults;
using TransactionProcessor.SystemSetupTool.identityserverconfig;

namespace TransactionProcessor.SystemSetupTool;

public class IdentityServerFunctions{
    private readonly ISecurityServiceClient SecurityServiceClient;

    private readonly IdentityServerConfiguration identityServerConfiguration;

    private TokenResponse ManagmementToken;

    public IdentityServerFunctions(ISecurityServiceClient securityServiceClient, IdentityServerConfiguration configuration){
        this.SecurityServiceClient = securityServiceClient;
        this.identityServerConfiguration = configuration;
    }

    private async Task<Result> GetManagementToken(CancellationToken cancellationToken) {
        var result = await this.SecurityServiceClient.GetToken(this.identityServerConfiguration.managementclientid, this.identityServerConfiguration.managementclientsecret, cancellationToken);

        if (result.Value is not Success<TokenResponse> { Value: { } token })
            return Results.Fail("Error getting management token");

        this.ManagmementToken = token;

        return Results.Ok();
    }

    private async Task<Result> CreateRoles(CancellationToken cancellationToken) {
        Result<List<RoleResponse>> rolesResult = await this.SecurityServiceClient.GetRoles(this.ManagmementToken.AccessToken, cancellationToken);
        if (rolesResult.Value is not Success<List<RoleResponse>> { Value: var roles })
            return rolesResult.WithoutPayload();

        roles ??= [];

        foreach (String role in this.identityServerConfiguration.roles)
        {
            if (roles.Any(r => r.RoleName == role))
                continue;
            Result createResult = await this.CreateRole(role, CancellationToken.None);
            if (!createResult.IsSuccess())
                return createResult;
        }

        return Results.Ok();
    }

    private async Task<Result> CreateApiResources(CancellationToken cancellationToken) {
        var apiResourcesResult = await this.SecurityServiceClient.GetApiResources(this.ManagmementToken.AccessToken, cancellationToken);
        if (apiResourcesResult.Value is not Success<List<ApiResourceResponse>> { Value: var apiResources })
            return apiResourcesResult.WithoutPayload();

        apiResources ??= [];
        foreach (ApiResource apiResource in this.identityServerConfiguration.apiresources)
        {
            if (apiResources.Any(a => a.Name == apiResource.name))
                continue;
            var createResult = await this.CreateApiResource(apiResource, CancellationToken.None);
            if (!createResult.IsSuccess())
                return createResult;
        }
        return Results.Ok();
    }

    private async Task<Result> CreateIdentityResources(CancellationToken cancellationToken) {
        var identityResourcesResult = await this.SecurityServiceClient.GetIdentityResources(this.ManagmementToken.AccessToken, cancellationToken);
        if (identityResourcesResult.Value is not Success<List<IdentityResourceResponse>> { Value: var identityResources })
            return identityResourcesResult.WithoutPayload();

        identityResources ??= [];

        foreach (IdentityResource identityResource in this.identityServerConfiguration.identityresources)
        {
            if (identityResources.Any(i => i.Name == identityResource.name))
                continue;
            var createResult = await this.CreateIdentityResource(identityResource, CancellationToken.None);
            if (!createResult.IsSuccess())
                return createResult;
        }
        return Results.Ok();
    }

    private async Task<Result> CreateClients(CancellationToken cancellationToken) {
        var clientsResult = await this.SecurityServiceClient.GetClients(this.ManagmementToken.AccessToken, cancellationToken);
        if (clientsResult.Value is not Success<List<ClientResponse>> { Value: var clients })
            return clientsResult.WithoutPayload();

        clients ??= [];
        foreach (identityserverconfig.Client client in this.identityServerConfiguration.clients)
        {
            if (clients.Any(c => c.ClientId == client.client_id))
                continue;
            var createResult = await this.CreateClient(client, CancellationToken.None);
            if (!createResult.IsSuccess())
                return createResult;
        }

        return Results.Ok();
    }

    private async Task<Result> CreateApiScopes(CancellationToken cancellationToken) {
        var apiScopesResult = await this.SecurityServiceClient.GetApiScopes(this.ManagmementToken.AccessToken, cancellationToken);
        if (apiScopesResult.Value is not Success<List<ApiScopeResponse>> { Value: var apiScopes })
            return apiScopesResult.WithoutPayload();
        apiScopes ??= [];
        foreach (ApiScope apiscope in this.identityServerConfiguration.apiscopes)
        {
            if (apiScopes.Any(a => a.Name== apiscope.name))
                continue;
            var createResult = await this.CreateApiScope(apiscope, CancellationToken.None);
            if (!createResult.IsSuccess())
                return createResult;
        }
        return Results.Ok();
    }

    public async Task<Result> CreateConfig(CancellationToken cancellationToken) {

        var managementTokenResult = await this.GetManagementToken(cancellationToken);
        if (!managementTokenResult.IsSuccess())
            return managementTokenResult;

        Result createRolesResult = await this.CreateRoles(cancellationToken);
        if (!createRolesResult.IsSuccess())
            return createRolesResult;

        Result createApiResourcesResult = await this.CreateApiResources(cancellationToken);
        if (!createApiResourcesResult.IsSuccess())
            return createApiResourcesResult;

        Result createIdentityResourcesResult = await this.CreateIdentityResources(cancellationToken);
        if (!createIdentityResourcesResult.IsSuccess())
            return createIdentityResourcesResult;

        Result createClientsResult = await this.CreateClients(cancellationToken);
        if (!createClientsResult.IsSuccess())
            return createClientsResult;

        Result createApiScopesResult = await this.CreateApiScopes(cancellationToken);
        if (!createApiScopesResult.IsSuccess())
            return createApiScopesResult;

        return Results.Ok();
    }

    private async Task<Result> CreateRole(String role, CancellationToken cancellationToken){
            
        CreateRoleRequest createRoleRequest = new() {
            RoleName = role
        };

        return await this.SecurityServiceClient.CreateRole(this.ManagmementToken.AccessToken, createRoleRequest, cancellationToken);
    }

    private async Task<Result> CreateApiScope(ApiScope apiscope,
                                              CancellationToken cancellationToken)
    {
        CreateApiScopeRequest createApiScopeRequest = new CreateApiScopeRequest
        {
            Description = apiscope.description,
            DisplayName = apiscope.displayName,
            Name = apiscope.name
        };

        return await this.SecurityServiceClient.CreateApiScope(this.ManagmementToken.AccessToken, createApiScopeRequest, cancellationToken);
    }

    private async Task<Result> CreateIdentityResource(IdentityResource identityResource,
                                                      CancellationToken cancellationToken)
    {
        CreateIdentityResourceRequest createIdentityResourceRequest = new CreateIdentityResourceRequest
        {
            Claims = identityResource.claims,
            Description = identityResource.description,
            DisplayName = identityResource.display_name,
            Emphasize = identityResource.emphasize,
            Name = identityResource.name,
            Required = identityResource.required,
            ShowInDiscoveryDocument = identityResource.showInDiscoveryDocument
        };

        return await this.SecurityServiceClient.CreateIdentityResource(this.ManagmementToken.AccessToken, createIdentityResourceRequest, cancellationToken);
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
        return await this.SecurityServiceClient.CreateClient(this.ManagmementToken.AccessToken, createClientRequest, cancellationToken);
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

        return await this.SecurityServiceClient.CreateApiResource(this.ManagmementToken.AccessToken, createApiResourceRequest, cancellationToken);
    }
}
