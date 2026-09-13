using FluentAssertions;
using PharmaX.Adapters.Files;

namespace PharmaX.Tests.Unit.Adapters;

/// <summary>
/// The filing cabinet for scanned prescriptions, against a real folder.
/// </summary>
public sealed class AttachmentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"phx-attach-{Guid.NewGuid():N}");

    private readonly AttachmentStore _store;

    public AttachmentStoreTests() => _store = new AttachmentStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task A_scan_is_copied_in_and_can_be_found_again()
    {
        var patient = Guid.NewGuid();
        var source = GivenAFile("script.png", 2048);

        var stored = await _store.SaveAsync(patient, source);

        stored.SizeBytes.Should().Be(2048);
        stored.ContentType.Should().Be("image/png");
        _store.Exists(stored.RelativePath).Should().BeTrue();

        // Under the patient's own folder, so one patient's file can be
        // handed over or removed without sifting a shared directory.
        stored.RelativePath.Should().StartWith(patient.ToString("N"));

        File.ReadAllBytes(_store.Resolve(stored.RelativePath))
            .Should().HaveCount(2048);
    }

    /// <remarks>
    /// The same prescription scanned twice in a minute is ordinary, and
    /// the second must not quietly replace the first.
    /// </remarks>
    [Fact]
    public async Task Two_scans_of_the_same_file_do_not_collide()
    {
        var patient = Guid.NewGuid();
        var source = GivenAFile("script.png", 512);

        var first = await _store.SaveAsync(patient, source);
        var second = await _store.SaveAsync(patient, source);

        second.RelativePath.Should().NotBe(first.RelativePath);
        _store.Exists(first.RelativePath).Should().BeTrue();
        _store.Exists(second.RelativePath).Should().BeTrue();
    }

    /// <remarks>
    /// A stored path is usually ours, but it also arrives from the host,
    /// and a row carrying a climbing path must not be able to read or
    /// delete outside the cabinet.
    /// </remarks>
    [Theory]
    [InlineData("../../../windows/system32/drivers/etc/hosts")]
    [InlineData("..\\..\\secrets.txt")]
    public void A_path_that_climbs_out_of_the_cabinet_is_refused(string path)
    {
        var resolving = () => _store.Resolve(path);

        resolving.Should().Throw<InvalidOperationException>();

        // And the convenience methods must not leak it either.
        _store.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Removing_a_scan_takes_the_image_with_it()
    {
        var stored = await _store.SaveAsync(Guid.NewGuid(),
                                            GivenAFile("card.jpg", 64));

        _store.Delete(stored.RelativePath);

        _store.Exists(stored.RelativePath).Should().BeFalse();
    }

    [Fact]
    public async Task A_file_that_has_since_been_moved_is_reported_plainly()
    {
        var missing = Path.Combine(_root, "never-existed.png");

        var saving = async () =>
            await _store.SaveAsync(Guid.NewGuid(), missing);

        await saving.Should().ThrowAsync<FileNotFoundException>();
    }

    private string GivenAFile(string name, int bytes)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }
}
