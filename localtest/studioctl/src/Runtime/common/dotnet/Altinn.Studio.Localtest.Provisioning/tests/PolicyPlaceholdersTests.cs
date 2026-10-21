namespace Altinn.Studio.Localtest.Provisioning.Tests;

public sealed class PolicyPlaceholdersTests
{
    [Fact]
    public void ReplacesBothCasesOfOrgAndApp()
    {
        const string policy =
            "<xacml:Policy PolicyId=\"urn:altinn:example:policyid:1\"><Rule RuleId=\"urn:altinn:example:ruleid:[ORG]:[APP]:1\">"
            + "<AttributeValue>[org]</AttributeValue><AttributeValue>[app]</AttributeValue></Rule></xacml:Policy>";

        var resolved = PolicyPlaceholders.Replace(policy, AppId.Parse("ttd/frontend-test"));

        Assert.Equal(
            "<xacml:Policy PolicyId=\"urn:altinn:example:policyid:1\"><Rule RuleId=\"urn:altinn:example:ruleid:ttd:frontend-test:1\">"
                + "<AttributeValue>ttd</AttributeValue><AttributeValue>frontend-test</AttributeValue></Rule></xacml:Policy>",
            resolved
        );
    }

    [Fact]
    public void LeavesOtherBracketsAlone()
    {
        const string policy = "<a>[Org]</a><b>[ORGANISATION]</b><c>[apps]</c>";

        Assert.Equal(policy, PolicyPlaceholders.Replace(policy, AppId.Parse("ttd/frontend-test")));
    }
}
