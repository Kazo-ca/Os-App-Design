using Kazo.Design.Hosting;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Kazo.Design;

/// <summary>
/// The Kazo Design window (doc/en/plan.md, work items 1 and 2): DesignCraft in an iframe started by the Kazo
/// OS module <c>Kazo.Os.AppSdk.Craft</c> (<see cref="CraftFrame"/>), with a loading screen until it is
/// ready and an error screen with "Retry" when it fails or does not answer within
/// <see cref="FrameStartup.Timeout"/>.
/// </summary>
/// <remarks>
/// Once DesignCraft is ready (<c>designcraft:ready</c>, fork H1) the window gives it the OS theme and
/// the user's language (decision 10), then opens the file it was started for. Opening, saving, the
/// exports, the tabs bound to their files and the close guard are in <c>DesignWindow.Documents.cs</c>;
/// the file pickers in <c>DesignWindow.Proxy.cs</c>. The launch argument <c>--webgl</c> forces
/// DesignCraft's WebGL 2 renderer, to try it without WebGPU.
/// </remarks>
public sealed partial class DesignWindow : IAsyncDisposable
{
    private readonly FrameStartup _startup = new();
    private ElementReference _frameHost;
    private CraftFrame? _frame;
    private CancellationTokenSource? _timeout;
    private IStringLocalizer<Strings>? _localizer;
    private OpenRequest _request = new(null, false);

    [Inject]
    private IJSRuntime JavaScript { get; set; } = default!;

    private IStringLocalizer<Strings> L => _localizer ??= AppContext.GetLocalizer<Strings>();

    /// <summary>The error screen's detail: DesignCraft's own error, or the timeout.</summary>
    private string FailureText => _startup.TimedOut
        ? L["TimedOut", (int)FrameStartup.Timeout.TotalSeconds]
        : _startup.Error ?? string.Empty;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        CancelTimeout();
        await StopFrameAsync();
        _nextSave?.TrySetResult(false);
        _saveGate.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>Reads what the window was started for, and names it before it is first shown.</remarks>
    protected override async Task OnAppInitializedAsync()
    {
        _request = OpenRequest.FromArguments(LaunchArguments);
        await SetWindowTitleAsync(DocumentTitle);
    }

    /// <inheritdoc />
    protected override async Task OnAppAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await StartFrameAsync();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The window came to the front: the keyboard goes to DesignCraft, and the title follows the tab
    /// in front, which DesignCraft may have changed without any change to save.
    /// </remarks>
    protected override async Task OnWindowActivatedAsync()
    {
        if (_frame is { } frame && _startup.State == FrameState.Running)
        {
            try
            {
                await frame.FocusAsync();
                await RefreshTabsAsync(frame);
                await UpdateTitleAsync();
            }
            catch (OperationCanceledException)
            {
                // The window is closing (activated on its way out, after the close guard saved).
            }
        }
    }

