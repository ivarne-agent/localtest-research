using System.Text;

using Altinn.Register.Models;

namespace Altinn.Register.Tests.UnitTests
{
    public class PersonLookupIdentifiersTests
    {
        [Fact]
        public void LastNameTest_ReadNull_ReturnsNull()
        {
            // Arrange
            var target = new PersonLookupIdentifiers
            {
                LastName = null
            };

            // Act
            var actual = target.LastName;

            // Assert
            Assert.Null(actual);
        }

        [Fact]
        public void LastNameTest_ReadEmpty_ReturnsEmpty()
        {
            // Arrange
            var target = new PersonLookupIdentifiers
            {
                LastName = string.Empty
            };

            // Act
            var actual = target.LastName;

            // Assert
            Assert.Equal(string.Empty, actual);
        }

        [Fact]
        public void LastNameTest_ReadNotEncoded_ReturnsLiteral()
        {
            // Arrange
            var target = new PersonLookupIdentifiers
            {
                LastName = "hopla"
            };

            // Act
            var actual = target.LastName;

            // Asserts
            Assert.Equal("hopla", actual);
        }

        [Fact]
        public void LastNameTest_ReadLiteralThatIsValidBase64ButInvalidUtf8_ReturnsLiteral()
        {
            // Arrange
            var target = new PersonLookupIdentifiers
            {
                LastName = "Aase"
            };

            // Act
            var actual = target.LastName;

            // Assert
            Assert.Equal("Aase", actual);
        }

        [Fact]
        public void LastNameTest_ReadEncoded_ReturnsDecoded()
        {
            // Arrange
            var bytes = Encoding.UTF8.GetBytes("Hørtfør");
            var base64 = Convert.ToBase64String(bytes);
            var target = new PersonLookupIdentifiers
            {
                LastName = base64
            };

            // Act
            var actual = target.LastName;

            // Asserts
            Assert.Equal("Hørtfør", actual);
        }

        [Fact]
        public void LastNameTest_ReadLongEncoded_ReturnsDecoded()
        {
            // Arrange
            var original = new string('a', 160);
            var bytes = Encoding.UTF8.GetBytes(original);
            var base64 = Convert.ToBase64String(bytes);
            var target = new PersonLookupIdentifiers
            {
                LastName = base64
            };

            // Act
            var actual = target.LastName;

            // Assert
            Assert.Equal(original, actual);
        }

        [Fact]
        public void LastNameTest_ReadTooLong_ThrowsArgumentException()
        {
            // Arrange
            var target = new PersonLookupIdentifiers();
            var tooLong = new string('a', 4097);

            // Act
            var action = () => target.LastName = tooLong;

            // Assert
            var exception = Assert.Throws<ArgumentException>(action);
            Assert.Equal("value", exception.ParamName);
        }
    }
}
