using System.Text.Json;

namespace Altinn.Register.Contracts.Tests;

public class TranslatedTextTests
{
    [Fact]
    public void Building_WithMissingRequiredLanguages_Throws()
    {
        var builder = TranslatedText.CreateBuilder();

        builder.TryToImmutable(out _).ShouldBeFalse();
        Assert.Throws<InvalidOperationException>(() => builder.ToImmutable());

        builder.Add(LangCode.En, "1");
        builder.TryToImmutable(out _).ShouldBeFalse();
        Assert.Throws<InvalidOperationException>(() => builder.ToImmutable());

        builder.Add(LangCode.Nb, "2");
        builder.TryToImmutable(out _).ShouldBeFalse();
        Assert.Throws<InvalidOperationException>(() => builder.ToImmutable());
    }

    [Fact]
    public void Building_WithRequiredLanguages_Works()
    {
        var builder = TranslatedText.CreateBuilder();

        builder.Add(LangCode.En, "en-language");
        builder.Add(LangCode.Nb, "nb-språk");
        builder.Add(LangCode.Nn, "nn-språk");

        builder.TryToImmutable(out var text).ShouldBeTrue();

        text.En.ShouldBe("en-language");
        text.Nb.ShouldBe("nb-språk");
        text.Nn.ShouldBe("nn-språk");

        text[LangCode.En].ShouldBe("en-language");
        text[LangCode.Nb].ShouldBe("nb-språk");
        text[LangCode.Nn].ShouldBe("nn-språk");

        var enumerated = ((IEnumerable<KeyValuePair<LangCode, string>>)text).ToList();
        enumerated.ShouldBe(
            [
                KeyValuePair.Create(LangCode.En, "en-language"),
                KeyValuePair.Create(LangCode.Nb, "nb-språk"),
                KeyValuePair.Create(LangCode.Nn, "nn-språk"),
            ],
            ignoreOrder: true);

        var keys = ((IReadOnlyDictionary<LangCode, string>)text).Keys.ToList();
        keys.ShouldBe([LangCode.En, LangCode.Nb, LangCode.Nn], ignoreOrder: true);

        var stringKeys = ((IDictionary<string, string>)text).Keys;
        stringKeys.ShouldBe([LangCode.En.Code, LangCode.Nb.Code, LangCode.Nn.Code], ignoreOrder: true);
    }

    [Fact]
    public void Building_WithAdditionalLanguages_Works()
    {
        var builder = TranslatedText.CreateBuilder();

        builder.Add(LangCode.En, "1");
        builder.Add(LangCode.Nb, "2");
        builder.Add(LangCode.Nn, "3");
        builder.Add(LangCode.FromCode("fr"), "4");
        builder.Add(LangCode.FromCode("dk"), "5");

        builder.TryToImmutable(out var text).ShouldBeTrue();

        text.ShouldSatisfyAllConditions(
            t => t.En.ShouldBe("1"),
            t => t.Nb.ShouldBe("2"),
            t => t.Nn.ShouldBe("3"));

        text.ShouldSatisfyAllConditions(
            t => t.ShouldContainKeyAndValue(LangCode.En, "1"),
            t => t.ShouldContainKeyAndValue(LangCode.Nb, "2"),
            t => t.ShouldContainKeyAndValue(LangCode.Nn, "3"),
            t => t.ShouldContainKeyAndValue(LangCode.FromCode("fr"), "4"),
            t => t.ShouldContainKeyAndValue(LangCode.FromCode("dk"), "5"));

        var keys = ((IReadOnlyDictionary<LangCode, string>)text).Keys.ToList();
        keys.ShouldBe([LangCode.En, LangCode.Nb, LangCode.Nn, LangCode.FromCode("fr"), LangCode.FromCode("dk")], ignoreOrder: true);

        var stringKeys = ((IDictionary<string, string>)text).Keys;
        stringKeys.ShouldBe([LangCode.En.Code, LangCode.Nb.Code, LangCode.Nn.Code, LangCode.FromCode("fr").Code, LangCode.FromCode("dk").Code], ignoreOrder: true);
    }

    [Fact]
    public void Builder_AddingSameLanguageTwice_Throws()
    {
        var builder = TranslatedText.CreateBuilder();

        builder.Add(LangCode.En, "hello");
        Assert.Throws<ArgumentException>(() => builder.Add(LangCode.En, "hello"));

        builder.Add(LangCode.FromCode("fr"), "bonjour");
        Assert.Throws<ArgumentException>(() => builder.Add(LangCode.FromCode("fr"), "bonjour"));
    }

