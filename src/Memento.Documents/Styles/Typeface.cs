using System.Text.Json.Serialization;
using Memento.Documents.Model;

namespace Memento.Documents.Styling;

/// <summary>Sans or Serif. The paper uses system font stacks; the app ships no document fonts.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<Typeface>))]
public enum Typeface
{
    Sans,
    Serif,
}
