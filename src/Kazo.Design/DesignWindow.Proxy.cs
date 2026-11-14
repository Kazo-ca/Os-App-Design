using Kazo.Design.Documents;
using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.IO;

namespace Kazo.Design;

/// <summary>
/// DesignCraft's file pickers (doc/en/plan.md, work item 2): the OS Open dialog instead of the
/// browser's.
/// </summary>
/// <remarks>
/// The proxy's messages arrive through a <c>[JSInvokable]</c> callback, outside the scope the OS opens
/// for app code: the handler enters the app's context itself before it reaches the SDK (files,
/// dialogs). DesignCraft keeps no preferences in the browser (decision 11), so the window has no
/// settings file to keep.
/// </remarks>
public sealed partial class DesignWindow
{
    // DesignCraft opened its file picker: the OS dialog instead. File › Open (and the start screen's
    // Open…) is not handed to DesignCraft: it hears "cancelled", and the window opens the chosen file
    // itself (OpenPathAsync), bound to its file (decision 6). Every other picker (Place, Load Styles,
    // the Swatches panel's .ase, a shortcut set) gets the file read and handed over: DesignCraft reads
    // its bytes itself.
    private Task OnPickRequestedAsync(PickRequest request) => InvokeAsync(async () =>
    {
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        CraftFrame? frame = _frame;
        if (frame is null)
        {
            return;
        }

        bool open = DesignFormats.IsOpenPicker(request);
        IReadOnlyList<PickedFile>? files = null;
        IReadOnlyList<string> toOpen = [];
        try
        {
            // The dialog opens in the folder of the document in front.
            await RefreshTabsAsync(frame);

            OpenFileDialogResult choice = await OpenFileAsync(
                new OpenFileDialogRequest
                {
                    Title = L[open ? "OpenTitle" : "PickTitle"],
                    InitialDirectory = VirtualPath.Parse(OpenFolder()),
                    AllowMultiple = open || request.Multiple,
                    Filters = FiltersOf(request, open),
                },
                AppContext.CancellationToken);
            if (choice.Status == FileDialogStatus.Selected && choice.Resources.Count > 0)
            {
                if (open)
                {
                    toOpen = [.. choice.Resources.Select(resource => resource.Path.Value)];
                }
                else
                {
                    files = await ReadPickedAsync(choice.Resources);
                }
            }
        }
        finally
        {
            // DesignCraft waits on its picker until it hears back: it always does, even on a failure.
            await frame.AnswerPickAsync(request.Id, files);
        }

        // After the answer: DesignCraft's picker is closed before the files open.
        foreach (string path in toOpen)
        {
            await OpenPathAsync(path);
        }
    });

    // The Open dialog starts in the folder of the document in front, else in the user's home.
    private string OpenFolder() =>
        _documents.Active is { } tab
        && _documents.FileOf(tab.Uid) is { } path
        && CraftDocumentFormats.FolderOf(path) is { } folder
            ? folder
            : AppContext.HomeDirectory;

    private async Task<IReadOnlyList<PickedFile>?> ReadPickedAsync(IReadOnlyList<SelectedFileResource> resources)
    {
        List<PickedFile> files = [];
        foreach (SelectedFileResource resource in resources)
        {
            string path = resource.Path.Value;
            string name = KazoPath.GetFileName(path) ?? path;
            try
            {
                byte[] bytes = await KazoFile.ReadAllBytesAsync(path, AppContext.CancellationToken);
                files.Add(new PickedFile(name, string.Empty, bytes));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                await ShowProblemAsync(L["ReadFailed", name, exception.Message]);
                return null;
            }
        }

        return files;
    }

    // The extensions DesignCraft accepts (for File › Open, the formats Kazo Design opens, in their
    // order), then every file (DesignCraft checks what it gets).
    private List<FileDialogFilter> FiltersOf(PickRequest request, bool open)
    {
        List<FileDialogFilter> filters = [];
        if (open)
        {
            filters.Add(new FileDialogFilter(L["DocumentsFilter"], DesignFormats.OpenExtensions, []));
        }
        else if (request.Extensions.Count > 0)
        {
            filters.Add(new FileDialogFilter(L["FilesFilter"], request.Extensions, []));
        }

        filters.Add(new FileDialogFilter(L["AllFiles"], [], []));
        return filters;
    }

    private async Task ShowProblemAsync(string message)
    {
        try
        {
            await MessageBox(L["ProblemTitle"], message, MessageBoxType.Ok, MessageBoxIcon.Error, AppContext.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The window closed.
        }
    }
}
