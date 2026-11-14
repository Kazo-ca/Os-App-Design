using Kazo.Design.Documents;
using Kazo.Os.App.Abstractions;

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
}
