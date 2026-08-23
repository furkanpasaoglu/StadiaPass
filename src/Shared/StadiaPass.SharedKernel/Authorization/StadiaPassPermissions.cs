using System.Collections.Frozen;
using System.Reflection;

namespace StadiaPass.SharedKernel.Authorization;

/// <summary>
/// Single source of truth for every permission in the system. Nothing outside this class may invent a
/// permission string, and no role name is ever referenced in code - roles only exist in the identity
/// provider and are mapped onto these permissions at runtime.
/// </summary>
public static class StadiaPassPermissions
{
    public const string GroupName = "StadiaPass";

    public static IReadOnlySet<string> All { get; } = Discover();

    public static bool IsDefined(string permission) => All.Contains(permission);

    private static FrozenSet<string> Discover() =>
        typeof(StadiaPassPermissions)
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(group => group.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Back-office operations: defining venues and opening matches for sale.</summary>
    public static class Venues
    {
        public const string Default = GroupName + ".Venues";
        public const string View = Default + ".View";
        public const string Create = Default + ".Create";
    }

    public static class Matches
    {
        public const string Default = GroupName + ".Matches";
        public const string View = Default + ".View";
        public const string Create = Default + ".Create";
        public const string Postpone = Default + ".Postpone";
    }

    /// <summary>Customer facing seat selection and purchase.</summary>
    public static class Tickets
    {
        public const string Default = GroupName + ".Tickets";
        public const string View = Default + ".View";
        public const string Reserve = Default + ".Reserve";
        public const string Purchase = Default + ".Purchase";
        public const string Cancel = Default + ".Cancel";
    }
}
