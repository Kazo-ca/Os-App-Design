using System.Text.Json;
using Kazo.Design.Documents;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Design.Tests;

/// <summary>
/// The tabs bound to their files by uid and the save rules of work item 2 (doc/en/plan.md,
/// decisions 6, 8 and 9).
/// </summary>
public sealed class DesignDocumentsTests
{
    private const string Brochure = "/home/ada/Documents/Brochure.designcraft";
    private const string Catalog = "/home/ada/Clients/Catalog.idml";

    [Fact]
    public void Document_list_is_read_with_uid_index_path_and_flags()
    {
        IReadOnlyList<DesignTab> tabs = DesignTab.ListFrom(Json("""
            [
              {"index": 0, "uid": 7, "title": "Brochure", "path": "Brochure.designcraft", "dirty": false, "active": false},
              {"index": 1, "uid": 9, "title": "Untitled-1", "path": null, "dirty": true, "active": true}
            ]
            """));

        Assert.Equal(
            [new DesignTab(7, 0, "Brochure", "Brochure.designcraft", false, false), new DesignTab(9, 1, "Untitled-1", null, true, true)],
            tabs);
    }

    [Fact]
    public void Malformed_entries_from_the_iframe_are_skipped()
    {
        IReadOnlyList<DesignTab> tabs = DesignTab.ListFrom(Json("""
            [1, {"index": 0}, {"uid": 3}, {"index": -1, "uid": 4}, {"index": 0, "uid": "5"},
             {"index": 0, "uid": 6, "title": 2, "path": ""}, {"index": 1, "uid": 6}]
            """));

        Assert.Equal([new DesignTab(6, 0, string.Empty, null, false, false)], tabs);
        Assert.Empty(DesignTab.ListFrom(Json("""{"documents": []}""")));
    }

    [Fact]
    public void A_designcraft_sent_is_bound_to_its_new_tab_and_rewritten_on_save()
    {
        DesignDocuments documents = new();
        documents.Update([Tab(1, 0, "Untitled-1", null, active: true)]);

        PendingOpen pending = documents.Opening(Brochure);
        documents.Update([Tab(1, 0, "Untitled-1", null), Tab(2, 1, "Brochure", "Brochure.designcraft", active: true)]);

        Assert.Empty(documents.Pending);
        Assert.True(pending.Native);
        Assert.Equal(Brochure, documents.FileOf(2));
        Assert.Null(documents.FileOf(1));
        DesignSave save = documents.Decide("Brochure.designcraft", saveAs: false);
        Assert.Equal(new SaveDecision(SaveAction.Rewrite, "Brochure.designcraft", Brochure, "/home/ada/Documents"), save.Decision);
        Assert.Equal(2, save.Uid);
        Assert.Equal("Brochure.designcraft — Kazo Design", documents.Title("Untitled"));
    }

    [Fact]
    public void Save_As_and_a_new_document_ask_in_the_folder_of_the_file()
    {
        DesignDocuments documents = Opened(Brochure, uid: 2, path: "Brochure.designcraft");

        SaveDecision saveAs = documents.Decide("Brochure.designcraft", saveAs: true).Decision;
        Assert.Equal(new SaveDecision(SaveAction.AskAndBind, "Brochure.designcraft", null, "/home/ada/Documents"), saveAs);

        documents.Update([Tab(2, 0, "Brochure", "Brochure.designcraft"), Tab(5, 1, "Untitled-2", null, active: true)]);
        DesignSave fresh = documents.Decide("Untitled-2.designcraft", saveAs: true);
        Assert.Equal(new SaveDecision(SaveAction.AskAndBind, "Untitled-2.designcraft", null, null), fresh.Decision);
        Assert.Equal(5, fresh.Uid);
    }

    [Fact]
    public void A_save_without_saveAs_of_a_document_the_window_never_bound_still_asks()
    {
        DesignDocuments documents = new();
        documents.Update([Tab(3, 0, "Dropped", "Dropped.designcraft", active: true)]);

        Assert.Equal(SaveAction.AskAndBind, documents.Decide("Dropped.designcraft", saveAs: false).Decision.Action);
    }

    [Fact]
    public void Save_As_binds_the_file_chosen_even_though_DesignCraft_keeps_its_suggested_name()
    {
        DesignDocuments documents = Opened(Brochure, uid: 2, path: "Brochure.designcraft");
        DesignSave saveAs = documents.Decide("Brochure.designcraft", saveAs: true);

        documents.Saved(saveAs, "/home/ada/Archive/Brochure v2.designcraft");

        // DesignCraft's path stays the name it handed over; the next Save rewrites the chosen file.
        SaveDecision next = documents.Decide("Brochure.designcraft", saveAs: false).Decision;
        Assert.Equal(SaveAction.Rewrite, next.Action);
        Assert.Equal("/home/ada/Archive/Brochure v2.designcraft", next.Path);
        Assert.Equal("Brochure v2.designcraft — Kazo Design", documents.Title("Untitled"));
    }

