using Altinn.Register.Core.Utils;

namespace Altinn.Register.Tests.UnitTests
{
    public class StringExtensionsTests
    {
        [Theory]
        [InlineData("fràe", "frae")]
        [InlineData("frãe", "frae")]
        [InlineData("REèb", "reeb")]
        [InlineData("ôröe", "Oroe")]
        public void IsSimilarTo_TestPositive(string text1, string text2)
        {
            Assert.True(PersonNames.IsLastNamesSimilar(text1, text2));
        }

        [Theory]
        [InlineData("Åjue", "Ajue")]
        public void IsSimilarTo_TestNegative(string text1, string text2)
        {
            Assert.False(PersonNames.IsLastNamesSimilar(text1, text2));
        }
    }
}
