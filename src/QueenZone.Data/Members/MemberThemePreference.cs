namespace QueenZone.Data;

/// <summary>
/// A member's appearance override. <see cref="System"/> follows the device's light/dark
/// setting; <see cref="Light"/> and <see cref="Dark"/> force a mode on web and mobile.
/// </summary>
public enum MemberThemePreference : byte
{
    /// <summary>Follow the operating system / browser setting (default).</summary>
    System = 0,

    /// <summary>Always use the light theme.</summary>
    Light = 1,

    /// <summary>Always use the dark theme.</summary>
    Dark = 2,
}
