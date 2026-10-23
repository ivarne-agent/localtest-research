namespace Altinn.App.Aspire;

/// <summary>
/// The localtest platform in minimal mode: the manifest's <c>minimal</c> group (Postgres, Storage, the workflow engine
/// and the localtest container), the localtest container emulating the rest of the default group, as session containers
/// on tmpfs. It has no optional groups: no method that enables one takes its builder.
/// </summary>
public sealed class AltinnLocaltestMinimalResource : AltinnLocaltestResourceBase
{
    internal AltinnLocaltestMinimalResource(string name, LocaltestStack stack)
        : base(name, stack) { }

    /// <summary>
    /// The services the localtest container emulates, in start order: every HTTP container of the default group that
    /// minimal mode does not start (register, profile, authentication, ...). Each is announced to every container as
    /// <c>services__&lt;name&gt;__http__0=http://localtest:8080</c>.
    /// </summary>
    public IReadOnlyList<string> Emulated => Manifest.StackSpec.Emulated(Stack.Manifest);
}
