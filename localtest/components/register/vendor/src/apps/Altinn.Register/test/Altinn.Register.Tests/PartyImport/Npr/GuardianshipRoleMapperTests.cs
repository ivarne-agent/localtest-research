using System.Diagnostics;
using System.Text;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Integrations.Npr;
using Xunit.Sdk;

namespace Altinn.Register.Tests.PartyImport.Npr;

/// <summary>Tests for mapping of guardianship roles.</summary>
public partial class GuardianshipRoleMapperTests
{
    [Theory]
    [MemberData(nameof(Guardianships))]
    public static void MatchesNprAreaAndTask(GuardianshipMetadata meta)
    {
        GuardianshipRoleMapper.TryFindRoleByNprValues(meta.NprArea, meta.NprTask, out var role).ShouldBeTrue();
        role.ShouldBe(meta.Role);
    }

    [Theory]
    [MemberData(nameof(Guardianships))]
    public static void MatchesNprAreaAndTaskUtf8(GuardianshipMetadata meta)
    {
        var area = Encoding.UTF8.GetBytes(meta.NprArea);
        var task = Encoding.UTF8.GetBytes(meta.NprTask);
        GuardianshipRoleMapper.TryFindRoleByNprValues(area, task, out var role).ShouldBeTrue();
        role.ShouldBe(meta.Role);
    }

    [Theory]
    [MemberData(nameof(ExpiredGuardianships))]
    public static void MatchesNprAreaTask_Expired(ExpiredGuardianshipMetadata meta)
    {
        GuardianshipRoleMapper.TryFindRoleByNprValues(meta.NprArea, meta.NprTask, out var role).ShouldBeTrue();
        role.ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(ExpiredGuardianships))]
    public static void MatchesNprAreaAndTaskUtf8_Expired(ExpiredGuardianshipMetadata meta)
    {
        var area = Encoding.UTF8.GetBytes(meta.NprArea);
        var task = Encoding.UTF8.GetBytes(meta.NprTask);
        GuardianshipRoleMapper.TryFindRoleByNprValues(area, task, out var role).ShouldBeTrue();
        role.ShouldBeNull();
    }

    [Fact]
    public static void DoesNotMatchInvalidNprAreaAndTask()
    {
        GuardianshipRoleMapper.TryFindRoleByNprValues("notExisting", "noTask", out _).ShouldBeFalse();
    }

    [Fact]
    public static void DoesNotMatchInvalidNprAreaAndTaskUtf8()
    {
        GuardianshipRoleMapper.TryFindRoleByNprValues("notExisting"u8, "noTask"u8, out _).ShouldBeFalse();
    }

    public static TheoryData<GuardianshipMetadata> Guardianships
        => GetGuardianshipRoles();

    public static TheoryData<ExpiredGuardianshipMetadata> ExpiredGuardianships
        => GetExpiredGuardianshipRoles();

    private static partial TheoryData<GuardianshipMetadata> GetGuardianshipRoles();

    private static partial TheoryData<ExpiredGuardianshipMetadata> GetExpiredGuardianshipRoles();

    [DebuggerDisplay("Role = {Identifier}")]
    public sealed record class GuardianshipMetadata
        : IXunitSerializable
    {
        private string _identifier = null!;
        private ExternalRoleReference _role = null!;
        private string _nprArea = null!;
        private string _nprTask = null!;

        public required string Identifier
        {
            get => _identifier;
            init => _identifier = value;
        }

        public required ExternalRoleReference Role
        {
            get => _role;
            init => _role = value;
        }

        public required string NprArea
        {
            get => _nprArea;
            init => _nprArea = value;
        }

        public required string NprTask
        {
            get => _nprTask;
            init => _nprTask = value;
        }

        public override string ToString()
            => Identifier;

        void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
        {
            _identifier = info.GetValue<string>(nameof(Identifier))!;
            _role = new(Contracts.ExternalRoleSource.CivilRightsAuthority, _identifier);
            _nprArea = info.GetValue<string>(nameof(NprArea))!;
            _nprTask = info.GetValue<string>(nameof(NprTask))!;
        }

        void IXunitSerializable.Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(Identifier), Identifier, typeof(string));
            info.AddValue(nameof(NprArea), NprArea, typeof(string));
            info.AddValue(nameof(NprTask), NprTask, typeof(string));
        }
    }

    [DebuggerDisplay("Npr = \"{NprArea,nq}/{NprTask,nq}\"")]
    public sealed record class ExpiredGuardianshipMetadata
        : IXunitSerializable
    {
        private string _nprArea = null!;
        private string _nprTask = null!;

        public required string NprArea
        {
            get => _nprArea;
            init => _nprArea = value;
        }

        public required string NprTask
        {
            get => _nprTask;
            init => _nprTask = value;
        }

        public override string ToString()
            => $"{NprArea}/{NprTask}";

        void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
        {
            _nprArea = info.GetValue<string>(nameof(NprArea))!;
            _nprTask = info.GetValue<string>(nameof(NprTask))!;
        }

        void IXunitSerializable.Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(NprArea), NprArea, typeof(string));
            info.AddValue(nameof(NprTask), NprTask, typeof(string));
        }
    }
}
