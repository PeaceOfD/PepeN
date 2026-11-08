namespace ServiceLib.Models.Dto;

/// <summary>
/// PattN: an entry of the lists of the WARP keys window's API address, server name and host: the value a pick puts in the
/// field, and the name the list shows it by, which for default, whose value is blank, is not its value.
/// </summary>
public sealed record ApiNameOption(string Value, string Display)
{
    /// <summary>
    /// The value: Avalonia's editable ComboBox, given a blank text for an entry, falls back to the entry's text, so that
    /// default would put the record's own text in the field.
    /// </summary>
    public override string ToString() => Value;
}
