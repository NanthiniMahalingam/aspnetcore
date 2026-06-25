// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Components.Sections;

internal sealed class SectionRegistry
{
    private readonly Dictionary<object, SectionOutlet> _subscribersByIdentifier = new();
    private readonly Dictionary<object, List<SectionContent>> _providersByIdentifier = new();

    public void AddProvider(object identifier, SectionContent provider, bool isDefaultProvider)
    {
        if (!_providersByIdentifier.TryGetValue(identifier, out var providers))
        {
            providers = new();
            _providersByIdentifier.Add(identifier, providers);
        }

        if (isDefaultProvider)
        {
            providers.Insert(0, provider);
        }
        else
        {
            providers.Add(provider);
        }

        ValidateRenderModeCompatibility(identifier);
    }

    public void RemoveProvider(object identifier, SectionContent provider)
    {
        if (!_providersByIdentifier.TryGetValue(identifier, out var providers))
        {
            throw new InvalidOperationException($"There are no content providers with the given section ID '{identifier}'.");
        }

        var index = providers.LastIndexOf(provider);

        if (index < 0)
        {
            throw new InvalidOperationException($"The provider was not found in the providers list of the given section ID '{identifier}'.");
        }

        providers.RemoveAt(index);

        if (index == providers.Count)
        {
            // We just removed the most recently added provider, meaning we need to change
            // the current content to that of second most recently added provider.
            var contentProvider = GetCurrentProviderContentOrDefault(providers);
            NotifyContentChangedForSubscriber(identifier, contentProvider);
        }
    }

    public void Subscribe(object identifier, SectionOutlet subscriber)
    {
        if (_subscribersByIdentifier.ContainsKey(identifier))
        {
            throw new InvalidOperationException($"There is already a subscriber to the content with the given section ID '{identifier}'.");
        }

        // Notify the new subscriber with any existing content.
        var provider = GetCurrentProviderContentOrDefault(identifier);
        ValidateRenderModeCompatibility(identifier, subscriber, provider);
        subscriber.ContentUpdated(provider);

        _subscribersByIdentifier.Add(identifier, subscriber);
    }

    public void Unsubscribe(object identifier)
    {
        if (!_subscribersByIdentifier.Remove(identifier))
        {
            throw new InvalidOperationException($"The subscriber with the given section ID '{identifier}' is already unsubscribed.");
        }
    }

    public void NotifyContentProviderChanged(object identifier, SectionContent provider)
    {
        if (!_providersByIdentifier.TryGetValue(identifier, out var providers))
        {
            throw new InvalidOperationException($"There are no content providers with the given section ID '{identifier}'.");
        }

        // We only notify content changed for subscribers when the content of the
        // most recently added provider changes.
        if (providers.Count != 0 && providers[^1] == provider)
        {
            NotifyContentChangedForSubscriber(identifier, provider);
        }
    }

    private static SectionContent? GetCurrentProviderContentOrDefault(List<SectionContent> providers)
        => providers.Count != 0
            ? providers[^1]
            : null;

    private SectionContent? GetCurrentProviderContentOrDefault(object identifier)
        => _providersByIdentifier.TryGetValue(identifier, out var existingList)
            ? GetCurrentProviderContentOrDefault(existingList)
            : null;

    private void NotifyContentChangedForSubscriber(object identifier, SectionContent? provider)
    {
        if (_subscribersByIdentifier.TryGetValue(identifier, out var subscriber))
        {
            subscriber.ContentUpdated(provider);
        }
    }

    private void ValidateRenderModeCompatibility(object identifier)
    {
        // Only the most recently added provider supplies content to the outlet, so that is the
        // one whose render mode must be compatible with the subscribing outlet.
        if (identifier is string && _subscribersByIdentifier.TryGetValue(identifier, out var subscriber))
        {
            ValidateRenderModeCompatibility(identifier, subscriber, GetCurrentProviderContentOrDefault(identifier));
        }
    }

    // Sections work by sharing a RenderFragment between the SectionContent and the SectionOutlet. The content is
    // rendered at the SectionOutlet's location, so it inherits the SectionOutlet's render mode rather than the one
    // declared where the SectionContent lives. When those render modes differ, the content silently renders in the
    // wrong mode (for example, statically when the developer expected it to be interactive), which is almost never
    // what the developer intended. We detect that situation and throw an actionable error.
    //
    // This check is intentionally scoped to user-defined named sections (string SectionName). The built-in
    // PageTitle/HeadContent/HeadOutlet components identify their sections with internal object SectionId sentinels
    // and rely on flowing content from interactive components into a statically-rendered outlet, so they must not
    // be subject to this validation.
    private static void ValidateRenderModeCompatibility(object identifier, SectionOutlet? subscriber, SectionContent? provider)
    {
        if (identifier is not string sectionName || subscriber is null || provider is null)
        {
            return;
        }

        var outletRenderMode = subscriber.RenderMode;
        var contentRenderMode = provider.RenderMode;

        if (RenderModesAreCompatible(outletRenderMode, contentRenderMode))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The content provided to the section '{sectionName}' uses the render mode '{DescribeRenderMode(contentRenderMode)}', " +
            $"but the matching '{nameof(SectionOutlet)}' uses the render mode '{DescribeRenderMode(outletRenderMode)}'. " +
            $"A '{nameof(SectionContent)}' and its matching '{nameof(SectionOutlet)}' must use the same render mode, because " +
            $"the section content is rendered at the location of the '{nameof(SectionOutlet)}'. To fix this, ensure the " +
            $"'{nameof(SectionContent)}' and the '{nameof(SectionOutlet)}' for the section '{sectionName}' use the same render mode.");
    }

    private static bool RenderModesAreCompatible(IComponentRenderMode? outletRenderMode, IComponentRenderMode? contentRenderMode)
    {
        if (outletRenderMode is null && contentRenderMode is null)
        {
            // Both are statically rendered.
            return true;
        }

        if (outletRenderMode is null || contentRenderMode is null)
        {
            // One is interactive and the other is static.
            return false;
        }

        // Both are interactive. Treating equal render mode types as compatible avoids false positives while still
        // catching the unambiguous cases (such as InteractiveServer content under an InteractiveWebAssembly outlet).
        return outletRenderMode.GetType() == contentRenderMode.GetType();
    }

    private static string DescribeRenderMode(IComponentRenderMode? renderMode)
        => renderMode is null ? "static rendering" : renderMode.GetType().Name;
}
