using Kazo.Os.AppSdk.Craft;

namespace Kazo.Design.Documents;

/// <summary>A save of the document decided by <see cref="DesignDocuments.Decide"/>.</summary>
/// <param name="Decision">What to do: the module's rule and record (rewrite, ask and bind, or a copy).</param>
/// <param name="Uid">The DesignCraft document the save is of, when a tab was in front.</param>
public sealed record DesignSave(SaveDecision Decision, long? Uid);

/// <summary>A file the window handed to DesignCraft, until its tab is listed (<see cref="DesignDocuments.Opening"/>).</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Name">The file's name, which DesignCraft keeps as its path for a <c>.designcraft</c> (fork, H2).</param>
/// <param name="Native">Whether the file is a <c>.designcraft</c>; an IDML or InDesign file is converted instead.</param>
/// <param name="Before">The documents open when the file was sent: its tab is a new one.</param>
public sealed record PendingOpen(string Path, string Name, bool Native, IReadOnlySet<long> Before);

/// <summary>
/// The documents of one Kazo Design window and their files (doc/en/plan.md, work item 2): each
/// DesignCraft tab is bound to its file by the document's <c>uid</c> (decision 6), and each save of a
/// document is decided here (decision 8).
/// </summary>
/// <remarks>
/// <para>
/// DesignCraft in host mode (fork F2) keeps the name of a <c>.designcraft</c> the window hands over as
/// the document's path, and hands each save over with that name and <c>saveAs</c> (H3): false only
/// when the document is written over the file it came from. Binding by <c>uid</c> rather than by name
/// lets two documents of the same name from two folders share a window, and keeps a document bound
/// to the file the user chose in the Save dialog even though DesignCraft keeps the name it suggested.
/// </para>
/// <para>
/// An IDML or InDesign file is converted when it opens and is never written again (decision 8): its
/// document has no file until its first save, which asks where to put a <c>.designcraft</c>, in the
/// original's folder. DesignCraft marks a document saved only when the window answers that the file
/// was written (H4), so its <c>dirty</c> state is trusted as is.
/// </para>
/// </remarks>
public sealed class DesignDocuments
{
    /// <summary>The window's name for its app, at the end of its title.</summary>
    public const string AppName = "Kazo Design";

    // The .designcraft file each document is written to, by uid.
    private readonly Dictionary<long, string> _files = [];

    // The IDML or InDesign file a document was converted from, until it is saved as a .designcraft:
    // the folder its first save opens in, and the tab a second open of that file brings back.
    private readonly Dictionary<long, string> _sources = [];

    private readonly List<PendingOpen> _pending = [];
    private IReadOnlyList<DesignTab> _tabs = [];
    private bool _frameDirty;

    /// <summary>Gets the tabs as DesignCraft last listed them.</summary>
    public IReadOnlyList<DesignTab> Tabs => _tabs;

    /// <summary>Gets the tab in front, or <see langword="null"/> when DesignCraft shows its start screen.</summary>
    public DesignTab? Active => _tabs.FirstOrDefault(tab => tab.Active);

    /// <summary>Gets whether the window has work that is not in a file.</summary>
    public bool IsDirty => _frameDirty || _tabs.Any(tab => tab.Dirty);

    /// <summary>Gets the files sent to DesignCraft whose tab is not listed yet.</summary>
    public IReadOnlyList<PendingOpen> Pending => _pending;

    /// <summary>Gets the <c>.designcraft</c> file a document is written to.</summary>
    /// <param name="uid">The document's uid.</param>
    /// <returns>The file's absolute path, or <see langword="null"/> when it has none yet.</returns>
    public string? FileOf(long uid) => _files.GetValueOrDefault(uid);

    /// <summary>
    /// Takes DesignCraft's tab list (<c>document.list</c>, read by <see cref="DesignTab.ListFrom"/>):
    /// binds the tab of each file sent and now listed, and forgets the documents that closed.
    /// </summary>
    /// <param name="tabs">The tabs DesignCraft has open.</param>
    public void Update(IReadOnlyList<DesignTab> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        _tabs = tabs;
        HashSet<long> open = [.. tabs.Select(tab => tab.Uid)];
        foreach (long closed in _files.Keys.Concat(_sources.Keys).Where(uid => !open.Contains(uid)).ToList())
        {
            _files.Remove(closed);
            _sources.Remove(closed);
        }

        foreach (PendingOpen pending in _pending.ToList())
        {
            if (NewTabOf(pending) is { } tab)
            {
                Bind(tab.Uid, pending);
                _pending.Remove(pending);
            }
        }
    }

    /// <summary>DesignCraft reported that unsaved changes appeared, or went away, in its tabs (H6).</summary>
    /// <param name="dirty">The new state.</param>
    public void SetFrameDirty(bool dirty) => _frameDirty = dirty;

    /// <summary>DesignCraft started again (Retry): its documents and their uids are gone.</summary>
    public void Reset()
    {
        _files.Clear();
        _sources.Clear();
        _pending.Clear();
        _tabs = [];
        _frameDirty = false;
    }

    /// <summary>The tab of a file already open in this window, whatever DesignCraft names it.</summary>
    /// <param name="path">The file's absolute path.</param>
    /// <returns>The tab, or <see langword="null"/>.</returns>
    public DesignTab? TabOf(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return _tabs.FirstOrDefault(tab =>
            string.Equals(_files.GetValueOrDefault(tab.Uid), path, StringComparison.Ordinal)
            || string.Equals(_sources.GetValueOrDefault(tab.Uid), path, StringComparison.Ordinal));
    }

