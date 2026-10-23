using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Memento.Core.Serialization;

/// <summary>
/// Finds, and on request repairs, a list, an object or a list entry that JSON left <c>null</c> although its declared
/// type says it cannot be. System.Text.Json on .NET 8 does not honour nullable annotations: <c>"items": null</c>,
/// <c>[null]</c>, a missing constructor parameter or a missing <c>init</c> member all read as <c>null</c> into a
/// non-nullable member (security audit 2, SA2-08 and §9). Files are repaired on load; the bridge refuses parameters
/// that have one.
/// </summary>
/// <remarks>
/// Only Memento's own types are walked; strings and value types are left to each schema's own checks. Members whose
/// nullability is unknown (generic type parameters, code without nullable annotations) are not reported.
/// </remarks>
public static class NullMembers
{
    private const int MaxDepth = 64;

    private static readonly ConcurrentDictionary<Type, Member[]> Members = new();
    private static readonly object Gate = new();

    /// <summary>
    /// The JSON path of the first non-nullable list, object or list entry that is <c>null</c> in
    /// <paramref name="value"/> (for example <c>items[2].speakers</c>), or <see langword="null"/> when there is none.
    /// </summary>
    public static string? FindFirst(object? value)
    {
        var walker = new Walker(repair: false, limit: 1);
        walker.Start(value);
        return walker.Unrepaired.Count == 0 ? null : walker.Unrepaired[0];
    }

    /// <summary>The JSON paths of every such member, at most <paramref name="limit"/> of them.</summary>
    public static IReadOnlyList<string> FindAll(object? value, int limit = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var walker = new Walker(repair: false, limit);
        walker.Start(value);
        return walker.Unrepaired;
    }

    /// <summary>
    /// Repairs <paramref name="value"/> in place, as a loader does right after reading a file: a null member takes the
    /// value a new instance of its type starts with (its initializer), and a null entry is dropped from its list. What
    /// cannot be repaired (a member without an initializer, an entry of a list nested in a list) is returned in
    /// <see cref="NullRepair.Unrepaired"/>, so the loader can refuse the file by name.
    /// </summary>
    public static NullRepair Repair(object? value)
    {
        var walker = new Walker(repair: true, limit: 1000);
        walker.Start(value);
        return new NullRepair(walker.Repaired, walker.Unrepaired);
    }

    private static bool IsWalked(Type type) =>
        !type.IsValueType && type.Namespace is { } ns && (ns == "Memento" || ns.StartsWith("Memento.", StringComparison.Ordinal));

    private static Member[] MembersOf(Type type)
    {
        if (Members.TryGetValue(type, out var cached))
        {
            return cached;
        }

        // NullabilityInfoContext is not thread-safe; a type's members are read once.
        lock (Gate)
        {
            return Members.GetOrAdd(type, Read);
        }
    }

    private static Member[] Read(Type type)
    {
        var nullability = new NullabilityInfoContext();
        var members = new List<Member>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0
                || property.GetMethod is not { IsPublic: true }
                || property.PropertyType.IsValueType
                || property.PropertyType == typeof(string)
                || property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null
                || property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
            {
                continue;
            }

            var info = nullability.Create(property);
            var element = info.ElementType ?? (info.GenericTypeArguments.Length > 0 ? info.GenericTypeArguments[^1] : null);
            var elementNotNull = element is not null && !element.Type.IsValueType && element.ReadState == NullabilityState.NotNull;
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            members.Add(new Member(property, name, info.ReadState == NullabilityState.NotNull, elementNotNull, element?.Type));
        }

