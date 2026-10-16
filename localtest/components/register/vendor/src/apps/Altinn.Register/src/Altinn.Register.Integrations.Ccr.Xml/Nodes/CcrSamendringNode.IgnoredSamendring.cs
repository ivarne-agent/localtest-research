using System.Xml;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;

internal abstract partial record CcrSamendringNode
{
    private sealed record IgnoredSamendring
        : CcrSamendringNode
    {
        private static readonly IgnoredSamendring Instance = new();

        public static new IgnoredSamendring ParseNode(XmlReader reader)
        {
            reader.Skip(); // Consume the <samendringer> element
            return Instance;
        }

        internal override void Apply(
            string orgform,
            IList<CcrRoleAssignment> additions,
            IList<CcrRoleAssignment> removals,
            IExternalRoleDefinitionLookup roleLookup,
            ILocationLookup locationLookup)
        {
        }
    }
}
