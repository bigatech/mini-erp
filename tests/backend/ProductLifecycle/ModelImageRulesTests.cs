using ProductLifecycle.Application;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;
public sealed class ModelImageRulesTests
{
    [Fact] public void File_extension_cannot_make_SVG_an_accepted_image()=>Assert.Throws<ArgumentException>(()=>ModelImageRules.ContentType(System.Text.Encoding.UTF8.GetBytes("<svg></svg>")));
    [Fact] public void Oversize_is_rejected_before_upload()=>Assert.Throws<ArgumentException>(()=>ModelImageRules.ContentType(new byte[ModelImageRules.MaximumBytes+1]));
    [Fact] public void Png_is_identified_from_content()=>Assert.Equal("image/png",ModelImageRules.ContentType(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNCcAAAAASUVORK5CYII=")));
}
