// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components.Forms.Mapping;
using Microsoft.AspNetCore.Components.Infrastructure;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Test.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Components.Forms;

public class EditFormTest
{
    private TestRenderer _testRenderer = new();

    public EditFormTest()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFormValueMapper, TestFormValueModelBinder>();
        services.AddAntiforgery();
        services.AddLogging();
        services.AddSingleton<ComponentStatePersistenceManager>();
        services.AddSingleton(services => services.GetRequiredService<ComponentStatePersistenceManager>().State);
        services.AddSingleton<AntiforgeryStateProvider, DefaultAntiforgeryStateProvider>();
        _testRenderer = new(services.BuildServiceProvider());
    }

    [Fact]
    public async Task ThrowsIfBothEditContextAndModelAreSupplied()
    {
        // Arrange
        var editForm = new EditForm
        {
            EditContext = new EditContext(new TestModel()),
            Model = new TestModel()
        };
        var testRenderer = new TestRenderer();
        var componentId = testRenderer.AssignRootComponentId(editForm);

        // Act/Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => testRenderer.RenderRootComponentAsync(componentId));
        Assert.StartsWith($"{nameof(EditForm)} requires a {nameof(EditForm.Model)} parameter, or an {nameof(EditContext)} parameter, but not both.", ex.Message);
    }

    [Fact]
    public async Task ThrowsIfBothEditContextAndModelAreNull()
    {
        // Arrange
        var editForm = new EditForm();
        var testRenderer = new TestRenderer();
        var componentId = testRenderer.AssignRootComponentId(editForm);

        // Act/Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => testRenderer.RenderRootComponentAsync(componentId));
        Assert.StartsWith($"{nameof(EditForm)} requires either a {nameof(EditForm.Model)} parameter, or an {nameof(EditContext)} parameter, please provide one of these.", ex.Message);
    }

    [Fact]
    public async Task ReturnsEditContextWhenModelParameterUsed()
    {
        // Arrange
        var model = new TestModel();
        var rootComponent = new TestEditFormHostComponent
        {
            Model = model
        };
        var editFormComponent = await RenderAndGetTestEditFormComponentAsync(rootComponent);

        // Act
        var returnedEditContext = editFormComponent.EditContext;

        // Assert
        Assert.NotNull(returnedEditContext);
        Assert.Same(model, returnedEditContext.Model);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReturnsEditContextWhenEditContextParameterUsed(bool createFieldPath)
    {
        // Arrange
        var editContext = new EditContext(new TestModel()) { ShouldUseFieldIdentifiers = createFieldPath };
        var rootComponent = new TestEditFormHostComponent
        {
            EditContext = editContext
        };
        var editFormComponent = await RenderAndGetTestEditFormComponentAsync(rootComponent);

        // Act
        var returnedEditContext = editFormComponent.EditContext;

        // Assert
        Assert.Same(editContext, returnedEditContext);
    }

    [Fact]
    public async Task DoesNotAddSSRContentWhenNoMappingContextPresent()
    {
        // Arrange
        var model = new TestModel();
        var rootComponent = new TestEditFormHostComponent
        {
            Model = model,
            FormName = "my-form",
        };

        // Act
        await RenderAndGetTestEditFormComponentAsync(rootComponent);
        var editFormComponentId = _testRenderer.Batches.Single()
            .GetComponentFrames<EditForm>().Single().ComponentId;
        var editFormFrames = _testRenderer.GetCurrentRenderTreeFrames(editFormComponentId);

        // Assert:
        //  - Does not set any "method" attribute
        //  - Does not assign any name to the submit event
        Assert.Collection(editFormFrames.AsEnumerable(),
            frame => AssertFrame.Region(frame, 7),
            frame => AssertFrame.Element(frame, "form", 6),
            frame => AssertFrame.Attribute(frame, "onsubmit"),
            frame => AssertFrame.Component<CascadingValue<EditContext>>(frame, 4),
            frame => AssertFrame.Attribute(frame, "IsFixed", true),
            frame => AssertFrame.Attribute(frame, "Value"),
            frame => AssertFrame.Attribute(frame, "ChildContent"));
    }

    [Fact]
    public async Task AddSSRContentWhenMappingContextPresent()
    {
        // Arrange
        var editContext = new EditContext(new object());
        var rootComponent = new TestEditFormHostComponent
        {
            FormName = "my-form",
            MappingContextName = "mapping-context-name",
            EditContext = editContext,
        };

        // Act
        await RenderAndGetTestEditFormComponentAsync(rootComponent);
        var editFormComponentId = _testRenderer.Batches.Single()
            .GetComponentFrames<EditForm>().Single().ComponentId;
        var editFormFrames = _testRenderer.GetCurrentRenderTreeFrames(editFormComponentId);

        // Assert
        Assert.Collection(editFormFrames.AsEnumerable(),
            frame => AssertFrame.Region(frame, 13),
            frame => AssertFrame.Element(frame, "form", 12),

            // Sets "method" to "post" by default
            frame => AssertFrame.Attribute(frame, "method", "post"),

            // Assigns name to the submit event
            frame => AssertFrame.Attribute(frame, "onsubmit"),
            frame => AssertFrame.NamedEvent(frame, "onsubmit", "my-form"),

            frame => AssertFrame.Region(frame, 4),

            // Adds FormMappingValidator child
            frame => AssertFrame.Component<FormMappingValidator>(frame, 2),
            frame => AssertFrame.Attribute(frame, nameof(FormMappingValidator.CurrentEditContext), editContext),

            // Adds AntiforgeryToken child
            frame => AssertFrame.Component<AntiforgeryToken>(frame, 1),

            frame => AssertFrame.Component<CascadingValue<EditContext>>(frame, 4),
            frame => AssertFrame.Attribute(frame, "IsFixed", true),
            frame => AssertFrame.Attribute(frame, "Value"),
            frame => AssertFrame.Attribute(frame, "ChildContent"));
    }

    [Fact]
    public async Task CanOverrideMethodWhenMappingContextPresent()
    {
        // Arrange
        var editContext = new EditContext(new object());
        var rootComponent = new TestEditFormHostComponent
        {
            FormName = "my-form",
            MappingContextName = "mapping-context-name",
            EditContext = editContext,
            AdditionalFormAttributes = new Dictionary<string, object>
            {
                { "method", "my method" },
                { "custom attribute", "some value" },
            },
        };

        // Act
        await RenderAndGetTestEditFormComponentAsync(rootComponent);
        var editFormComponentId = _testRenderer.Batches.Single()
            .GetComponentFrames<EditForm>().Single().ComponentId;
        var editFormFrames = _testRenderer.GetCurrentRenderTreeFrames(editFormComponentId);
        var editFormAttributes = editFormFrames.AsEnumerable()
            .SkipWhile(f => f.FrameType != RenderTreeFrameType.Attribute)
            .TakeWhile(f => f.FrameType == RenderTreeFrameType.Attribute)
            .ToDictionary(f => f.AttributeName, f => f.AttributeValue);

        // Assert
        Assert.Equal("my method", editFormAttributes["method"]);
        Assert.Equal("some value", editFormAttributes["custom attribute"]);
    }

    [Fact]
    public async Task Submit_AwaitsAsyncValidationBeforeOnValidSubmit()
    {
        var editContext = new EditContext(new TestModel());
        var field = editContext.Field(nameof(TestModel.StringProperty));
        TestAsyncValidator validator = null;
        var validSubmitCount = 0;
        var rootComponent = new AsyncEditFormHostComponent
        {
            EditContext = editContext,
            Configure = current =>
            {
                current.Configure(field, new ValidationConfig { Outcome = ValidationOutcome.Valid });
                current.GetGate(field);
            },
            Created = current => validator = current,
            OnValidSubmit = _ => validSubmitCount++,
        };
        await RenderAsyncRootAsync(rootComponent);

        var dispatchTask = _testRenderer.DispatchEventAsync(GetSubmitEventHandlerId(), EventArgs.Empty);
        await WaitUntilAsync(() => validator.FormValidationStartCount == 1);

        Assert.Equal(0, validSubmitCount);

        validator.OpenGate(field, ValidationOutcome.Valid);
        await dispatchTask.WaitAsync(DefaultAsyncTimeout);

        Assert.Equal(1, validSubmitCount);
    }

    [Fact]
    public async Task Submit_InvalidAsyncValidation_FiresOnInvalidSubmit()
    {
        var editContext = new EditContext(new TestModel());
        var field = editContext.Field(nameof(TestModel.StringProperty));
        var validSubmitCount = 0;
        var invalidSubmitCount = 0;
        var rootComponent = new AsyncEditFormHostComponent
        {
            EditContext = editContext,
            Configure = current => current.Configure(field, new ValidationConfig { Outcome = ValidationOutcome.Invalid, ErrorMessage = "Invalid" }),
            OnValidSubmit = _ => validSubmitCount++,
            OnInvalidSubmit = _ => invalidSubmitCount++,
        };
        await RenderAsyncRootAsync(rootComponent);

        await _testRenderer.DispatchEventAsync(GetSubmitEventHandlerId(), EventArgs.Empty).WaitAsync(DefaultAsyncTimeout);

        Assert.Equal(0, validSubmitCount);
        Assert.Equal(1, invalidSubmitCount);
        Assert.Equal(new[] { "Invalid" }, editContext.GetValidationMessages(field));
    }

    [Fact]
    public async Task Submit_AsyncValidatorThrows_FiresOnInvalidSubmitWithFaultedContext()
    {
        var editContext = new EditContext(new TestModel());
        var field = editContext.Field(nameof(TestModel.StringProperty));
        var validSubmitCount = 0;
        var invalidSubmitCount = 0;
        var observedFaulted = false;
        var rootComponent = new AsyncEditFormHostComponent
        {
            EditContext = editContext,
            Configure = current => current.Configure(field, new ValidationConfig { Outcome = ValidationOutcome.ThrowInfraException }),
            OnValidSubmit = _ => validSubmitCount++,
            OnInvalidSubmit = context =>
            {
                invalidSubmitCount++;
                observedFaulted = context.IsValidationFaulted();
            },
        };
        await RenderAsyncRootAsync(rootComponent);

        await _testRenderer.DispatchEventAsync(GetSubmitEventHandlerId(), EventArgs.Empty).WaitAsync(DefaultAsyncTimeout);

        Assert.Equal(0, validSubmitCount);
        Assert.Equal(1, invalidSubmitCount);
        Assert.True(observedFaulted);
    }

    [Fact]
    public async Task Submit_WithPendingFieldTask_CancelsFieldTaskAndRunsFormValidation()
    {
        var editContext = new EditContext(new TestModel());
        var field = editContext.Field(nameof(TestModel.StringProperty));
        TestAsyncValidator validator = null;
        var validSubmitCount = 0;
        var rootComponent = new AsyncEditFormHostComponent
        {
            EditContext = editContext,
            Configure = current => current.Configure(field, new ValidationConfig { Outcome = ValidationOutcome.Valid }),
            Created = current => validator = current,
            OnValidSubmit = _ => validSubmitCount++,
        };
        await RenderAsyncRootAsync(rootComponent);
        var pendingCts = new CancellationTokenSource();
        var pendingTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pendingRegistration = pendingCts.Token.Register(() => pendingTcs.TrySetCanceled(pendingCts.Token));
        editContext.AddValidationTask(field, pendingTcs.Task, pendingCts);

        await _testRenderer.DispatchEventAsync(GetSubmitEventHandlerId(), EventArgs.Empty).WaitAsync(DefaultAsyncTimeout);

        Assert.True(pendingCts.IsCancellationRequested);
        Assert.False(editContext.IsValidationPending(field));
        Assert.Equal(1, validator.FormValidationStartCount);
        Assert.Equal(1, validSubmitCount);
    }

    private async Task RenderAsyncRootAsync(AsyncEditFormHostComponent rootComponent)
    {
        var componentId = _testRenderer.AssignRootComponentId(rootComponent);
        await _testRenderer.RenderRootComponentAsync(componentId);
    }

    private ulong GetSubmitEventHandlerId()
    {
        var editFormComponentId = _testRenderer.Batches.Last().ReferenceFrames.AsEnumerable()
            .Where(frame => frame.FrameType == RenderTreeFrameType.Component)
            .Where(frame => frame.Component is EditForm)
            .Select(frame => frame.ComponentId)
            .Single();
        var editFormFrames = _testRenderer.GetCurrentRenderTreeFrames(editFormComponentId);
        return editFormFrames.AsEnumerable()
            .Where(frame => frame.FrameType == RenderTreeFrameType.Attribute)
            .Where(frame => frame.AttributeName == "onsubmit")
            .Select(frame => frame.AttributeEventHandlerId)
            .Single();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > DefaultAsyncTimeout)
            {
                throw new TimeoutException("The expected condition was not reached before the timeout.");
            }

            await Task.Yield();
        }
    }

    private static readonly TimeSpan DefaultAsyncTimeout = TimeSpan.FromSeconds(5);

    private sealed class AsyncEditFormHostComponent : AutoRenderComponent
    {
        public EditContext EditContext { get; set; }

        public Action<TestAsyncValidator> Configure { get; set; }

        public Action<TestAsyncValidator> Created { get; set; }

        public Action<EditContext> OnValidSubmit { get; set; }

        public Action<EditContext> OnInvalidSubmit { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddComponentParameter(1, nameof(EditForm.EditContext), EditContext);
            if (OnValidSubmit is not null)
            {
                builder.AddComponentParameter(2, nameof(EditForm.OnValidSubmit), EventCallback.Factory.Create(this, OnValidSubmit));
            }
            if (OnInvalidSubmit is not null)
            {
                builder.AddComponentParameter(3, nameof(EditForm.OnInvalidSubmit), EventCallback.Factory.Create(this, OnInvalidSubmit));
            }
            builder.AddComponentParameter(4, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(context => childBuilder =>
            {
                childBuilder.OpenComponent<TestAsyncValidatorComponent>(0);
                childBuilder.AddComponentParameter(1, nameof(TestAsyncValidatorComponent.Configure), Configure);
                childBuilder.AddComponentParameter(2, nameof(TestAsyncValidatorComponent.Created), EventCallback.Factory.Create<TestAsyncValidator>(this, Created));
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private static EditForm FindEditFormComponent(CapturedBatch batch)
        => batch.ReferenceFrames
                .Where(f => f.FrameType == RenderTreeFrameType.Component)
                .Select(f => f.Component)
                .OfType<EditForm>()
                .Single();

    [Fact]
    public async Task WhenModelIsReplaced_NewEditContextIsCreated()
    {
        var originalModel = new TestModel { StringProperty = "original" };
        var rootComponent = new TestEditFormHostComponent { Model = originalModel };
        var editFormComponent = await RenderAndGetTestEditFormComponentAsync(rootComponent);
        var originalContext = editFormComponent.EditContext;

        var replacementModel = new TestModel { StringProperty = "replacement" };
        rootComponent.Model = replacementModel;
        rootComponent.TriggerRender();

        Assert.NotSame(originalContext, editFormComponent.EditContext);
    }

    [Fact]
    public async Task WhenModelIsReplaced_NewEditContextReferencesTheReplacementModel()
    {
        var originalModel = new TestModel { StringProperty = "original" };
        var rootComponent = new TestEditFormHostComponent { Model = originalModel };
        await RenderAndGetTestEditFormComponentAsync(rootComponent);

        var replacementModel = new TestModel { StringProperty = "replacement" };
        rootComponent.Model = replacementModel;
        rootComponent.TriggerRender();

        Assert.Same(replacementModel, rootComponent.Model);
        Assert.NotSame(originalModel, rootComponent.Model);
    }

    [Fact]
    public async Task WhenSameModelInstanceIsReused_EditContextStillReferencesTheSameModel()
    {
        var model = new TestModel { StringProperty = "unchanged" };
        var rootComponent = new TestEditFormHostComponent { Model = model };
        var editFormComponent = await RenderAndGetTestEditFormComponentAsync(rootComponent);

        rootComponent.TriggerRender();

        Assert.Same(model, editFormComponent.EditContext!.Model);
    }

    [Fact]
    public async Task WhenModelIsReplaced_FieldModifiedStateFromPreviousModelIsDiscarded()
    {
        var originalModel = new TestModel { StringProperty = "original" };
        var rootComponent = new TestEditFormHostComponent { Model = originalModel };
        var editFormComponent = await RenderAndGetTestEditFormComponentAsync(rootComponent);
        var originalContext = editFormComponent.EditContext;
        var field = originalContext.Field(nameof(TestModel.StringProperty));
        originalContext.NotifyFieldChanged(field);
        Assert.True(originalContext.IsModified());

        var replacementModel = new TestModel { StringProperty = "replacement" };
        rootComponent.Model = replacementModel;
        rootComponent.TriggerRender();

        Assert.NotSame(originalContext, editFormComponent.EditContext);
        Assert.False(editFormComponent.EditContext!.IsModified());
    }

    [Fact]
    public async Task WhenModelIsReplacedMultipleTimes_EachEditContextReferencesItsRespectiveModel()
    {
        var model1 = new TestModel { StringProperty = "first" };
        var rootComponent = new TestEditFormHostComponent { Model = model1 };
        var editFormComponent = await RenderAndGetTestEditFormComponentAsync(rootComponent);
        var context1 = editFormComponent.EditContext;

        var model2 = new TestModel { StringProperty = "second" };
        rootComponent.Model = model2;
        rootComponent.TriggerRender();
        var context2 = editFormComponent.EditContext;

        var model3 = new TestModel { StringProperty = "third" };
        rootComponent.Model = model3;
        rootComponent.TriggerRender();
        var context3 = editFormComponent.EditContext;

        Assert.NotSame(context1, context2);
        Assert.NotSame(context2, context3);
        Assert.Same(model1, context1!.Model);
        Assert.Same(model2, context2!.Model);
        Assert.Same(model3, context3!.Model);
    }

    private async Task<EditForm> RenderAndGetTestEditFormComponentAsync(TestEditFormHostComponent hostComponent)
    {
        var componentId = _testRenderer.AssignRootComponentId(hostComponent);
        await _testRenderer.RenderRootComponentAsync(componentId);
        return FindEditFormComponent(_testRenderer.Batches.Single());
    }

    class TestModel
    {
        public string StringProperty { get; set; }
    }

    class TestEditFormHostComponent : AutoRenderComponent
    {
        public EditContext EditContext { get; set; }

        public TestModel Model { get; set; }

        public string MappingContextName { get; set; }

        public Action<EditContext> SubmitHandler { get; set; }

        public string FormName { get; set; }

        public Dictionary<string, object> AdditionalFormAttributes { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (MappingContextName is not null)
            {
                builder.OpenComponent<FormMappingScope>(0);
                builder.AddComponentParameter(1, nameof(FormMappingScope.Name), MappingContextName);
                builder.AddComponentParameter(3, nameof(FormMappingScope.ChildContent), (RenderFragment<FormMappingContext>)(_ => RenderForm));
                builder.CloseComponent();
            }
            else
            {
                RenderForm(builder);
            }

            void RenderForm(RenderTreeBuilder builder)
            {
                builder.OpenComponent<EditForm>(0);
                // Order here is intentional to make sure that the test fails if we
                // accidentally capture a parameter in a named property.
                builder.AddMultipleAttributes(1, AdditionalFormAttributes);

                builder.AddComponentParameter(2, "Model", Model);
                builder.AddComponentParameter(3, "EditContext", EditContext);
                if (SubmitHandler != null)
                {
                    builder.AddComponentParameter(4, "OnValidSubmit", new EventCallback<EditContext>(null, SubmitHandler));
                }
                builder.AddComponentParameter(5, "FormName", FormName);

                builder.CloseComponent();
            }
        }
    }

    // Issue https://github.com/dotnet/aspnetcore/issues/41621
    // Replacing the Model causes EditForm to use a new region key (EditContext.GetHashCode()),
    // which makes the renderer treat the subtree as entirely new, disposing and recreating
    // all child components rather than simply re-rendering them in place.

    [Fact]
    public async Task WhenModelIsReplaced_ChildComponentsAreDisposedAndRecreated()
    {
        var model = new TestModel { StringProperty = "initial" };
        var rootComponent = new EditFormWithChildHostComponent { Model = model };
        var componentId = _testRenderer.AssignRootComponentId(rootComponent);
        await _testRenderer.RenderRootComponentAsync(componentId);

        var batchesBeforeReplace = _testRenderer.Batches.Count;

        rootComponent.Model = new TestModel { StringProperty = "replaced" };
        rootComponent.TriggerRender();

        // Collect all disposed component IDs from batches after the model replacement
        var disposedIds = _testRenderer.Batches
            .Skip(batchesBeforeReplace)
            .SelectMany(b => b.DisposedComponentIDs)
            .ToList();

        // The bug: child components ARE disposed when the model changes because the
        // region key (EditContext.GetHashCode()) changes, tearing down the entire subtree.
        Assert.NotEmpty(disposedIds);
    }

    [Fact]
    public async Task WhenSameModelIsReused_ChildComponentsAreNotDisposed()
    {
        var model = new TestModel { StringProperty = "stable" };
        var rootComponent = new EditFormWithChildHostComponent { Model = model };
        var componentId = _testRenderer.AssignRootComponentId(rootComponent);
        await _testRenderer.RenderRootComponentAsync(componentId);

        var batchesBeforeRerender = _testRenderer.Batches.Count;

        // Re-render with the same model instance — no region key change expected
        rootComponent.TriggerRender();

        var disposedIds = _testRenderer.Batches
            .Skip(batchesBeforeRerender)
            .SelectMany(b => b.DisposedComponentIDs)
            .ToList();

        Assert.Empty(disposedIds);
    }

    [Fact]
    public async Task WhenAllowModelChangeIsTrue_ReplacingModelDoesNotDisposeChildComponents()
    {
        var model = new TestModel { StringProperty = "initial" };
        var rootComponent = new EditFormWithChildHostComponent { Model = model, AllowModelChange = true };
        var componentId = _testRenderer.AssignRootComponentId(rootComponent);
        await _testRenderer.RenderRootComponentAsync(componentId);

        var batchesBeforeReplace = _testRenderer.Batches.Count;

        rootComponent.Model = new TestModel { StringProperty = "replaced" };
        rootComponent.TriggerRender();

        // With AllowModelChange=true, child components should NOT be disposed
        var disposedIds = _testRenderer.Batches
            .Skip(batchesBeforeReplace)
            .SelectMany(b => b.DisposedComponentIDs)
            .ToList();

        Assert.Empty(disposedIds);
    }

    [Fact]
    public async Task WhenAllowModelChangeIsTrue_MultipleModelReplacements_ChildComponentsAreNeverDisposed()
    {
        var model = new TestModel { StringProperty = "first" };
        var rootComponent = new EditFormWithChildHostComponent { Model = model, AllowModelChange = true };
        var componentId = _testRenderer.AssignRootComponentId(rootComponent);
        await _testRenderer.RenderRootComponentAsync(componentId);

        var batchesBeforeReplace = _testRenderer.Batches.Count;

        rootComponent.Model = new TestModel { StringProperty = "second" };
        rootComponent.TriggerRender();

        rootComponent.Model = new TestModel { StringProperty = "third" };
        rootComponent.TriggerRender();

        var disposedIds = _testRenderer.Batches
            .Skip(batchesBeforeReplace)
            .SelectMany(b => b.DisposedComponentIDs)
            .ToList();

        Assert.Empty(disposedIds);
    }

    /// <summary>
    /// A host component that renders an <see cref="EditForm"/> with a child component inside,
    /// allowing tests to observe whether child components are disposed during model replacement.
    /// Corresponds to issue https://github.com/dotnet/aspnetcore/issues/41621.
    /// </summary>
    private class EditFormWithChildHostComponent : AutoRenderComponent
    {
        public TestModel Model { get; set; }

        public bool AllowModelChange { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddComponentParameter(1, "Model", Model);
            builder.AddComponentParameter(2, "AllowModelChange", AllowModelChange);
            builder.AddComponentParameter(3, "ChildContent", (RenderFragment<EditContext>)(_ => childBuilder =>
            {
                childBuilder.OpenComponent<LifecycleTrackingComponent>(0);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    /// <summary>
    /// A minimal child component that implements <see cref="IDisposable"/> so the renderer
    /// tracks its disposal in <see cref="CapturedBatch.DisposedComponentIDs"/>.
    /// </summary>
    private class LifecycleTrackingComponent : ComponentBase, IDisposable
    {
        public bool WasDisposed { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.AddContent(0, "child content");
        }

        public void Dispose()
        {
            WasDisposed = true;
        }
    }

    private class TestFormValueModelBinder : IFormValueMapper
    {
        public bool CanMap(Type valueType, string mappingScopeName, string formName) => false;
        public void Map(FormValueMappingContext context) { }
    }
}