    [Fact]
    public void A_converted_IDML_is_never_rewritten_and_its_first_save_asks_next_to_it()
    {
        DesignDocuments documents = new();
        PendingOpen pending = documents.Opening(Catalog);
        Assert.False(pending.Native);
        documents.Update([Tab(4, 0, "Catalog", null, dirty: false, active: true)]);

        DesignSave first = documents.Decide("Catalog.designcraft", saveAs: true);
        Assert.Equal(new SaveDecision(SaveAction.AskAndBind, "Catalog.designcraft", null, "/home/ada/Clients"), first.Decision);
        Assert.Null(documents.FileOf(4));
        Assert.Equal(4, documents.TabOf(Catalog)?.Uid);

        documents.Saved(first, "/home/ada/Clients/Catalog.designcraft");
        documents.Update([Tab(4, 0, "Catalog", "Catalog.designcraft", active: true)]);

        SaveDecision second = documents.Decide("Catalog.designcraft", saveAs: false).Decision;
        Assert.Equal(new SaveDecision(SaveAction.Rewrite, "Catalog.designcraft", "/home/ada/Clients/Catalog.designcraft", "/home/ada/Clients"), second);
        Assert.Null(documents.TabOf(Catalog));
    }

    [Fact]
    public void Two_documents_of_the_same_name_from_two_folders_keep_their_own_file()
    {
        DesignDocuments documents = Opened(Brochure, uid: 2, path: "Brochure.designcraft");
        const string other = "/home/ada/Old/Brochure.designcraft";
        documents.Opening(other);
        documents.Update([Tab(2, 0, "Brochure", "Brochure.designcraft"), Tab(8, 1, "Brochure", "Brochure.designcraft", active: true)]);

        Assert.Equal(other, documents.Decide("Brochure.designcraft", saveAs: false).Decision.Path);
        Assert.Equal(2, documents.TabOf(Brochure)?.Uid);
        Assert.Equal(8, documents.TabOf(other)?.Uid);
    }

    [Fact]
    public void A_new_tab_that_does_not_match_the_file_sent_is_not_bound()
    {
        DesignDocuments documents = new();
        documents.Opening(Brochure);

        // The user made a new document while the file was still opening.
        documents.Update([Tab(1, 0, "Untitled-1", null, active: true)]);

        Assert.Single(documents.Pending);
        Assert.Null(documents.FileOf(1));
    }

    [Fact]
    public void A_file_DesignCraft_never_shows_can_be_forgotten()
    {
        DesignDocuments documents = new();
        PendingOpen pending = documents.Opening(Brochure);

        documents.Forget(pending);
        documents.Update([Tab(1, 0, "Brochure", "Brochure.designcraft", active: true)]);

        Assert.Null(documents.FileOf(1));
    }

    [Fact]
    public void A_closed_document_loses_its_binding()
    {
        DesignDocuments documents = Opened(Brochure, uid: 2, path: "Brochure.designcraft");

        documents.Update([]);

        Assert.Null(documents.FileOf(2));
        Assert.Null(documents.TabOf(Brochure));
        Assert.Equal("Kazo Design", documents.Title("Untitled"));
    }

    [Fact]
    public void Exports_are_copies_asked_in_the_folder_of_the_document_in_front()
    {
        DesignDocuments documents = Opened(Brochure, uid: 2, path: "Brochure.designcraft");

        SaveDecision copy = documents.DecideCopy("Brochure.pdf");

        Assert.Equal(new SaveDecision(SaveAction.AskForCopy, "Brochure.pdf", null, "/home/ada/Documents"), copy);
        documents.Saved(new DesignSave(copy, 2), "/home/ada/Documents/Brochure.pdf");
        Assert.Equal(Brochure, documents.FileOf(2));
    }

    [Fact]
    public void The_close_guard_lists_the_modified_tabs_and_the_title_marks_them()
    {
        DesignDocuments documents = Opened(Brochure, uid: 2, path: "Brochure.designcraft");
        documents.Update([Tab(2, 0, "Brochure", "Brochure.designcraft", dirty: true), Tab(5, 1, "Untitled-2", null, active: true)]);

        Assert.Equal([2L], documents.WithUnsavedWork().Select(tab => tab.Uid));
        Assert.True(documents.IsDirty);
        Assert.Equal("*Untitled-2 — Kazo Design", documents.Title("Untitled"));
        Assert.Equal("Kazo Design", documents.Title("Untitled", running: false));
    }

    [Fact]
    public void The_frame_dirty_flag_marks_the_title_before_the_tabs_are_read()
    {
        DesignDocuments documents = new();

        documents.SetFrameDirty(true);

        Assert.Equal("*Kazo Design", documents.Title("Untitled"));
    }

    [Fact]
    public void Reset_forgets_everything_of_the_DesignCraft_that_stopped()
    {
        DesignDocuments documents = Opened(Brochure, uid: 2, path: "Brochure.designcraft");
        documents.Opening(Catalog);
        documents.SetFrameDirty(true);

        documents.Reset();

        Assert.Empty(documents.Tabs);
        Assert.Empty(documents.Pending);
        Assert.Null(documents.FileOf(2));
        Assert.False(documents.IsDirty);
    }

    private static DesignDocuments Opened(string file, long uid, string path)
    {
        DesignDocuments documents = new();
        documents.Opening(file);
        documents.Update([Tab(uid, 0, Path.GetFileNameWithoutExtension(file), path, active: true)]);
        Assert.Equal(file, documents.FileOf(uid));
        return documents;
    }

    private static DesignTab Tab(long uid, int index, string title, string? path, bool dirty = false, bool active = false) =>
        new(uid, index, title, path, dirty, active);

    private static JsonElement Json(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
