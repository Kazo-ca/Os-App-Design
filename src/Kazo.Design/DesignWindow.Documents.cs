using Kazo.Design.Documents;
using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.IO;
using Microsoft.JSInterop;

namespace Kazo.Design;

/// <summary>
/// Opening, saving, the exports and closing (doc/en/plan.md, work item 2): the window reads and
/// writes the files with <see cref="KazoFile"/>, asks the OS dialogs, keeps the title and guards the
/// close. <see cref="DesignDocuments"/> binds each DesignCraft tab to its file by its <c>uid</c> and
/// decides each save.
/// </summary>
/// <remarks>
/// <para>
/// DesignCraft in host mode (fork F2) hands over only the saves of a document
/// (<c>designcraft:save</c>), always a <c>.designcraft</c>, with <c>saveAs</c> true for Save As, a new
/// document and a converted one; it marks the document saved only when the window answers that the
/// file was written (H4). Exports (PDF, IDML, EPUB, images…) and Save a Copy are downloads, which the
/// module hands to the window as copies (<see cref="CraftFrame.Exporting"/>, ADR 0264 of Kazo OS).
/// </para>
/// <para>
/// A file opens in a new tab of this window, or brings back its tab when it is already open here
/// (decision 6). The File Explorer starts a new Kazo Design window for each file it opens.
/// </para>
/// </remarks>
public sealed partial class DesignWindow : IWindowCloseGuard
{
    /// <summary>
    /// How long the close guard waits for DesignCraft's save of one document: the Save dialog of a new
    /// document waits for the user, and a save that never comes must not hold the window open for good.
    /// </summary>
    private static readonly TimeSpan CloseSaveTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long the window waits for DesignCraft to show a file it sent (a large document takes a while).</summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(90);

    /// <summary>How often the window looks for the tab of a file it sent.</summary>
    private static readonly TimeSpan OpenPollInterval = TimeSpan.FromMilliseconds(150);

    private readonly DesignDocuments _documents = new();

    // Saves run one at a time: each may show a dialog, and the order is DesignCraft's.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    // Completed with the outcome of the next save (true = written), for the close guard.
    private TaskCompletionSource<bool>? _nextSave;

    // A message over DesignCraft: a failed write, a cancelled save, a copy written.
    private string? _notice;
    private bool _noticeIsError;

    /// <summary>Gets the title the OS shows for this window: <c>*name — Kazo Design</c>.</summary>
    internal string DocumentTitle => _documents.Title(L["Untitled"], _startup.State == FrameState.Running);