    /// <summary>
    /// A file is about to be sent to DesignCraft: the next new tab that matches it (its name as the
    /// path for a <c>.designcraft</c>, no path for a converted file) is bound to it.
    /// </summary>
    /// <param name="path">The file's absolute path.</param>
    /// <returns>The pending open, to <see cref="Forget"/> when DesignCraft never shows it.</returns>
    public PendingOpen Opening(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string name = CraftDocumentFormats.FileNameOf(path);
        bool native = string.Equals(CraftDocumentFormats.ExtensionOf(name), DesignFormats.NativeExtension, StringComparison.Ordinal);
        PendingOpen pending = new(path, name, native, new HashSet<long>(_tabs.Select(tab => tab.Uid)));
        _pending.Add(pending);
        return pending;
    }

    /// <summary>DesignCraft did not show a file sent to it (it could not read it): it is no longer awaited.</summary>
    /// <param name="pending">The pending open.</param>
    public void Forget(PendingOpen pending) => _pending.Remove(pending);

    /// <summary>
    /// Decides a save of the document in front (decision 8): written over its file when DesignCraft
    /// says it is its own (<c>saveAs</c> false) and the window knows that file; otherwise the OS Save
    /// dialog, in the folder of the document's file or of the file it was converted from.
    /// </summary>
    /// <param name="name">The name DesignCraft gave (always a <c>.designcraft</c>).</param>
    /// <param name="saveAs">DesignCraft's <c>saveAs</c>.</param>
    /// <returns>The decision.</returns>
    public DesignSave Decide(string name, bool saveAs)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        DesignTab? tab = Active;
        string? folder = FolderOf(tab);
        if (!saveAs && tab is not null && _files.TryGetValue(tab.Uid, out string? path))
        {
            return new DesignSave(new SaveDecision(SaveAction.Rewrite, name, path, folder), tab.Uid);
        }

        return new DesignSave(new SaveDecision(SaveAction.AskAndBind, name, null, folder), tab?.Uid);
    }

    /// <summary>Decides an export (a copy, ADR 0264 of Kazo OS): always asks, in the folder of the document in front.</summary>
    /// <param name="name">The name DesignCraft gave.</param>
    /// <returns>The decision.</returns>
    public SaveDecision DecideCopy(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new SaveDecision(SaveAction.AskForCopy, name, null, FolderOf(Active));
    }

    /// <summary>The file was written: a save of the document binds it to that file.</summary>
    /// <param name="save">The save carried out.</param>
    /// <param name="path">The file written.</param>
    public void Saved(DesignSave save, string path)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (save.Decision.SavesTheDocument && save.Uid is { } uid)
        {
            _files[uid] = path;
            _sources.Remove(uid);
        }
    }

    /// <summary>Picks the tabs with work that is not in a file, in DesignCraft's order.</summary>
    /// <returns>The tabs to save before the window closes.</returns>
    public IReadOnlyList<DesignTab> WithUnsavedWork() => [.. _tabs.Where(tab => tab.Dirty)];

    /// <summary>The name of a document in a message or a title: its file's, else its tab's.</summary>
    /// <param name="tab">The tab.</param>
    /// <param name="untitled">The name of a document with no name, in the user's language.</param>
    /// <returns>The name.</returns>
    public string NameOf(DesignTab tab, string untitled)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return _files.TryGetValue(tab.Uid, out string? path)
            ? CraftDocumentFormats.FileNameOf(path)
            : tab.Title.Length > 0 ? tab.Title : untitled;
    }

    /// <summary>
    /// Builds the window title: <c>*name — Kazo Design</c>, the name of the front tab's file (or of the
    /// tab when it has none), the asterisk while any tab has work that is not in a file.
    /// </summary>
    /// <param name="untitled">The name of a document with no name, in the user's language.</param>
    /// <param name="running">Whether DesignCraft is running.</param>
    /// <returns>The title.</returns>
    public string Title(string untitled, bool running = true)
    {
        if (!running)
        {
            return AppName;
        }

        string mark = IsDirty ? "*" : string.Empty;
        return Active is { } tab ? $"{mark}{NameOf(tab, untitled)} — {AppName}" : $"{mark}{AppName}";
    }

    // The folder a dialog about this document opens in: its file's, else its original's.
    private string? FolderOf(DesignTab? tab) =>
        tab is null ? null
        : (_files.GetValueOrDefault(tab.Uid) ?? _sources.GetValueOrDefault(tab.Uid)) is { } path ? CraftDocumentFormats.FolderOf(path)
        : null;

    // The new tab of a file sent: one that was not open then, is bound to nothing, and carries the
    // file's name as its path (a .designcraft) or no path (a converted file).
    private DesignTab? NewTabOf(PendingOpen pending) =>
        _tabs.FirstOrDefault(tab =>
            !pending.Before.Contains(tab.Uid)
            && !_files.ContainsKey(tab.Uid)
            && !_sources.ContainsKey(tab.Uid)
            && (pending.Native
                ? string.Equals(tab.Path, pending.Name, StringComparison.Ordinal)
                : tab.Path is null));

    private void Bind(long uid, PendingOpen pending)
    {
        if (pending.Native)
        {
            _files[uid] = pending.Path;
        }
        else
        {
            _sources[uid] = pending.Path;
        }
    }
}
