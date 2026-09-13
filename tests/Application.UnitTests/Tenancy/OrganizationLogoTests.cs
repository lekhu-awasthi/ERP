using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Tenancy.Commands.RemoveOrganizationLogo;
using ErpApp.Application.Tenancy.Commands.SetOrganizationLogo;
using ErpApp.Application.Tenancy.Queries.GetOrganizationProfile;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Tenancy;
using FluentValidation;

namespace ErpApp.Application.UnitTests.Tenancy;

/// <summary>
/// Phase 39 — the organization logo's write path.
///
/// <para>Weighted towards refusals rather than the happy path, because the happy path is one line
/// and the refusals are the feature: this file ends up in customer-facing PDFs and in browsers, so
/// "what does it reject" is the question that matters. The <see cref="A_file_that_is_not_an_image_is_refused_whatever_it_is_called"/>
/// case is the one the whole <c>ImageHeader</c> design exists for.</para>
/// </summary>
public class OrganizationLogoTests
{
    private static async Task<(IAppDbContext Db, Guid OrganizationId)> SeedAsync()
    {
        var db = TestAppDbContext.Create();
        var organization = Organization.Create(
            "Moonbeam Trading", "Retail", "Manbhawan", new DateOnly(2026, 4, 1), true,
            "moonbeam", "hello@example.test", "9705056788", "031564579", null, Guid.NewGuid());

        db.Organizations.Add(organization);
        await db.SaveChangesAsync(CancellationToken.None);

        return (db, organization.Id);
    }

    /// <summary>A PNG header is all these tests need: nothing here decodes pixels, and the size
    /// checks read exactly these bytes. <c>Api.IntegrationTests.TestPng</c> builds a real one for the
    /// print assertions, which do.</summary>
    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        "IHDR"u8.ToArray().CopyTo(bytes, 12);

        foreach (var (offset, value) in new[] { (16, width), (20, height) })
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