    private async Task StartFrameAsync()
    {
        CraftFrame frame = new(JavaScript);
        frame.Ready += OnReadyAsync;
        frame.Failed += OnFailedAsync;
        frame.SchemeChanged += OnSchemeChangedAsync;
        frame.DirtyChanged += OnDirtyChangedAsync;
        frame.Saving += OnSavingAsync;
        frame.Exporting += OnExportingAsync;
        frame.PickRequested += OnPickRequestedAsync;
        _frame = frame;
        ArmTimeout(_startup.Attempt);

        try
        {
            await frame.StartAsync(_frameHost, FrameOptions(), AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            await OnFailedAsync(exception.Message);
        }
    }

    // DesignCraft's page, its language, the WebGL 2 switch, the control channel the proxy drives
    // DesignCraft with, and the chords the OS keeps. Ctrl+N stays DesignCraft's "New document"
    // (decision 12): in a tabbed layout app a new document is a new tab, not a new window.
    private CraftFrameOptions FrameOptions() =>
        new(CraftFrameOptions.PagePathOf(typeof(DesignWindow).Assembly.GetName().Name!), L["EditorLabel"])
        {
            Language = DesignCraftLanguage.Of(AppContext.Culture),
            Parameters = DesignCraftParameters.For(_request.WebGl),
            ReservedKeys = ReservedKeys.Os,
        };

    private async Task StopFrameAsync()
    {
        if (_frame is null)
        {
            return;
        }

        _frame.Ready -= OnReadyAsync;
        _frame.Failed -= OnFailedAsync;
        _frame.SchemeChanged -= OnSchemeChangedAsync;
        _frame.DirtyChanged -= OnDirtyChangedAsync;
        _frame.Saving -= OnSavingAsync;
        _frame.Exporting -= OnExportingAsync;
        _frame.PickRequested -= OnPickRequestedAsync;
        await _frame.DisposeAsync();
        _frame = null;
    }

    private async Task RetryAsync()
    {
        CancelTimeout();
        await StopFrameAsync();
        _startup.Retry();
        // A new DesignCraft starts with no document: the old tabs and their uids are gone.
        _documents.Reset();
        await UpdateTitleAsync();
        await StartFrameAsync();
    }

    private async Task OnReadyAsync(string? appVersion)
    {
        if (!_startup.OnReady(appVersion))
        {
            return;
        }

        CancelTimeout();
        await InvokeAsync(StateHasChanged);
        if (_frame is not { } frame)
        {
            return;
        }

        // DesignCraft keeps nothing between two starts (decision 11): the window gives it the theme
        // and the language each time.
        await OnSchemeChangedAsync(await frame.GetColorSchemeAsync());
        await ExecuteAsync(frame, "app.language", new { lang = DesignCraftLanguage.CommandValue(AppContext.Culture) });

        await PrepareDocumentAsync();
        if (_frame is { } ready && IsWindowActive)
        {
            await ready.FocusAsync();
        }
    }

    private async Task OnFailedAsync(string? error)
    {
        if (_startup.State == FrameState.Running)
        {
            // Once running, DesignCraft also reports a file it could not open (H1, H2) as `failed`:
            // the app is still there, so that is a message, not the error screen. Lost graphics come
            // the same way, and DesignCraft's own page shows its recovery panel in the iframe.
            await ShowNoticeAsync(error ?? string.Empty, error: true);
            return;
        }

        if (_startup.OnFailed(error))
        {
            CancelTimeout();
            await InvokeAsync(StateHasChanged);
        }
    }

    // DesignCraft follows the OS theme (decision 10): its "dark" brightness for the dark scheme,
    // "light" for the light one.
    private async Task OnSchemeChangedAsync(string scheme)
    {
        if (_frame is { } frame)
        {
            await frame.RequestAsync("ui.set", new { brightness = DesignCraftTheme.Of(scheme) }, cancellationToken: AppContext.CancellationToken);
        }
    }

    private Task<FrameCommandResult> ExecuteAsync(CraftFrame frame, string command, object parameters, CancellationToken cancellationToken = default) =>
        frame.RequestAsync(
            "engine.execute",
            new { command, @params = parameters },
            cancellationToken: cancellationToken == default ? AppContext.CancellationToken : cancellationToken);

    // Shows the error screen if attempt `attempt` gets no answer in time.
    private void ArmTimeout(int attempt)
    {
        CancelTimeout();
        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(AppContext.CancellationToken);
        _timeout = timeout;
        _ = WaitForTimeoutAsync(attempt, timeout.Token);
    }

    private async Task WaitForTimeoutAsync(int attempt, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FrameStartup.Timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            if (_startup.OnTimeout(attempt))
            {
                StateHasChanged();
            }
        });
    }

    private void CancelTimeout()
    {
        _timeout?.Cancel();
        _timeout?.Dispose();
        _timeout = null;
    }
}
