#!/usr/bin/env python3
"""Regenerate testData.json from the old localtest's built-in test data.

Usage: python3 -I generate-testdata.py [OLD_TESTDATA_DIR] [OUTPUT]

OLD_TESTDATA_DIR defaults to the altinn-studio checkout next to this repository
(src/Runtime/localtest/testdata). The conversion is deterministic: running it twice gives the same file.

What it does (see README.md in this folder for the result):
- persons from Register/Party (person and self-identified parties) + Register/Person + Profile/User,
- organisations from Register/Party + Register/Org, sub-units from authorization/partylist childParties
  plus the explicit SUB_UNITS below,
- partyRoles from authorization/roles (codes kept verbatim; Register decides which are CCR roles),
  plus empty role lists for parties a user may act for without roles (authorization/partylist),
- customClaims from authorization/claims, systems from authorization/systems,
- every fødselsnummer and organisation number is replaced by a valid mod-11 synthetic number,
- schema v3: mobile numbers in +47 form, the reserved flag and organisations' official notification addresses
  (the tables below).
"""
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_SRC = os.path.normpath(os.path.join(HERE, "../../../../../altinn-studio/src/Runtime/localtest/testdata"))

# Sub-units the old data only implied (separate parties with the main unit's name prefix).
SUB_UNITS = {500800: 500600, 500801: 500600, 500802: 500600}

# Organisations in Register/Org without a party. Kari Consulting is kept with a new party id; the second
# "EAS Health Consulting Svolvær" record (910423185) is a duplicate of party 500802 and is dropped.
EXTRA_ORG_PARTY_IDS = {"900000001": 500900}

# Unit types the old data had wrong or left out; the production Register always has one (Access Management's import
# rejects an organisation without it).
UNIT_TYPES = {500004: "ENK", 500005: "STAT", 500800: "BEDR", 500801: "BEDR", 500802: "BEDR"}

# Self-identified users are ID-porten e-mail registrations: no name, no username, identified by a unique e-mail.
SELF_IDENTIFIED_EMAILS = {1004: "selvregistrert.bruker@example.com"}

# Schema v3: persons reserved in KRR (the contact and reservation register). Rik Forelder (1003) has no dependency
# in the Cypress suite (its person-lookup tests intercept the lookup) and holds no role for an organisation.
RESERVED_USER_IDS = {1003}

# Schema v3: official notification addresses (Profile's organizations/notificationaddresses/lookup) for the
# organisations the Notifications smoke test and the Cypress suite use; their sub-units fall back to them.
NOTIFICATION_ADDRESSES = {
    500000: [{"email": "varsling@ddgfitness.no"}, {"mobileNumber": "+4799500000"}],
    500006: [{"email": "varsling@digdir.no"}, {"mobileNumber": "+4799500006"}],
}

SCHEMA = "../../../testdata/test-users.schema.v3.json"

# Access packages (schema v2), which the old data did not have, keyed by user id, then by the party id they are held for.
ACCESS_PACKAGES = {1001: {"500000": ["regnskapsforer-med-signeringsrettighet", "lonn"]}}

FNR_K1 = [3, 7, 6, 1, 8, 9, 4, 5, 2]
FNR_K2 = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2]
ORG_W = [3, 2, 7, 6, 5, 4, 3, 2]

