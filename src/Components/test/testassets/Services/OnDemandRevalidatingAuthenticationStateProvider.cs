// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Components.TestServer.Services;

// A self-contained RevalidatingServerAuthenticationStateProvider used to E2E test on-demand
// revalidation (RevalidateAsync). It seeds a signed-in user and exposes CredentialsAreValid so a
// test page can simulate credentials becoming invalid and then force an immediate revalidation.
public sealed class RevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    public RevalidatingAuthenticationStateProvider(ILoggerFactory loggerFactory)
        : base(loggerFactory)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "revalidation-user") },
            authenticationType: "TestAuth");

        SetAuthenticationState(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
    }

    // Long enough that the periodic loop never fires during the test; revalidation only happens on demand.
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    public bool CredentialsAreValid { get; set; } = true;

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
        => Task.FromResult(CredentialsAreValid);
}