    /// <summary>
    /// Asks before closing a window with work that is not in a file: DesignCraft lists its tabs
    /// (<c>document.list</c>); "Yes" brings each modified one to the front and saves it, and the window
    /// closes only once every write succeeded (decision 9).
    /// </summary>
    /// <param name="cancellationToken">Cancels the close request.</param>
    /// <returns><see langword="true"/> to close.</returns>
    public async ValueTask<bool> ConfirmCloseAsync(CancellationToken cancellationToken = default)
    {
        if (_frame is not { } frame || _startup.State != FrameState.Running)
        {
            return true;
        }

        // A save still being written must end first: closing now would lose the write.
        await _saveGate.WaitAsync(cancellationToken);
        _saveGate.Release();

        if (!await RefreshTabsAsync(frame, cancellationToken))
        {
            // DesignCraft does not answer, so it cannot save either: closing without saving is the
            // only choice left, and it is the user's.
            if (!_documents.IsDirty)
            {
                return true;
            }

            string name = _documents.Active is { } active ? _documents.NameOf(active, L["Untitled"]) : L["Untitled"];
            MessageBoxDialogResult discard = await MessageBox(
                L["CloseTitle"], L["CloseUnanswered", name], MessageBoxType.YesNo, cancellationToken);
            return discard.Result == MessageBoxResult.Yes;
        }

        IReadOnlyList<DesignTab> unsaved = _documents.WithUnsavedWork();
        if (unsaved.Count == 0)
        {
            return true;
        }

        string names = string.Join(", ", unsaved.Select(tab => _documents.NameOf(tab, L["Untitled"])));
        MessageBoxDialogResult answer = await MessageBox(
            L["CloseTitle"], L["CloseUnsaved", names], MessageBoxType.YesNoCancel, cancellationToken);
        return answer.Result switch
        {
            MessageBoxResult.Yes => await SaveAllAsync(frame, unsaved, cancellationToken),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    // After `ready`: the file the window was started for. DesignCraft starts on its start screen
    // (New, Open, recent files), as on the desktop: a window with no file stays there.
    private async Task PrepareDocumentAsync()
    {
        // Runs after `ready`, a [JSInvokable] callback: the app's scope must be entered for KazoFile.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        if (_request.Path is { } path)
        {
            await OpenPathAsync(path);
        }

        await UpdateTitleAsync();
    }

    /// <summary>Opens a file in a new tab, or brings back the tab that already has it (decision 6).</summary>
    /// <param name="path">The file's absolute path.</param>
    /// <returns>A task that completes once the file is shown, or a message says why not.</returns>
    private async Task OpenPathAsync(string path)
    {
        if (_frame is not { } frame)
        {
            return;
        }

        await RefreshTabsAsync(frame);
        if (_documents.TabOf(path) is { } tab)
        {
            await ExecuteAsync(frame, "file.activate", new { index = tab.Index });
            await RefreshTabsAsync(frame);
            await UpdateTitleAsync();
            return;
        }

        await SendFileAsync(frame, path);
    }

    // Reads a file and sends it to DesignCraft (H2), which opens it in a new tab: a .designcraft under
    // its name, an IDML or InDesign file converted. Its tab is then bound to the file. A file that
    // does not read, or that DesignCraft does not show, leaves a message.
    private async Task SendFileAsync(CraftFrame frame, string path)
    {
        string name = CraftDocumentFormats.FileNameOf(path);
        byte[] bytes;
        try
        {
            bytes = await KazoFile.ReadAllBytesAsync(path, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await ShowNoticeAsync(L["OpenFailed", name, exception.Message], error: true);
            return;
        }

        PendingOpen pending = _documents.Opening(path);
        if (!await frame.OpenAsync(name, bytes))
        {
            _documents.Forget(pending);
            await ShowNoticeAsync(L["OpenFailed", name, L["NotConnected"]], error: true);
            return;
        }

        await ClearNoticeAsync();
        if (!await WaitForTabAsync(frame, pending))
        {
            // DesignCraft could not read it: it says so itself (its alert, and `failed`).
            _documents.Forget(pending);
        }

        await UpdateTitleAsync();
    }

    // DesignCraft opens the file on its next frames, or later for a large one: wait for its tab, so
    // the title names it and the tab is bound before its first save.
    private async Task<bool> WaitForTabAsync(CraftFrame frame, PendingOpen pending)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + OpenTimeout;
        while (DateTimeOffset.UtcNow < deadline && !AppContext.CancellationToken.IsCancellationRequested)
        {
            if (await RefreshTabsAsync(frame) && !_documents.Pending.Contains(pending))
            {
                return true;
            }

            try
            {
                await Task.Delay(OpenPollInterval, AppContext.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    // Asks DesignCraft for its tabs (document.list); false when it does not answer.
    private async Task<bool> RefreshTabsAsync(CraftFrame frame, CancellationToken cancellationToken = default)
    {
        FrameCommandResult list = await ExecuteAsync(frame, "document.list", new { }, cancellationToken);
        if (!list.Ok)
        {
            return false;
        }

        _documents.Update(DesignTab.ListFrom(list.Value));
        return true;
    }

    private async Task OnDirtyChangedAsync(bool dirty)
    {
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        _documents.SetFrameDirty(dirty);
        if (_frame is { } frame)
        {
            // The tab in front may have changed too: the title names it.
            await RefreshTabsAsync(frame);
        }

        await UpdateTitleAsync();
    }

    // One save of a document DesignCraft hands over: decided by DesignDocuments, written here. The
    // answer is what DesignCraft hears (`saved` only when true, H4).
    private async Task<bool> OnSavingAsync(FrameSave save)
    {
        // Called from JavaScript ([JSInvokable]): the app's scope must be entered for KazoFile.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        await _saveGate.WaitAsync(AppContext.CancellationToken);
        bool written = false;
        try
        {
            written = await SaveAsync(save);
        }
        finally
        {
            _saveGate.Release();
            Interlocked.Exchange(ref _nextSave, null)?.TrySetResult(written);
            await UpdateTitleAsync();
        }

        return written;
    }

    // One file DesignCraft exports (PDF, IDML, EPUB, HTML, images, Save a Copy, packages…; ADR 0264 of
    // Kazo OS): always a copy, in the folder of the document in front. It does not wait for the save
    // gate: it never touches the document.
    private async Task<bool> OnExportingAsync(FrameSave export)
    {
        // Called from JavaScript ([JSInvokable]): the app's scope must be entered for KazoFile.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        if (string.IsNullOrEmpty(export.Name))
        {
            return false;
        }

        if (_frame is { } frame)
        {
            await RefreshTabsAsync(frame);
        }

        DesignSave copy = new(_documents.DecideCopy(export.Name), null);
        try
        {
            return await CarryOutAsync(copy, export.Bytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowNoticeAsync(L["SaveFailed", export.Name, exception.Message], error: true);
            return false;
        }
    }

    private async Task<bool> SaveAsync(FrameSave save)
    {
        if (string.IsNullOrEmpty(save.Name))
        {
            return false;
        }

        if (_frame is { } frame)
        {
            // The save is the tab's in front: its uid gives its file, or the dialog's folder.
            await RefreshTabsAsync(frame);
        }

        DesignSave decision = _documents.Decide(save.Name, save.SaveAs);
        try
        {
            return await CarryOutAsync(decision, save.Bytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever went wrong (a dialog, the file system), the document must not pass for saved.
            await ShowNoticeAsync(L["SaveFailed", save.Name, exception.Message], error: true);
            return false;
        }
    }

    // Asks for the path when the decision says so, writes the bytes, and records the outcome.
    private async Task<bool> CarryOutAsync(DesignSave save, byte[] bytes)
    {
        SaveDecision decision = save.Decision;
        string? path = decision.AsksForPath ? await AskSavePathAsync(decision) : decision.Path;
        if (path is null)
        {
            if (decision.SavesTheDocument)
            {
                await ShowNoticeAsync(L["SaveCancelled", decision.Name], error: false);
            }

            return false;
        }

        try
        {
            await KazoFile.WriteAllBytesAsync(path, bytes, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await ShowNoticeAsync(L["SaveFailed", CraftDocumentFormats.FileNameOf(path), exception.Message], error: true);
            return false;
        }

        _documents.Saved(save, path);
        if (decision.Action == SaveAction.AskForCopy)
        {
            await ShowNoticeAsync(L["CopySaved", CraftDocumentFormats.FileNameOf(path)], error: false);
            return true;
        }

        await ClearNoticeAsync();
        return true;
    }

    // The OS save dialog, in the document's folder, for the format DesignCraft chose.
    private async Task<string?> AskSavePathAsync(SaveDecision decision)
    {
        string extension = CraftDocumentFormats.ExtensionOf(decision.Name);
        CraftDocumentFormat? format = DesignFormats.Formats.Find(decision.Name);
        SaveFileDialogResult result = await SaveFileAsync(new SaveFileDialogRequest
        {
            Title = decision.Action == SaveAction.AskForCopy ? L["SaveCopyTitle"] : L["SaveTitle"],
            InitialDirectory = VirtualPath.Parse(decision.Folder ?? AppContext.HomeDirectory),
            SuggestedFileName = decision.Name,
            DefaultExtension = extension.Length > 0 ? extension : null,
            Filters = extension.Length > 0
                ? [new FileDialogFilter(L["FilterFormat", extension], [extension], format is null ? [] : [format.MediaType])]
                : [],
            ConfirmOverwrite = true,
        });
        return result.Status == FileDialogStatus.Selected && result.Resource is { } chosen
            ? CraftDocumentFormats.WithExtensionOf(chosen.Path.Value, decision.Name)
            : null;
    }

    // "Yes" in the close dialog: each document is brought to the front (file.activate) and saved by
    // DesignCraft's own File › Save (app.save); the window closes only when all of them reached a file.
    private async Task<bool> SaveAllAsync(CraftFrame frame, IReadOnlyList<DesignTab> documents, CancellationToken cancellationToken)
    {
        foreach (DesignTab document in documents)
        {
            // An earlier save may have moved the tabs: find this document's place again by its uid.
            if (!await RefreshTabsAsync(frame, cancellationToken))
            {
                return false;
            }

            if (_documents.Tabs.FirstOrDefault(tab => tab.Uid == document.Uid) is not { } tab)
            {
                // Closed meanwhile: nothing left to save.
                continue;
            }

            TaskCompletionSource<bool> outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _nextSave = outcome;
            FrameCommandResult result = await ExecuteAsync(frame, "file.activate", new { index = tab.Index }, cancellationToken);
            if (result.Ok)
            {
                result = await frame.RequestAsync("ui.menu.invoke", new { id = "app.save" }, cancellationToken: cancellationToken);
            }

            if (!result.Ok)
            {
                _nextSave = null;
                await ShowNoticeAsync(L["SaveFailed", _documents.NameOf(tab, L["Untitled"]), result.Error ?? string.Empty], error: true);
                return false;
            }

            bool written;
            try
            {
                written = await outcome.Task.WaitAsync(CloseSaveTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                _nextSave = null;
                written = false;
            }

            if (!written)
            {
                return false;
            }
        }

        return true;
    }

    private async Task UpdateTitleAsync()
    {
        try
        {
            await SetWindowTitleAsync(DocumentTitle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JSDisconnectedException)
        {
            // The window is closing.
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task ShowNoticeAsync(string text, bool error)
    {
        _notice = text;
        _noticeIsError = error;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ClearNoticeAsync()
    {
        if (_notice is not null)
        {
            _notice = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void DismissNotice() => _notice = null;
}
