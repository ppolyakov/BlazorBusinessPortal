using BusinessPortal.Web.Services;

namespace BusinessPortal.IntegrationTests;

public sealed class AvatarImagePolicyTests
{
    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    public void DetectContentType_accepts_supported_image_signatures(byte[] content, string expected) =>
        Assert.Equal(expected, AvatarImagePolicy.DetectContentType(content));

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 })]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 })]
    public void DetectContentType_rejects_unsupported_or_spoofed_files(byte[] content) =>
        Assert.Null(AvatarImagePolicy.DetectContentType(content));
}
