namespace Memento.Documents.Agenda.Text;

/// <summary>How a list line is marked.</summary>
internal enum MarkerStyle
{
    /// <summary>No marker: a plain line.</summary>
    None,

    /// <summary>1. 2. 3. (and 1) (1) 1: 1 -)</summary>
    Decimal,

    /// <summary>1.1, 1.2.3: the depth is the number of parts.</summary>
    Outline,

    LowerLetter,

    UpperLetter,

    LowerRoman,

    UpperRoman,

    /// <summary>A bullet glyph; <see cref="ListMarker.BulletFamily"/> tells - and • apart from ◦ and ▪.</summary>
    Bullet,

    /// <summary>[ ], [x], ☐, ☑.</summary>
    Checkbox,
}
