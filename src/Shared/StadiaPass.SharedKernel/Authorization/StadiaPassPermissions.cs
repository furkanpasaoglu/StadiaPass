using System.Collections.Frozen;
using System.Reflection;

namespace StadiaPass.SharedKernel.Authorization;

/// <summary>
/// Single source of truth for every permission in the system. Nothing outside this class may invent a
/// permission string, and no role name is ever referenced in code - roles live only in the identity
/// provider and are mapped onto these permissions at runtime.
/// </summary>
public static class StadiaPassPermissions
{
    public const string GroupName = "StadiaPass";

    public static IReadOnlySet<string> All { get; } = Discover();

    /// <summary>
    /// The permission catalogue, grouped by module. This is what the role editor renders as a checklist,
    /// so adding a constant below is enough to make it appear in the portal.
    /// </summary>
    public static IReadOnlyList<PermissionGroup> Groups { get; } = BuildGroups();

    public static bool IsDefined(string permission) => All.Contains(permission);

    /// <summary>True when the name is a permission rather than a business role such as "BoxOffice".</summary>
    public static bool IsPermissionRole(string roleName) => All.Contains(roleName);

    private static FrozenSet<string> Discover() =>
        typeof(StadiaPassPermissions)
            .GetNestedTypes(BindingFlags.Public)
            .Where(group => group != typeof(PermissionGroup))
            .SelectMany(group => group.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToFrozenSet(StringComparer.Ordinal);

    private static List<PermissionGroup> BuildGroups() =>
    [
        .. All
            .Select(permission => permission.Split('.'))
            .Where(segments => segments.Length is 3)
            .GroupBy(segments => segments[1], StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new PermissionGroup(
                group.Key,
                [.. group.Select(segments => string.Join('.', segments)).Order(StringComparer.Ordinal)]))
    ];

    public static class Venues
    {
        public const string Default = GroupName + ".Venues";
        public const string View = Default + ".View";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Sport categories a match can be opened for.</summary>
    public static class Categories
    {
        public const string Default = GroupName + ".Categories";
        public const string View = Default + ".View";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    public static class Matches
    {
        public const string Default = GroupName + ".Matches";
        public const string View = Default + ".View";
        public const string Create = Default + ".Create";

        /// <summary>
        /// Call a fixture off. Its own permission rather than Create's, because this one spends money: every
        /// ticket sold for the fixture is refunded, and whoever may open a match for sale is not
        /// automatically whoever may hand back a stadium's worth of takings.
        /// </summary>
        public const string Cancel = Default + ".Cancel";
    }

    public static class Tickets
    {
        public const string Default = GroupName + ".Tickets";
        public const string View = Default + ".View";

        /// <summary>Read a ticket that belongs to somebody else - the box office, not the customer.</summary>
        public const string ViewAll = Default + ".ViewAll";

        public const string Reserve = Default + ".Reserve";
        public const string Purchase = Default + ".Purchase";
        public const string Cancel = Default + ".Cancel";
    }

    /// <summary>Identity portal: business roles and the permissions bound to them.</summary>
    public static class Roles
    {
        public const string Default = GroupName + ".Roles";
        public const string View = Default + ".View";
        public const string Create = Default + ".Create";
        public const string Manage = Default + ".Manage";
    }

    public static class Users
    {
        public const string Default = GroupName + ".Users";
        public const string View = Default + ".View";
        public const string Create = Default + ".Create";
        public const string Manage = Default + ".Manage";
    }
}

public sealed record PermissionGroup(string Name, IReadOnlyList<string> Permissions);