def load(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def ci(d, key):
    """Case-insensitive key lookup (the old files mix PascalCase and camelCase)."""
    for k, v in d.items():
        if k.lower() == key.lower():
            return v
    return None


def mod11(digits, weights):
    r = (11 - sum(int(d) * w for d, w in zip(digits, weights)) % 11) % 11
    return None if r == 10 else r


def fnr_valid(s):
    if len(s) != 11 or not s.isdigit():
        return False
    k1 = mod11(s[:9], FNR_K1)
    k2 = mod11(s[:9] + str(k1), FNR_K2) if k1 is not None else None
    return k1 is not None and k2 is not None and s[9:] == f"{k1}{k2}"


def org_valid(s):
    return len(s) == 9 and s.isdigit() and mod11(s[:8], ORG_W) == int(s[8])


def individual_range(year, individual):
    """Individual-number range that keeps the same century for a two-digit year."""
    if individual < 500:
        return range(0, 500)
    if individual >= 900 and year >= 40:
        return range(900, 1000)
    if individual < 750 and year >= 54:
        return range(500, 750)
    return range(500, 1000) if year < 40 else range(individual, individual + 1)


def synthetic_fnr(old):
    """Tenor-style synthetic number: month + 80, same birth date, nearest valid individual number."""
    day, month, year, individual = old[:2], int(old[2:4]), int(old[4:6]), int(old[6:9])
    if month < 80:
        month = month % 40 + 80 if month > 40 else month + 80
    prefix = f"{day}{month:02d}{year:02d}"
    rng = individual_range(year, individual)
    ordered = sorted(rng, key=lambda i: (abs(i - individual), i))
    for ind in ordered:
        base = f"{prefix}{ind:03d}"
        k1 = mod11(base, FNR_K1)
        if k1 is None:
            continue
        k2 = mod11(base + str(k1), FNR_K2)
        if k2 is None:
            continue
        return f"{base}{k1}{k2}"
    raise ValueError(f"no valid synthetic number for {old}")


def synthetic_orgnr(old, taken, reserved):
    """Nearest valid organisation number that is neither used nor another organisation's old number."""
    if org_valid(old) and old not in taken:
        return old
    n = int(old)
    for delta in range(1, 1_000_000):
        for cand in (n + delta, n - delta):
            s = f"{cand:09d}"
            if 0 <= cand < 1_000_000_000 and org_valid(s) and s not in taken and s not in reserved:
                return s
    raise ValueError(f"no valid organisation number near {old}")


def international(number):
    """Schema v3: mobile numbers in full international form. A number without a country code is Norwegian."""
    if not number or number.startswith("+"):
        return number
    return "+47" + number


def drop_empty(d):
    return {k: v for k, v in d.items() if v not in (None, "", [], {})}


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SRC
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "testData.json")

    parties = {int(ci(p, "partyId")): p for p in map(load, glob.glob(f"{src}/Register/Party/*.json"))}
    if not parties:
        sys.exit(f"no Register/Party/*.json under {src}; pass the old localtest testdata folder")
    orgs_by_number = {ci(o, "orgNumber"): o for o in map(load, glob.glob(f"{src}/Register/Org/*.json"))}
    # Register/Org/910423495.json carries a different orgNumber inside; index by file name too.
    for f in glob.glob(f"{src}/Register/Org/*.json"):
        orgs_by_number.setdefault(os.path.basename(f)[:-5], load(f))
    persons_by_ssn = {ci(p, "ssn"): p for p in map(load, glob.glob(f"{src}/Register/Person/*.json"))}
    profiles = {int(ci(u, "userId")): u for u in map(load, glob.glob(f"{src}/Profile/User/*.json"))}
    partylists = {int(os.path.basename(f)[:-5]): load(f) for f in glob.glob(f"{src}/authorization/partylist/*.json")}
    roles = {}
    for f in glob.glob(f"{src}/authorization/roles/User_*/party_*/roles.json"):
        user = int(f.split("User_")[1].split("/")[0])
        party = int(f.split("party_")[1].split("/")[0])
        roles.setdefault(user, {})[party] = [{"type": ci(r, "type") or "altinn", "value": ci(r, "value")} for r in load(f)]
    claims = {int(os.path.basename(f)[:-5]): load(f) for f in glob.glob(f"{src}/authorization/claims/*.json")}

    parent_of = dict(SUB_UNITS)
    for plist in partylists.values():
        for p in plist:
            for child in ci(p, "childParties") or []:
                parent_of[int(ci(child, "partyId"))] = int(ci(p, "partyId"))

    mapping = {"persons": [], "orgs": []}

    # Organisations: valid numbers first, so systems and persons can refer to the new ones.
    org_numbers = {}
    taken = set()
    org_parties = sorted((pid, p) for pid, p in parties.items() if ci(p, "partyTypeName") == 2)
    org_parties += [(pid, {"orgNumber": num, "partyUuid": None}) for num, pid in EXTRA_ORG_PARTY_IDS.items()]
    reserved = {ci(p, "orgNumber") for _, p in org_parties}
    for _, p in sorted(org_parties, key=lambda t: t[0]):
        old = ci(p, "orgNumber")
        org_numbers[old] = synthetic_orgnr(old, taken, reserved)
        taken.add(org_numbers[old])

    orgs = []
    for pid, p in sorted(org_parties, key=lambda t: t[0]):
        old = ci(p, "orgNumber")
        details = orgs_by_number.get(old, {})
        name = ci(p, "name") or ci(details, "name")
        orgs.append(drop_empty({
            "partyId": pid,
            "partyUuid": ci(p, "partyUuid") or f"00000000-0000-0000-0000-{pid:012d}",
            "orgNumber": org_numbers[old],
            "parentPartyId": parent_of.get(pid),
            "name": name,
            "unitType": UNIT_TYPES.get(pid) or ci(p, "unitType") or ci(details, "unitType"),
            "unitStatus": ci(details, "unitStatus") or "N",
            "businessAddress": ci(details, "businessAddress"),
            "businessPostalCode": ci(details, "businessPostalCode"),
            "businessPostalCity": ci(details, "businessPostalCity"),
            "mailingAddress": ci(details, "mailingAddress"),
            "mailingPostalCode": ci(details, "mailingPostalCode"),
            "mailingPostalCity": ci(details, "mailingPostalCity"),
            "eMailAddress": ci(details, "eMailAddress"),
            "internetAddress": ci(details, "internetAddress"),
            "telephoneNumber": ci(details, "telephoneNumber"),
            "mobileNumber": international(ci(details, "mobileNumber")),
            "faxNumber": ci(details, "faxNumber"),
            "notificationAddresses": NOTIFICATION_ADDRESSES.get(pid),
        }))
        mapping["orgs"].append((pid, name, old, org_numbers[old]))

    persons = []
    for user_id, profile in sorted(profiles.items()):
        pid = int(ci(profile, "partyId"))
        party = parties[pid]
        old_ssn = ci(party, "ssn") or ""
        details = persons_by_ssn.get(old_ssn, {})
        name = ci(party, "name")
        first, _, last = name.partition(" ")
        new_ssn = synthetic_fnr(old_ssn) if old_ssn else ""
        prefs = ci(profile, "profileSettingPreference") or {}

        party_roles = {}
        for for_party, rs in sorted(roles.get(user_id, {}).items()):
            party_roles[str(for_party)] = rs
        # Parties the user may act for without any role in the old data (authorization/partylist).
        for p in partylists.get(user_id, []):
            ppid = int(ci(p, "partyId"))
            if ppid != pid:
                party_roles.setdefault(str(ppid), [])

        self_identified = not old_ssn
        persons.append({"ssn": new_ssn} | drop_empty({
            "userId": user_id,
            "partyId": pid,
            "partyUuid": ci(party, "partyUuid"),
            "firstName": None if self_identified else ci(details, "firstName") or first,
            "middleName": None if self_identified else ci(details, "middleName"),
            "lastName": None if self_identified else ci(details, "lastName") or last,
            "userName": None if self_identified else ci(profile, "userName"),
            "email": SELF_IDENTIFIED_EMAILS.get(user_id) or ci(profile, "email"),
            "language": ci(prefs, "language"),
            "preSelectedPartyId": ci(prefs, "preSelectedPartyId"),
            "doNotPromptForParty": ci(prefs, "doNotPromptForParty"),
            "telephoneNumber": ci(details, "telephoneNumber"),
            "mobileNumber": international(ci(details, "mobileNumber") or ci(profile, "phoneNumber")),
            "reserved": True if user_id in RESERVED_USER_IDS else None,
            "addressStreetName": ci(details, "addressStreetName"),
            "addressHouseNumber": ci(details, "addressHouseNumber"),
            "addressHouseLetter": ci(details, "addressHouseLetter"),
            "addressPostalCode": ci(details, "addressPostalCode"),
            "addressCity": ci(details, "addressCity"),
            "addressMunicipalNumber": ci(details, "addressMunicipalNumber"),
            "addressMunicipalName": ci(details, "addressMunicipalName"),
            "mailingAddress": ci(details, "mailingAddress"),
            "mailingPostalCode": ci(details, "mailingPostalCode"),
            "mailingPostalCity": ci(details, "mailingPostalCity"),
            "partyRoles": party_roles,
            "accessPackages": ACCESS_PACKAGES.get(user_id),
            "customClaims": claims.get(user_id),
        }))
        # doNotPromptForParty=false is meaningful; drop_empty keeps False (only None/""/[]/{} are dropped).
        mapping["persons"].append((user_id, pid, name, old_ssn, new_ssn))

    systems = []
    for system_file in sorted(glob.glob(f"{src}/authorization/systems/*/system.json")):
        system = load(system_file)
        users = []
        for su_file in sorted(glob.glob(os.path.join(os.path.dirname(system_file), "systemusers/*.json"))):
            su = load(su_file)
            users.append({"id": ci(su, "id"), "orgNumber": org_numbers[ci(su, "orgNumber")], "actions": ci(su, "actions")})
        systems.append({"id": ci(system, "id"), "name": ci(system, "name"), "systemUsers": users})

    for _, _, _, _, new in mapping["persons"]:
        assert new == "" or fnr_valid(new), new
    for o in orgs:
        assert org_valid(o["orgNumber"]), o

    data = {
        "$schema": SCHEMA,
        "persons": persons,
        "orgs": orgs,
        "systems": systems,
    }
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print("| userId | partyId | name | old fødselsnummer | new fødselsnummer |")
    print("| --- | --- | --- | --- | --- |")
    for user_id, pid, name, old, new in mapping["persons"]:
        print(f"| {user_id} | {pid} | {name} | {old or '(self-identified)'} | {new or '-'} |")
    print()
    print("| partyId | name | old org number | new org number |")
    print("| --- | --- | --- | --- |")
    for pid, name, old, new in mapping["orgs"]:
        print(f"| {pid} | {name} | {old} | {new}{'' if old != new else ' (unchanged)'} |")


if __name__ == "__main__":
    main()