        return bytes;
    }

    private static SetOrganizationLogoCommand Upload(Guid organizationId, byte[] content, string name = "logo.png") =>
        new(organizationId, name, content.Length, new MemoryStream(content));

    // --- the happy path -----------------------------------------------------------------------

    [Fact]
    public async Task A_valid_logo_is_stored_and_the_profile_reports_it()
    {
        var (db, organizationId) = await SeedAsync();
        var storage = new FakeFileStorage();

        var result = await new SetOrganizationLogoCommandHandler(db, storage).Handle(
            Upload(organizationId, PngHeader(300, 300)), CancellationToken.None);

        Assert.True(result.HasLogo);
        Assert.Equal("image/png", result.ContentType);

        var profile = await new GetOrganizationProfileQueryHandler(db).Handle(
            new GetOrganizationProfileQuery(organizationId), CancellationToken.None);

        Assert.True(profile.HasLogo);
    }

    /// <summary>
    /// The content type is taken from the bytes, not from the upload's own claim. A caller that
    /// names a PNG "logo.gif" gets image/png back, because that is what the file is.
    /// </summary>
    [Fact]
    public async Task The_stored_content_type_comes_from_the_bytes_not_the_file_name()
    {
        var (db, organizationId) = await SeedAsync();

        var result = await new SetOrganizationLogoCommandHandler(db, new FakeFileStorage()).Handle(
            Upload(organizationId, PngHeader(300, 300), "corporate-identity.gif"), CancellationToken.None);

        Assert.Equal("image/png", result.ContentType);
    }

    // --- refusals -----------------------------------------------------------------------------

    /// <summary>
    /// The case <c>ImageHeader</c> exists for. The declared name and length say PNG; the bytes are a
    /// script. Rejecting on the bytes is what stops this app serving that content back to a browser
    /// under an image content type.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_whatever_it_is_called()
    {
        var (db, organizationId) = await SeedAsync();
        var payload = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new SetOrganizationLogoCommandHandler(db, new FakeFileStorage()).Handle(
                Upload(organizationId, payload), CancellationToken.None));

        Assert.Contains("PNG, JPEG or GIF", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_logo_below_the_minimum_side_is_refused_and_the_message_names_both_sizes()
    {
        var (db, organizationId) = await SeedAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            new SetOrganizationLogoCommandHandler(db, new FakeFileStorage()).Handle(
                Upload(organizationId, PngHeader(299, 300)), CancellationToken.None));

        Assert.Contains("300", exception.Message, StringComparison.Ordinal);
        Assert.Contains("299", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Refused on the declared length, before a byte is buffered. Buffering first and checking after
    /// would let a caller with a dishonest length make the server hold whatever it liked in memory.
    /// </summary>
    [Fact]
    public async Task An_oversized_logo_is_refused_before_it_is_read()
    {
        var (db, organizationId) = await SeedAsync();
        var storage = new FakeFileStorage();

        await Assert.ThrowsAsync<ValidationException>(() =>
            new SetOrganizationLogoCommandHandler(db, storage).Handle(
                new SetOrganizationLogoCommand(
                    organizationId, "huge.png",
                    SetOrganizationLogoCommandHandler.MaxSizeBytes + 1,
                    new MemoryStream(PngHeader(300, 300))),
                CancellationToken.None));

        Assert.Empty(storage.SavedKeys);
    }

    [Fact]
    public async Task A_refused_logo_leaves_no_blob_behind()
    {
        var (db, organizationId) = await SeedAsync();
        var storage = new FakeFileStorage();

        await Assert.ThrowsAsync<ValidationException>(() =>
            new SetOrganizationLogoCommandHandler(db, storage).Handle(
                Upload(organizationId, "not an image"u8.ToArray()), CancellationToken.None));

        Assert.Empty(storage.SavedKeys);
    }

    // --- the deletion story (phase-21b Decision E) ---------------------------------------------

    [Fact]
    public async Task Replacing_a_logo_deletes_the_blob_it_replaced()
    {
        var (db, organizationId) = await SeedAsync();
        var storage = new FakeFileStorage();
        var handler = new SetOrganizationLogoCommandHandler(db, storage);

        await handler.Handle(Upload(organizationId, PngHeader(300, 300)), CancellationToken.None);
        var first = Assert.Single(storage.SavedKeys);

        await handler.Handle(Upload(organizationId, PngHeader(400, 400)), CancellationToken.None);

        Assert.Contains(first, storage.DeletedKeys);
        Assert.Equal(2, storage.SavedKeys.Count);
    }

    [Fact]
    public async Task Removing_a_logo_deletes_its_blob_and_clears_the_flag()
    {
        var (db, organizationId) = await SeedAsync();
        var storage = new FakeFileStorage();

        await new SetOrganizationLogoCommandHandler(db, storage).Handle(
            Upload(organizationId, PngHeader(300, 300)), CancellationToken.None);
        var key = Assert.Single(storage.SavedKeys);

        var result = await new RemoveOrganizationLogoCommandHandler(db, storage).Handle(
            new RemoveOrganizationLogoCommand(organizationId), CancellationToken.None);

        Assert.False(result.HasLogo);
        Assert.Contains(key, storage.DeletedKeys);
    }

    /// <summary>Removing a logo that is not there leaves the tenant with no logo, which is what the
    /// button promises. A 404 would be technically defensible and useless.</summary>
    [Fact]
    public async Task Removing_a_logo_that_is_not_there_is_a_no_op()
    {
        var (db, organizationId) = await SeedAsync();
        var storage = new FakeFileStorage();

        var result = await new RemoveOrganizationLogoCommandHandler(db, storage).Handle(
            new RemoveOrganizationLogoCommand(organizationId), CancellationToken.None);

        Assert.False(result.HasLogo);
        Assert.Empty(storage.DeletedKeys);
    }
}