        return [.. members];
    }

    /// <summary>The value a new instance of <paramref name="owner"/> gives the member, when it has a parameterless constructor.</summary>
    private static object? InitialValue(Type owner, PropertyInfo property)
    {
        if (owner.GetConstructor(Type.EmptyTypes) is null)
        {
            return null;
        }

        return property.GetValue(Activator.CreateInstance(owner));
    }

    /// <summary>A copy of <paramref name="sequence"/> without its null entries, as the member's type, or null when that type cannot hold one.</summary>
    private static object? WithoutNulls(Type memberType, Type elementType, IEnumerable sequence)
    {
        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in sequence)
        {
            if (item is not null)
            {
                list.Add(item);
            }
        }

        if (memberType.IsArray)
        {
            var array = Array.CreateInstance(elementType, list.Count);
            list.CopyTo(array, 0);
            return array;
        }

        return memberType.IsAssignableFrom(listType) ? list : null;
    }

    /// <summary>A copy of <paramref name="dictionary"/> without its null values, as the member's type, or null when that type cannot hold one.</summary>
    private static object? WithoutNullValues(Type memberType, IDictionary dictionary)
    {
        var generic = dictionary.GetType().GetInterfaces().Append(dictionary.GetType())
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (generic is null)
        {
            return null;
        }

        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(generic.GetGenericArguments());
        var copy = (IDictionary)Activator.CreateInstance(dictionaryType)!;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Value is not null)
            {
                copy.Add(entry.Key, entry.Value);
            }
        }

        return memberType.IsAssignableFrom(dictionaryType) ? copy : null;
    }

    private sealed record Member(PropertyInfo Property, string JsonName, bool NotNull, bool ElementNotNull, Type? ElementType);

    private sealed class Walker(bool repair, int limit)
    {
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        public List<string> Repaired { get; } = [];

        public List<string> Unrepaired { get; } = [];

        private bool Full => Unrepaired.Count >= limit;

        public void Start(object? value)
        {
            if (value is not null)
            {
                Object(value, string.Empty, 0);
            }
        }

        private void Object(object value, string path, int depth)
        {
            if (depth > MaxDepth || !IsWalked(value.GetType()) || !_visited.Add(value))
            {
                return;
            }

            foreach (var member in MembersOf(value.GetType()))
            {
                if (Full)
                {
                    return;
                }

                var memberPath = path.Length == 0 ? member.JsonName : path + "." + member.JsonName;
                object? memberValue;
                try
                {
                    memberValue = member.Property.GetValue(value);
                }
                catch (TargetInvocationException)
                {
                    // A computed member that cannot be read is built from something that is missing.
                    Unrepaired.Add(memberPath);
                    continue;
                }

                if (memberValue is null)
                {
                    if (member.NotNull && !TrySet(value, member, InitialValue(value.GetType(), member.Property), memberPath))
                    {
                        Unrepaired.Add(memberPath);
                    }

                    continue;
                }

                if (repair && member.ElementNotNull && Cleaned(member, memberValue) is { } cleaned && TrySet(value, member, cleaned, memberPath + "[]"))
                {
                    memberValue = cleaned;
                }

                Value(memberValue, memberPath, member.ElementNotNull, depth + 1);
            }
        }

        /// <summary>The member's list or dictionary without its null entries, when it has some and its type can hold the copy.</summary>
        private static object? Cleaned(Member member, object value)
        {
            if (value is IDictionary dictionary)
            {
                return dictionary.Values.Cast<object?>().Any(v => v is null) ? WithoutNullValues(member.Property.PropertyType, dictionary) : null;
            }

            return value is IEnumerable sequence && member.ElementType is { } elementType && sequence.Cast<object?>().Any(item => item is null)
                ? WithoutNulls(member.Property.PropertyType, elementType, sequence)
                : null;
        }

        private bool TrySet(object owner, Member member, object? replacement, string path)
        {
            if (!repair || replacement is null || member.Property.SetMethod is null)
            {
                return false;
            }

            member.Property.SetValue(owner, replacement);
            Repaired.Add(path);
            return true;
        }

        private void Value(object value, string path, bool entriesNotNull, int depth)
        {
            if (value is string or JsonElement or JsonNode)
            {
                return;
            }

            if (IsWalked(value.GetType()) && !value.GetType().IsArray)
            {
                Object(value, path, depth);
                return;
            }

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    Entry(entry.Value, $"{path}.{entry.Key}", entriesNotNull, depth);
                }
            }
            else if (value is IEnumerable sequence)
            {
                var index = 0;
                foreach (var item in sequence)
                {
                    Entry(item, $"{path}[{index}]", entriesNotNull, depth);
                    index++;
                }
            }
        }

        private void Entry(object? entry, string path, bool notNull, int depth)
        {
            if (Full)
            {
                return;
            }

            if (entry is null)
            {
                if (notNull)
                {
                    Unrepaired.Add(path);
                }

                return;
            }

            Value(entry, path, entriesNotNull: false, depth + 1);
        }
    }
}