    [Fact]
    public void Builder_Count_ReturnsCorrectCount()
    {
        var builder = TranslatedText.CreateBuilder();

        builder.Count.ShouldBe(0);

        builder.Add(LangCode.En, "en-language");
        builder.Count.ShouldBe(1);

        builder.Add(LangCode.FromCode("fr"), "bonjour");
        builder.Count.ShouldBe(2);

        builder.Add(LangCode.Nb, "hei");
        builder.Count.ShouldBe(3);

        builder.Remove(LangCode.En);
        builder.Count.ShouldBe(2);

        builder.Remove(LangCode.FromCode("fr"));
        builder.Count.ShouldBe(1);

        builder.Remove(LangCode.Nb);
        builder.Count.ShouldBe(0);
    }

    [Fact]
    public void Builder_Keys_ReturnsAllKeys()
    {
        var builder = TranslatedText.CreateBuilder();
        var fr = LangCode.FromCode("fr");

        builder.Count.ShouldBe(0);
        builder.ContainsKey(fr).ShouldBeFalse();

        builder.Add(LangCode.En, "hello");
        builder.Keys.ShouldBe([LangCode.En], ignoreOrder: true);
        builder.ContainsKey(LangCode.En).ShouldBeTrue();

        builder.Add(fr, "bonjour");
        builder.Keys.ShouldBe([LangCode.En, fr], ignoreOrder: true);
        builder.ContainsKey(fr).ShouldBeTrue();

        builder.Add(LangCode.Nb, "hei");
        builder.Keys.ShouldBe([LangCode.En, fr, LangCode.Nb], ignoreOrder: true);
        builder.ContainsKey(LangCode.Nb).ShouldBeTrue();

        builder.Remove(LangCode.En);
        builder.Keys.ShouldBe([fr, LangCode.Nb], ignoreOrder: true);
        builder.ContainsKey(LangCode.En).ShouldBeFalse();

        builder.Remove(fr);
        builder.Keys.ShouldBe([LangCode.Nb], ignoreOrder: true);
        builder.ContainsKey(fr).ShouldBeFalse();

        builder.Remove(LangCode.Nb);
        builder.Keys.ShouldBeEmpty();
        builder.ContainsKey(LangCode.Nb).ShouldBeFalse();
    }

    [Fact]
    public void Builder_Values_ReturnsAllValues()
    {
        var builder = TranslatedText.CreateBuilder();
        var fr = LangCode.FromCode("fr");

        builder.Count.ShouldBe(0);

        builder.Add(LangCode.En, "hello");
        builder.Values.ShouldBe(["hello"], ignoreOrder: true);

        builder.Add(fr, "bonjour");
        builder.Values.ShouldBe(["hello", "bonjour"], ignoreOrder: true);

        builder.Add(LangCode.Nb, "hei");
        builder.Values.ShouldBe(["hello", "bonjour", "hei"], ignoreOrder: true);

        builder.Remove(LangCode.En);
        builder.Values.ShouldBe(["bonjour", "hei"], ignoreOrder: true);

        builder.Remove(fr);
        builder.Values.ShouldBe(["hei"], ignoreOrder: true);

        builder.Remove(LangCode.Nb);
        builder.Values.ShouldBeEmpty();
    }

    [Fact]
    public void Builder_Indexer_ThrowsOnMissingKey()
    {
        var builder = TranslatedText.CreateBuilder();

        Assert.Throws<KeyNotFoundException>(() => builder[LangCode.En]);
    }

    [Fact]
    public void Builder_Indexer_CanOverwrite()
    {
        var builder = TranslatedText.CreateBuilder();
        var fr = LangCode.FromCode("fr");

        builder[LangCode.En] = "1";
        builder[LangCode.En].ShouldBe("1");

        builder[LangCode.En] = "2";
        builder[LangCode.En].ShouldBe("2");

        builder[fr] = "3";
        builder[fr].ShouldBe("3");

        builder[fr] = "4";
        builder[fr].ShouldBe("4");
    }

    [Fact]
    public void JsonRoundtrip()
    {
        var builder = TranslatedText.CreateBuilder();

        builder.Add(LangCode.En, "hello");
        builder.Add(LangCode.Nb, "hei");
        builder.Add(LangCode.Nn, "hei");
        builder.Add(LangCode.FromCode("fr"), "bonjour");

        var text = builder.ToImmutable();

        var json = JsonSerializer.SerializeToDocument(text);
        json.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);

        var deserialized = JsonSerializer.Deserialize<TranslatedText>(json);

        deserialized.ShouldBe(text);
        (deserialized == text).ShouldBeTrue();
        (deserialized != text).ShouldBeFalse();
    }
}
