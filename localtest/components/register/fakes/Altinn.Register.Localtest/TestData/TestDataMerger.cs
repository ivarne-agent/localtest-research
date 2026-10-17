namespace Altinn.Register.Localtest.TestData;

/// <summary>
/// Merges test data sets the way the old localtest did (<c>TestDataMerger</c>): entries are added when their
/// identifiers are new, identical duplicates are accepted, and an entry that reuses an identifier with a different
/// definition is a conflict. Conflicting entries are skipped (the first definition wins) and reported.
/// </summary>
public static class TestDataMerger
{
    public static TestDataFile Merge(IEnumerable<(string Source, TestDataFile Data)> sources, List<string> conflicts)
    {
        var result = new TestDataFile();
        var persons = new Dictionary<string, (TestPerson Person, string Source)>();
        var orgs = new Dictionary<string, (TestOrg Org, string Source)>();
        var partyIds = new Dictionary<int, string>();
        var systems = new Dictionary<string, (TestSystem System, string Source)>(StringComparer.Ordinal);

        foreach (var (source, data) in sources)
        {
            foreach (var person in data.Persons)
            {
                string[] keys = [$"userId {person.UserId}", $"partyId {person.PartyId}", .. string.IsNullOrEmpty(person.Ssn) ? Array.Empty<string>() : [$"ssn {person.Ssn}"]];
                if (Existing(persons, keys) is { } existing)
                {
                    if (TestDataFile.Canonical(existing.Item1) != TestDataFile.Canonical(person))
                    {
                        conflicts.Add($"{source}: person {Describe(person)} conflicts with the definition from {existing.Source}");
                    }

                    continue;
                }

                if (partyIds.TryGetValue(person.PartyId, out var partyOwner))
                {
                    conflicts.Add($"{source}: person {Describe(person)} reuses partyId {person.PartyId} from {partyOwner}");
                    continue;
                }

                foreach (var key in keys)
                {
                    persons[key] = (person, source);
                }

                partyIds[person.PartyId] = $"{source} ({Describe(person)})";
                result.Persons.Add(person);
            }

            foreach (var org in data.Orgs)
            {
                string[] keys = [$"partyId {org.PartyId}", $"orgNumber {org.OrgNumber}"];
                if (Existing(orgs, keys) is { } existing)
                {
                    if (TestDataFile.Canonical(existing.Item1) != TestDataFile.Canonical(org))
                    {
                        conflicts.Add($"{source}: organisation {org.Name} ({org.OrgNumber}, partyId {org.PartyId}) conflicts with the definition from {existing.Source}");
                    }

                    continue;
                }

                if (partyIds.TryGetValue(org.PartyId, out var partyOwner))
                {
                    conflicts.Add($"{source}: organisation {org.Name} ({org.OrgNumber}) reuses partyId {org.PartyId} from {partyOwner}");
                    continue;
                }

                foreach (var key in keys)
                {
                    orgs[key] = (org, source);
                }

                partyIds[org.PartyId] = $"{source} ({org.Name})";
                result.Orgs.Add(org);
            }

            foreach (var system in data.Systems)
            {
                if (systems.TryGetValue(system.Id, out var existing))
                {
                    if (TestDataFile.Canonical(existing.System) != TestDataFile.Canonical(system))
                    {
                        conflicts.Add($"{source}: system {system.Id} conflicts with the definition from {existing.Source}");
                    }

                    continue;
                }

                systems[system.Id] = (system, source);
                result.Systems.Add(system);
            }
        }

        return result;
    }

    private static (T, string Source)? Existing<T>(Dictionary<string, (T, string Source)> index, string[] keys)
    {
        foreach (var key in keys)
        {
            if (index.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        return null;
    }

    private static string Describe(TestPerson person)
        => $"{string.Join(' ', new[] { person.FirstName, person.MiddleName, person.LastName }.Where(static s => !string.IsNullOrWhiteSpace(s)))} (userId {person.UserId}, partyId {person.PartyId})";
}
