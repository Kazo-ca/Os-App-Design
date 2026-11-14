using Kazo.Design.Documents;
using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Design.Tests;

/// <summary>The file associations of decision 7 (doc/en/plan.md).</summary>
public sealed class DesignFormatsTests
{
    [Theory]
    [InlineData(".designcraft")]
    [InlineData(".idml")]
    [InlineData(".indd")]
    [InlineData(".indt")]
    public void Kazo_Design_is_the_default_app_of_its_documents_and_InDesign_files(string extension)
    {
        FileHandler handler = Assert.Single(DesignFormats.FileHandlers, handler => handler.Extensions.Contains(extension));
        Assert.Equal(["open", "edit"], handler.Actions);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".txt")]
    [InlineData(".svg")]
    [InlineData(".xlsx")]
    public void What_DesignCraft_only_places_gets_no_association(string extension)
    {
        Assert.DoesNotContain(DesignFormats.FileHandlers, handler => handler.Extensions.Contains(extension));
    }

    [Fact]
    public void Every_format_opens_none_is_a_copy_and_the_native_one_comes_first()
    {
        // Copies are told apart by DesignCraft's host mode (fork, H3), not by the extension.
        Assert.All(DesignFormats.Formats.All, format =>
        {
            Assert.True(format.Opens);
            Assert.True(format.IsDefault);
            Assert.False(format.IsCopy);
        });
        Assert.Equal([".designcraft", ".idml", ".indd", ".indt"], DesignFormats.OpenExtensions);
        Assert.Equal(DesignFormats.NativeExtension, DesignFormats.OpenExtensions[0]);
    }

    [Fact]
    public void File_Open_is_told_apart_from_the_other_pickers_of_DesignCraft()
    {
        // DesignCraft's web build (apps/designcraft-web/src/browser_policy.rs, picker_filter): Open,
        // Load Styles (fork kazo/2c-styles-picker), Place, the Swatches panel, a shortcut set.
        Assert.True(DesignFormats.IsOpenPicker(new PickRequest(1, ".designcraft,.idml,.indd,.indt", false)));
        Assert.False(DesignFormats.IsOpenPicker(new PickRequest(2, ".designcraft,.idml", false)));
        Assert.False(DesignFormats.IsOpenPicker(new PickRequest(3, ".png,.jpg,.jpeg,.pdf,.idml,.designcraft", false)));
        Assert.False(DesignFormats.IsOpenPicker(new PickRequest(4, ".ase", false)));
        Assert.False(DesignFormats.IsOpenPicker(new PickRequest(5, ".json,.txt", false)));
        Assert.False(DesignFormats.IsOpenPicker(new PickRequest(6, string.Empty, false)));
    }
}
