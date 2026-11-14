using Kazo.Design.Hosting;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Kazo.Design;

/// <summary>
/// The Kazo Design window (doc/en/plan.md, work item 1): DesignCraft in an iframe started by the Kazo
/// OS module <c>Kazo.Os.AppSdk.Craft</c> (<see cref="CraftFrame"/>), with a loading screen until it is
/// ready and an error screen with "Retry" when it fails or does not answer within
/// <see cref="FrameStartup.Timeout"/>.
/// </summary>
/// <remarks>
/// Once DesignCraft is ready the window gives it the OS theme and the user's language (decision 10).
/// "Ready" is DesignCraft's <c>designcraft:ready</c> event, which only the host mode of our fork sends
/// (H1, fork work item F2): a build without it stays on the loading screen until the timeout.
/// Opening, saving, the tabs bound to their files, the close guard and the file picker come with
/// work item 2. The launch argument <c>--webgl</c> forces DesignCraft's WebGL 2 renderer, to try it
/// without WebGPU.
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
    }

    /// <inheritdoc />
    /// <remarks>Reads what the window was started for, and names it before it is first shown.</remarks>
    protected override async Task OnAppInitializedAsync()
    {
        _request = OpenRequest.FromArguments(LaunchArguments);
        await SetWindowTitleAsync($"{L["Untitled"]} — Kazo Design");
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
    /// <remarks>The window came to the front: the keyboard goes to DesignCraft.</remarks>
    protected override async Task OnWindowActivatedAsync()
    {
        if (_frame is not null && _startup.State == FrameState.Running)
        {
            await _frame.FocusAsync();
        }
    }

    private async Task StartFrameAsync()
    {
        CraftFrame frame = new(JavaScript);
        frame.Ready += OnReadyAsync;
        frame.Failed += OnFailedAsync;
        frame.SchemeChanged += OnSchemeChangedAsync;
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
        await _frame.DisposeAsync();
        _frame = null;
    }

    private async Task RetryAsync()
    {
        CancelTimeout();
        await StopFrameAsync();
        _startup.Retry();
        StateHasChanged();
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

        // DesignCraft starts on its home screen (New, Open, recent files), as on the desktop: the
        // window leaves it there. Work item 2 opens the requested file instead when there is one.
        if (IsWindowActive)
        {
            await frame.FocusAsync();
        }
    }

    private async Task OnFailedAsync(string? error)
    {
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

    private Task<FrameCommandResult> ExecuteAsync(CraftFrame frame, string command, object parameters) =>
        frame.RequestAsync("engine.execute", new { command, @params = parameters }, cancellationToken: AppContext.CancellationToken);

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
