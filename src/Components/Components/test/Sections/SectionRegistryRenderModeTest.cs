// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Test.Helpers;

namespace Microsoft.AspNetCore.Components.Sections;

public class SectionRegistryRenderModeTest
{
    private const string SectionName = "test-section";

    [Fact]
    public void InteractiveContentWithStaticOutlet_Throws()
    {
        // This is the scenario from https://github.com/dotnet/aspnetcore/issues/51132:
        // the SectionContent is interactive but the matching SectionOutlet is static,
        // so the content silently renders statically and is not interactive.

        // Arrange
        var renderer = new SectionRenderModeRenderer(outletRenderMode: null, contentRenderMode: new ServerRenderMode());
        var host = new SectionHost { SectionName = SectionName };

        // Act
        renderer.AssignRootComponentId(host);
        host.TriggerRender();

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(Assert.Single(renderer.HandledExceptions));
        Assert.Contains(SectionName, exception.Message);
        Assert.Contains("same render mode", exception.Message);
    }

    [Fact]
    public void StaticContentWithInteractiveOutlet_Throws()
    {
        // Arrange
        var renderer = new SectionRenderModeRenderer(outletRenderMode: new ServerRenderMode(), contentRenderMode: null);
        var host = new SectionHost { SectionName = SectionName };

        // Act
        renderer.AssignRootComponentId(host);
        host.TriggerRender();

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(Assert.Single(renderer.HandledExceptions));
        Assert.Contains(SectionName, exception.Message);
        Assert.Contains("same render mode", exception.Message);
    }

    [Fact]
    public void ServerContentWithWebAssemblyOutlet_Throws()
    {
        // Arrange
        var renderer = new SectionRenderModeRenderer(outletRenderMode: new WebAssemblyRenderMode(), contentRenderMode: new ServerRenderMode());
        var host = new SectionHost { SectionName = SectionName };

        // Act
        renderer.AssignRootComponentId(host);
        host.TriggerRender();

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(Assert.Single(renderer.HandledExceptions));
        Assert.Contains(SectionName, exception.Message);
        Assert.Contains(nameof(ServerRenderMode), exception.Message);
        Assert.Contains(nameof(WebAssemblyRenderMode), exception.Message);
    }

    [Fact]
    public void WebAssemblyContentWithServerOutlet_Throws()
    {
        // Arrange
        var renderer = new SectionRenderModeRenderer(outletRenderMode: new ServerRenderMode(), contentRenderMode: new WebAssemblyRenderMode());
        var host = new SectionHost { SectionName = SectionName };

        // Act
        renderer.AssignRootComponentId(host);
        host.TriggerRender();

        // Assert
        var exception = Assert.IsType<InvalidOperationException>(Assert.Single(renderer.HandledExceptions));
        Assert.Contains(SectionName, exception.Message);
        Assert.Contains(nameof(ServerRenderMode), exception.Message);
        Assert.Contains(nameof(WebAssemblyRenderMode), exception.Message);
    }

    private sealed class SectionRenderModeRenderer : TestRenderer
    {
        private readonly IComponentRenderMode _outletRenderMode;
        private readonly IComponentRenderMode _contentRenderMode;

        public SectionRenderModeRenderer(IComponentRenderMode outletRenderMode, IComponentRenderMode contentRenderMode)
        {
            _outletRenderMode = outletRenderMode;
            _contentRenderMode = contentRenderMode;
            ShouldHandleExceptions = true;
        }

        protected internal override IComponentRenderMode GetComponentRenderMode(IComponent component)
            => component switch
            {
                SectionOutlet => _outletRenderMode,
                SectionContent => _contentRenderMode,
                _ => null,
            };
    }

    private sealed class SectionHost : AutoRenderComponent
    {
        public string SectionName { get; set; }

        public object SectionId { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SectionOutlet>(0);
            AddSectionIdentifier<SectionOutlet>(builder, 1);
            builder.CloseComponent();

            builder.OpenComponent<SectionContent>(2);
            AddSectionIdentifier<SectionContent>(builder, 3);
            builder.AddComponentParameter(4, nameof(SectionContent.ChildContent), (RenderFragment)(b => b.AddContent(0, "Section content")));
            builder.CloseComponent();
        }

        private void AddSectionIdentifier<TComponent>(RenderTreeBuilder builder, int sequence)
        {
            if (SectionName is not null)
            {
                builder.AddComponentParameter(sequence, nameof(SectionOutlet.SectionName), SectionName);
            }
            else
            {
                builder.AddComponentParameter(sequence, nameof(SectionOutlet.SectionId), SectionId);
            }
        }
    }

    private sealed class ServerRenderMode : IComponentRenderMode
    {
    }

    private sealed class WebAssemblyRenderMode : IComponentRenderMode
    {
    }
}
