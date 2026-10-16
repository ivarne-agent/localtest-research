using System.Collections;
using Altinn.Authorization.ModelUtils.EnumUtils;
using Altinn.Register.Core.Parties.Records;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Models;

/// <summary>
/// A set of <see cref="PartyRecordType"/>.
/// </summary>
internal readonly struct PartyTypesSet
    : IReadOnlySet<PartyRecordType>
{
    private static readonly FlagsEnumModel<PartyTypes> _model = FlagsEnumModel.Create<PartyTypes>();
    private static readonly PartyTypes _mask = CreateMask(_model);

    private static PartyTypes CreateMask(FlagsEnumModel<PartyTypes> model)
    {
        var mask = PartyTypes.None;
        foreach (var item in model.Items)
        {
            mask |= item.Value;
        }

        return mask;
    }

    private readonly PartyTypes _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartyTypesSet"/> struct.
    /// </summary>
    /// <param name="value">The party type value to assign to this instance.</param>
    public PartyTypesSet(PartyTypes value)
    {
        _value = value & _mask;
    }

    /// <inheritdoc/>
    public readonly int Count => _value.NumBitsSet();

    /// <inheritdoc/>
    public readonly bool Contains(PartyRecordType item)
        => _value.HasFlag(ToBitValue(item));

    /// <inheritdoc/>
    public readonly IEnumerator<PartyRecordType> GetEnumerator()
    {
        if (_value is PartyTypes.None)
        {
            return Enumerable.Empty<PartyRecordType>().GetEnumerator();
        }

        return MakeEnumerator(_value);

        static IEnumerator<PartyRecordType> MakeEnumerator(PartyTypes value)
        {
            foreach (var item in _model.Items)
            {
                if (value.HasFlag(item.Value))
                {
                    yield return FromBitValue(item.Value);
                }
            }
        }
    }

    /// <inheritdoc/>
    readonly IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    /// <inheritdoc/>
    readonly bool IReadOnlySet<PartyRecordType>.IsProperSubsetOf(IEnumerable<PartyRecordType> other)
        => ThrowHelper.ThrowNotSupportedException<bool>();

    /// <inheritdoc/>
    readonly bool IReadOnlySet<PartyRecordType>.IsProperSupersetOf(IEnumerable<PartyRecordType> other)
        => ThrowHelper.ThrowNotSupportedException<bool>();

    /// <inheritdoc/>
    readonly bool IReadOnlySet<PartyRecordType>.IsSubsetOf(IEnumerable<PartyRecordType> other)
        => ThrowHelper.ThrowNotSupportedException<bool>();

    /// <inheritdoc/>
    readonly bool IReadOnlySet<PartyRecordType>.IsSupersetOf(IEnumerable<PartyRecordType> other)
        => ThrowHelper.ThrowNotSupportedException<bool>();

    /// <inheritdoc/>
    readonly bool IReadOnlySet<PartyRecordType>.Overlaps(IEnumerable<PartyRecordType> other)
        => ThrowHelper.ThrowNotSupportedException<bool>();

    /// <inheritdoc/>
    readonly bool IReadOnlySet<PartyRecordType>.SetEquals(IEnumerable<PartyRecordType> other)
        => ThrowHelper.ThrowNotSupportedException<bool>();

    private static PartyTypes ToBitValue(PartyRecordType value)
        => value switch
        {
            PartyRecordType.Person => PartyTypes.Person,
            PartyRecordType.Organization => PartyTypes.Organization,
            PartyRecordType.SelfIdentifiedUser => PartyTypes.SelfIdentifiedUser,
            PartyRecordType.SystemUser => PartyTypes.SystemUser,
            PartyRecordType.EnterpriseUser => PartyTypes.EnterpriseUser,
            _ => ThrowHelper.ThrowArgumentOutOfRangeException<PartyTypes>(nameof(value), value, $"Invalid {nameof(PartyRecordType)}"),
        };

    private static PartyRecordType FromBitValue(PartyTypes value)
        => value switch
        {
            PartyTypes.Person => PartyRecordType.Person,
            PartyTypes.Organization => PartyRecordType.Organization,
            PartyTypes.SelfIdentifiedUser => PartyRecordType.SelfIdentifiedUser,
            PartyTypes.SystemUser => PartyRecordType.SystemUser,
            PartyTypes.EnterpriseUser => PartyRecordType.EnterpriseUser,
            _ => ThrowHelper.ThrowArgumentOutOfRangeException<PartyRecordType>(nameof(value), value, $"Invalid {nameof(PartyTypes)}"),
        };
}
