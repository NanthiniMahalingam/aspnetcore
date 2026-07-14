// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Components.Server;

/// <summary>
/// A base class for <see cref="AuthenticationStateProvider"/> services that receive an
/// authentication state from the host environment, and revalidate it at regular intervals.
/// </summary>
public abstract class RevalidatingServerAuthenticationStateProvider
    : ServerAuthenticationStateProvider, IDisposable
{
    private readonly ILogger _logger;

    // Serializes calls to ValidateAuthenticationStateAsync so that an on-demand revalidation
    // (RevalidateAsync) and the periodic revalidation loop never run concurrently.
    private readonly SemaphoreSlim _validationSemaphore = new SemaphoreSlim(1, 1);
    private readonly object _loopCancellationTokenSourceLock = new object();
    private CancellationTokenSource _loopCancellationTokenSource = new CancellationTokenSource();
    private bool _disposed;

    /// <summary>
    /// Constructs an instance of <see cref="RevalidatingServerAuthenticationStateProvider"/>.
    /// </summary>
    /// <param name="loggerFactory">A logger factory.</param>
    public RevalidatingServerAuthenticationStateProvider(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger<RevalidatingServerAuthenticationStateProvider>();

        // Whenever we receive notification of a new authentication state, cancel any
        // existing revalidation loop and start a new one
        AuthenticationStateChanged += authenticationStateTask =>
        {
            lock (_loopCancellationTokenSourceLock)
            {
                var oldCancellationTokenSource = _loopCancellationTokenSource;
                if (oldCancellationTokenSource is not null)
                {
                    oldCancellationTokenSource.Cancel();
                    oldCancellationTokenSource.Dispose();
                }

                _loopCancellationTokenSource = new CancellationTokenSource();
                _ = RevalidationLoop(authenticationStateTask, _loopCancellationTokenSource.Token);
            }
        };
    }

    /// <summary>
    /// Gets the interval between revalidation attempts.
    /// </summary>
    protected abstract TimeSpan RevalidationInterval { get; }

    /// <summary>
    /// Determines whether the authentication state is still valid.
    /// </summary>
    /// <param name="authenticationState">The current <see cref="AuthenticationState"/>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while performing the operation.</param>
    /// <returns>A <see cref="Task"/> that resolves as true if the <paramref name="authenticationState"/> is still valid, or false if it is not.</returns>
    protected abstract Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken);

    /// <summary>
    /// Immediately revalidates the current authentication state instead of waiting for the next
    /// scheduled revalidation defined by <see cref="RevalidationInterval"/>.
    /// </summary>
    /// <remarks>
    /// This performs the same validation as the periodic revalidation loop by invoking
    /// <see cref="ValidateAuthenticationStateAsync(AuthenticationState, CancellationToken)"/>. If the current user is
    /// not authenticated, this method completes without performing any validation. If the validation determines that the
    /// authentication state is no longer valid, the user is signed out and the authentication state is updated before the
    /// returned <see cref="Task"/> completes. When the authentication state remains valid, the periodic revalidation loop
    /// continues on its regular schedule; when the user is signed out, the current loop is stopped and restarted for the
    /// updated (anonymous) state, as happens for any authentication state change.
    /// </remarks>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while performing the operation.</param>
    /// <returns>A <see cref="Task"/> that completes when the revalidation has finished.</returns>
    /// <example>
    /// The following example shows how to trigger an on-demand revalidation from a component:
    /// <code>
    /// @inject AuthenticationStateProvider AuthenticationStateProvider
    ///
    /// @code {
    ///     private async Task RevalidateAsync()
    ///     {
    ///         if (AuthenticationStateProvider is RevalidatingServerAuthenticationStateProvider revalidatingProvider)
    ///         {
    ///             await revalidatingProvider.RevalidateAsync();
    ///         }
    ///     }
    /// }
    /// </code>
    /// </example>
    public async Task RevalidateAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource loopCancellationTokenSource;
        lock (_loopCancellationTokenSourceLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            loopCancellationTokenSource = _loopCancellationTokenSource;
        }

        var authenticationState = await GetAuthenticationStateAsync();
        if (authenticationState.User.Identity?.IsAuthenticated != true)
        {
            // There is nothing to revalidate for an unauthenticated user.
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            loopCancellationTokenSource.Token, cancellationToken);

        try
        {
            await RevalidateAndUpdateAsync(authenticationState, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The authentication state changed while revalidating (for example, the host supplied a
            // new state or the user was signed out). The state is already up to date, so we treat the
            // superseded revalidation as complete rather than surfacing the cancellation to the caller.
        }
    }

    private async Task RevalidationLoop(Task<AuthenticationState> authenticationStateTask, CancellationToken cancellationToken)
    {
        try
        {
            var authenticationState = await authenticationStateTask;
            if (authenticationState.User.Identity?.IsAuthenticated == true)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool isValid;

                    try
                    {
                        await Task.Delay(RevalidationInterval, cancellationToken);
                        isValid = await RevalidateAndUpdateAsync(authenticationState, cancellationToken);
                    }
                    catch (OperationCanceledException oce)
                    {
                        // If it was our cancellation token, then this revalidation loop gracefully completes
                        // Otherwise, treat it like any other failure
                        if (oce.CancellationToken == cancellationToken)
                        {
                            break;
                        }

                        throw;
                    }

                    if (!isValid)
                    {
                        // ForceSignOut was already performed inside RevalidateAndUpdateAsync.
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while revalidating authentication state");
            ForceSignOut();
        }
    }

    private async Task<bool> RevalidateAndUpdateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await _validationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var isValid = await ValidateAuthenticationStateAsync(authenticationState, cancellationToken);
            if (!isValid)
            {
                ForceSignOut();
            }

            return isValid;
        }
        finally
        {
            _validationSemaphore.Release();
        }
    }

    private void ForceSignOut()
    {
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var anonymousState = new AuthenticationState(anonymousUser);
        SetAuthenticationState(Task.FromResult(anonymousState));
    }

    void IDisposable.Dispose()
    {
        lock (_loopCancellationTokenSourceLock)
        {
            _disposed = true;
            _loopCancellationTokenSource?.Cancel();
        }

        _validationSemaphore.Dispose();

        Dispose(disposing: true);
    }

    /// <inheritdoc />
    protected virtual void Dispose(bool disposing)
    {
    }
}
