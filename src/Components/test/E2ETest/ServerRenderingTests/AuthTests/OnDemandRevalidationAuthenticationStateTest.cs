// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Components.TestServer.RazorComponents;
using Microsoft.AspNetCore.Components.E2ETest.Infrastructure;
using Microsoft.AspNetCore.Components.E2ETest.Infrastructure.ServerFixtures;
using Microsoft.AspNetCore.E2ETesting;
using OpenQA.Selenium;
using TestServer;
using Xunit.Abstractions;

namespace Microsoft.AspNetCore.Components.E2ETests.ServerRenderingTests.AuthTests;

public class RevalidationAuthenticationStateTest
    : ServerTestBase<BasicTestAppServerSiteFixture<RazorComponentEndpointsStartup<App>>>
{
    public RevalidationAuthenticationStateTest(
        BrowserFixture browserFixture,
        BasicTestAppServerSiteFixture<RazorComponentEndpointsStartup<App>> serverFixture,
        ITestOutputHelper output)
        : base(browserFixture, serverFixture, output)
    {
    }

    [Fact]
    public void RevalidateAsync_WhenCredentialsRemainValid_KeepsUserSignedIn()
    {
        NavigateToRevalidationPage();

        // Revalidating while the credentials are still valid keeps the user signed in.
        Browser.Click(By.Id("revalidate-now"));

        Browser.Equal("True", () => Browser.FindElement(By.Id("identity-authenticated")).Text);
        Browser.Equal("revalidation-user", () => Browser.FindElement(By.Id("identity-name")).Text);
    }

    [Fact]
    public void RevalidateAsync_WhenCredentialsBecomeInvalid_SignsUserOut()
    {
        NavigateToRevalidationPage();

        // Invalidating the credentials does not sign the user out on its own; the user stays signed in
        // until the next revalidation (this is the Blazor Server behavior the feature addresses).
        Browser.Click(By.Id("invalidate-credentials"));
        Browser.Equal("False", () => Browser.FindElement(By.Id("credentials-valid")).Text);
        Browser.Equal("True", () => Browser.FindElement(By.Id("identity-authenticated")).Text);

        // Forcing an on-demand revalidation signs the user out immediately.
        Browser.Click(By.Id("revalidate-now"));

        Browser.Equal("False", () => Browser.FindElement(By.Id("identity-authenticated")).Text);
        Browser.Equal("", () => Browser.FindElement(By.Id("identity-name")).Text);
    }

    private void NavigateToRevalidationPage()
    {
        Navigate($"{ServerPathBase}/auth/on-demand-revalidation-authentication-state");

        // Wait until the circuit is established and the page is interactive before acting.
        Browser.Equal("True", () => Browser.FindElement(By.Id("is-interactive")).Text);
        Browser.Equal("True", () => Browser.FindElement(By.Id("identity-authenticated")).Text);
        Browser.Equal("revalidation-user", () => Browser.FindElement(By.Id("identity-name")).Text);
    }
}
