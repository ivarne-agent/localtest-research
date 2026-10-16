using Altinn.Register.Core.Utils;

namespace Altinn.Register.Tests.UnitTests;

public class ListExtensionsTests
{
    public static TheoryData<List<int>, int, List<int>> SwapRemoveIntCases => new()
    {
        { [0, 1, 2, 3, 4, 5, 6, 7], 0, [7, 1, 2, 3, 4, 5, 6] },
        { [0, 1, 2, 3, 4, 5, 6, 7], 3, [0, 1, 2, 7, 4, 5, 6] },
        { [0, 1, 2, 3, 4, 5, 6, 7], 7, [0, 1, 2, 3, 4, 5, 6] },
    };

    public static TheoryData<List<string>, string, List<string>> SwapRemoveStringCases => new()
    {
        { ["0", "1", "2", "3", "4", "5", "6", "7"], "0", ["7", "1", "2", "3", "4", "5", "6"] },
        { ["0", "1", "2", "3", "4", "5", "6", "7"], "3", ["0", "1", "2", "7", "4", "5", "6"] },
        { ["0", "1", "2", "3", "4", "5", "6", "7"], "7", ["0", "1", "2", "3", "4", "5", "6"] },
    };

    [Theory]
    [MemberData(nameof(SwapRemoveIntCases))]
    public void SwapRemoveInt_Test(List<int> list, int value, List<int> expected)
    {
        Assert.True(list.SwapRemove(value));
        Assert.Equal(expected, list);
    }

    [Theory]
    [MemberData(nameof(SwapRemoveStringCases))]
    public void SwapRemoveString_Test(List<string> list, string value, List<string> expected)
    {
        Assert.True(list.SwapRemove(value));
        Assert.Equal(expected, list);
    }

    [Fact]
    public void SwapRemove_NonExisting()
    {
        List<int> list = [0, 1, 2, 3, 4, 5, 6, 7];
        Assert.False(list.SwapRemove(8));
        Assert.Equal(8, list.Count);
    }

    [Fact]
    public void SwapRemove_Throws_IfNull()
    {
        List<int> list = null!;

        Assert.Throws<ArgumentNullException>(() => list.SwapRemove(0));
    }

    [Fact]
    public void SwapRemoveAt_Throws_IfNull()
    {
        List<int> list = null!;

        Assert.Throws<ArgumentNullException>(() => list.SwapRemoveAt(0));
    }

    [Fact]
    public void SwapRemoveAt_Throws_IfIndexIsNegative()
    {
        List<int> list = [0, 1, 2, 3, 4, 5, 6, 7];

        Assert.Throws<ArgumentOutOfRangeException>(() => list.SwapRemoveAt(-1));
    }

    [Fact]
    public void SwapRemoveAt_Throws_IfIndexIsOutOfRange()
    {
        List<int> list = [0, 1, 2, 3, 4, 5, 6, 7];

        Assert.Throws<ArgumentOutOfRangeException>(() => list.SwapRemoveAt(8));
    }
}
