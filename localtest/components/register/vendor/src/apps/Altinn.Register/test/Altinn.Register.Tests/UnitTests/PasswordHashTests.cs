using System.Buffers.Text;
using Altinn.Register.Core.Cryptography;

namespace Altinn.Register.Tests.UnitTests;

public class PasswordHashTests
{
    [Fact]
    public void Create_Validate_RoundTrips()
    {
        const string userName = "alice";
        const string password = "correct horse battery staple";

        var hash = PasswordHash.Create(userName, password);

        PasswordHash.Validate(userName, password, hash).ShouldBeTrue();
    }

    [Fact]
    public void Create_SameInput_ProducesDifferentHashes()
    {
        const string userName = "alice";
        const string password = "correct horse battery staple";

        var first = PasswordHash.Create(userName, password);
        var second = PasswordHash.Create(userName, password);

        first.ShouldNotBe(second);
        PasswordHash.Validate(userName, password, first).ShouldBeTrue();
        PasswordHash.Validate(userName, password, second).ShouldBeTrue();
    }

    [Fact]
    public void Validate_WrongPassword_ReturnsFalse()
    {
        var hash = PasswordHash.Create("alice", "correct horse battery staple");

        PasswordHash.Validate("alice", "wrong password", hash).ShouldBeFalse();
    }

    [Fact]
    public void Validate_WrongUserName_ReturnsFalse()
    {
        var hash = PasswordHash.Create("alice", "correct horse battery staple");

        PasswordHash.Validate("bob", "correct horse battery staple", hash).ShouldBeFalse();
    }

    [Fact]
    public void Validate_InvalidVersion_ThrowsFormatException()
    {
        var hash = PasswordHash.Create("alice", "correct horse battery staple");
        var mutatedHash = RewriteDecodedHash(hash, static bytes =>
        {
            bytes[0] = 0x02;
            return bytes;
        });

        var exn = Assert.Throws<FormatException>(() => PasswordHash.Validate("alice", "correct horse battery staple", mutatedHash));

        exn.Message.ShouldContain("Invalid client hash version");
    }

    [Fact]
    public void Validate_TruncatedPayload_ThrowsFormatException()
    {
        var hash = PasswordHash.Create("alice", "correct horse battery staple");
        var truncatedHash = RewriteDecodedHash(hash, static bytes => bytes[..^1]);

        var exn = Assert.Throws<FormatException>(() => PasswordHash.Validate("alice", "correct horse battery staple", truncatedHash));

        exn.Message.ShouldContain("length");
    }

    [Fact]
    public void Validate_MalformedHash_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => PasswordHash.Validate("alice", "correct horse battery staple", "!"));
    }

    [Fact]
    public void Create_UserNameLongerThanMax_ThrowsArgumentException()
    {
        var userName = new string('a', 257);

        var exn = Assert.Throws<ArgumentException>(() => PasswordHash.Create(userName, "correct horse battery staple"));

        exn.ParamName.ShouldBe("userName");
        exn.Message.ShouldContain("too long");
    }

    private static string RewriteDecodedHash(string hash, Func<byte[], byte[]> rewrite)
    {
        byte[] buffer = new byte[Base64Url.GetMaxDecodedLength(hash.Length)];
        var written = Base64Url.DecodeFromChars(hash, buffer);
        Array.Resize(ref buffer, written);

        buffer = rewrite(buffer);

        return Base64Url.EncodeToString(buffer);
    }
}
