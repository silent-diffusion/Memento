using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Serialization;

namespace Memento.Core.Tests.Serialization;

/// <summary>
/// Walks every object type a source-generated JSON context can read (its <c>[JsonSerializable]</c> roots and every type
/// reachable from them through members, list entries and derived types), reads each one from the smallest object the
/// JSON allows (<c>{}</c>, or only its <c>required</c> members), and reports every non-nullable list, object or list
/// entry that comes back <c>null</c>, and every initializer a left-out member loses. Shared by the test projects that
/// own a context (linked, not referenced).
/// </summary>
internal static class NonNullableContracts
{
    private const int MaxDepth = 8;

    /// <summary>
    /// One line per problem: the type, the member's JSON path and the JSON that was read. <paramref name="validated"/>
    /// names members (as <c>Type.FullName:path</c>) that may read as null because the loader refuses them by name.
    /// </summary>
    public static IReadOnlyList<string> FindNullsReadFromEmptyObjects(JsonSerializerContext context, Func<Type, bool>? roots = null, IReadOnlySet<string>? validated = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var problems = new List<string>();
        foreach (var type in ObjectTypes(context, roots))
        {
            var info = context.GetTypeInfo(type)!;
            var json = MinimalJson(context, type);
            object? value;
            try
            {
                value = JsonSerializer.Deserialize(json, info);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                problems.Add($"{type.FullName}: {json} could not be read ({ex.GetType().Name}: {ex.Message})");
                continue;
            }

            foreach (var path in NullMembers.FindAll(value).Where(p => validated is null || !validated.Contains($"{type.FullName}:{p}")))
            {
                problems.Add($"{type.FullName}: '{path}' reads as null from {json}");
            }

            problems.AddRange(InitializersLost(context, type, value!, json));
        }

        return problems;
    }

    /// <summary>The smallest JSON object <paramref name="type"/> can be read from: its <c>required</c> members only.</summary>
    public static string MinimalJson(JsonSerializerContext context, Type type)
    {
        ArgumentNullException.ThrowIfNull(context);
        return MinimalObject(context, context.GetTypeInfo(type)!, 0);
    }

