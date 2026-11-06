namespace BookStitch.Services;

public static class AudioProfilePresentationService
{
    public static string Format(string? codec, string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile) || profile.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return "—";
        if (!string.Equals(codec, "aac", StringComparison.OrdinalIgnoreCase)) return profile;
        return profile switch
        {
            "LC" => "AAC-LC",
            "HE-AAC" => "HE-AAC v1",
            "HE-AACv2" => "HE-AAC v2",
            _ => profile
        };
    }
}