    /// <summary>
    /// The init trap for every other member: a member with an initializer that the JSON leaves out must keep that value
    /// (an <c>init</c> member reads as <c>null</c>, <c>0</c> or <c>false</c>). Only types with a parameterless constructor
    /// have an initializer to compare with. <c>schemaVersion</c> is left out on purpose: a file without one reads as 0,
    /// older than every schema, which the readers migrate or refuse; it must never read as the current version.
    /// </summary>
    public static IEnumerable<string> InitializersLost(JsonSerializerContext context, Type type, object read, string json)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(type);
        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            yield break;
        }

        var fresh = Activator.CreateInstance(type)!;
        foreach (var property in context.GetTypeInfo(type)!.Properties.Where(p => !p.IsRequired && p.Get is not null && p.Name != "schemaVersion"))
        {
            var expected = property.Get!(fresh);
            var actual = property.Get!(read);
            if (expected is null || (expected is not string && !expected.GetType().IsValueType))
            {
                // No initializer, or a list or object (the walk reports those when they read as null).
                continue;
            }

            if (!Equals(expected, actual))
            {
                yield return $"{type.FullName}: '{property.Name}' reads as {Show(actual)} instead of its default {Show(expected)} from {json}";
            }
        }
    }

    /// <summary>
    /// Every concrete Memento object type the context can read from its roots (all of its <c>[JsonSerializable]</c>
    /// types, or those <paramref name="roots"/> selects), in a stable order.
    /// </summary>
    public static IReadOnlyList<Type> ObjectTypes(JsonSerializerContext context, Func<Type, bool>? roots = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var start = context.GetType().GetCustomAttributesData()
            .Where(a => a.AttributeType == typeof(JsonSerializableAttribute))
            .Select(a => (Type)a.ConstructorArguments[0].Value!)
            .Where(t => roots is null || roots(t));
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(start);
        var found = new List<Type>();
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!seen.Add(type))
            {
                continue;
            }

            var info = context.GetTypeInfo(type);
            if (info is null)
            {
                continue;
            }

            switch (info.Kind)
            {
                case JsonTypeInfoKind.Object:
                    if (IsMemento(type) && !type.IsAbstract && !type.IsValueType)
                    {
                        found.Add(type);
                    }

                    foreach (var property in info.Properties)
                    {
                        queue.Enqueue(property.PropertyType);
                    }

                    break;
                case JsonTypeInfoKind.Enumerable:
                case JsonTypeInfoKind.Dictionary:
                    if (ElementTypeOf(type) is { } element)
                    {
                        queue.Enqueue(element);
                    }

                    break;
                case JsonTypeInfoKind.None:
                default:
                    break;
            }

            foreach (var derived in info.PolymorphismOptions?.DerivedTypes ?? [])
            {
                queue.Enqueue(derived.DerivedType);
            }

            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                queue.Enqueue(underlying);
            }
        }

        return [.. found.OrderBy(t => t.FullName, StringComparer.Ordinal)];
    }

    /// <summary>How many non-nullable list and object members <paramref name="types"/> declare, for the test output.</summary>
    public static int GuardedMemberCount(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var nullability = new System.Reflection.NullabilityInfoContext();
        return types.Sum(t => t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Count(p => !p.PropertyType.IsValueType && p.PropertyType != typeof(string) && p.GetIndexParameters().Length == 0
                && p.GetCustomAttributes(typeof(JsonIgnoreAttribute), true).Length == 0
                && p.GetCustomAttributes(typeof(JsonExtensionDataAttribute), true).Length == 0
                && nullability.Create(p).ReadState == System.Reflection.NullabilityState.NotNull));
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        string s => JsonSerializer.Serialize(s),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "?",
    };

    private static Type? ElementTypeOf(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        var candidates = type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces();
        var dictionary = candidates.FirstOrDefault(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        if (dictionary is not null)
        {
            return dictionary.GetGenericArguments()[1];
        }

        return candidates.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    }

    private static bool IsMemento(Type type) => type.Namespace is { } ns && (ns == "Memento" || ns.StartsWith("Memento.", StringComparison.Ordinal));

    private static string MinimalObject(JsonSerializerContext context, JsonTypeInfo info, int depth)
    {
        var json = new StringBuilder("{");
        var first = true;
        foreach (var property in info.Properties.Where(p => p.IsRequired))
        {
            json.Append(first ? " " : ", ").Append(JsonSerializer.Serialize(property.Name)).Append(": ").Append(Placeholder(context, property.PropertyType, depth + 1));
            first = false;
        }

        return json.Append(first ? "}" : " }").ToString();
    }

    private static string Placeholder(JsonSerializerContext context, Type type, int depth)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;
        if (plain == typeof(string))
        {
            return "\"x\"";
        }

        if (plain == typeof(bool))
        {
            return "false";
        }

        if (plain == typeof(Guid))
        {
            return "\"00000000-0000-0000-0000-000000000001\"";
        }

        if (plain == typeof(DateTimeOffset) || plain == typeof(DateTime))
        {
            return "\"2026-01-01T00:00:00+00:00\"";
        }

        if (plain == typeof(TimeSpan))
        {
            return "\"00:00:01\"";
        }

        if (plain == typeof(JsonElement) || plain == typeof(object))
        {
            return "null";
        }

        if (plain.IsEnum)
        {
            // Enums are written by name in these schemas when they have a string converter; a number reads otherwise.
            var name = Enum.GetNames(plain)[0];
            return context.GetTypeInfo(plain)?.Converter.GetType().Name.Contains("String", StringComparison.Ordinal) == true
                ? JsonSerializer.Serialize(JsonNamingPolicy.CamelCase.ConvertName(name))
                : "0";
        }

        if (plain.IsPrimitive || plain == typeof(decimal))
        {
            return "0";
        }

        var info = context.GetTypeInfo(plain);
        return info?.Kind switch
        {
            JsonTypeInfoKind.Enumerable => "[]",
            JsonTypeInfoKind.Dictionary => "{}",
            JsonTypeInfoKind.Object when depth < MaxDepth => MinimalObject(context, info, depth),
            _ => "{}",
        };
    }
}
