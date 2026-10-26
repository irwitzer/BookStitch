using BookStitch.Models;
using System.Text;

namespace BookStitch.Services;

public sealed record DiscDriveInfoPresentation(string Title, string Body);

public static class DiscDriveInfoPresentationService
{
    public static DiscDriveInfoPresentation Create(ConfiguredDiscDrive configuration, DiscDriveInfo? driveInfo, bool isConnected, bool connectionVerified = true)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var driveLabel = BuildDriveLabel(configuration.DriveRoot);
        var title = LocalizationText.Format("Disc.Drive.Title", driveLabel);
        var lines = new List<string>();

        Add(lines, LocalizationText.Get("Disc.Drive.Vendor"), configuration.Vendor);
        Add(lines, LocalizationText.Get("Disc.Drive.Model"), configuration.Product);
        Add(lines, LocalizationText.Get("Disc.Drive.Revision"), configuration.Revision);

        var driveName = driveInfo?.DriveName ?? configuration.DisplayName;
        var hasDistinctDriveName = HasDriveNameInformationGain(driveName, configuration.Vendor, configuration.Product);
        if (hasDistinctDriveName)
            Add(lines, LocalizationText.Get("Disc.Drive.Device"), driveName);

        var status = !connectionVerified
            ? LocalizationText.Get("Disc.Drive.Checking")
            : !isConnected
            ? LocalizationText.Get("Disc.Drive.Disconnected")
            : driveInfo?.DisplayName ?? LocalizationText.Get("Disc.Drive.ReadinessUnknown");
        Add(lines, LocalizationText.Get("Disc.Drive.Status"), status);

        if (connectionVerified && string.IsNullOrWhiteSpace(configuration.Vendor) &&
            string.IsNullOrWhiteSpace(configuration.Product) &&
            string.IsNullOrWhiteSpace(configuration.Revision) &&
            !hasDistinctDriveName)
        {
            lines.Insert(0, LocalizationText.Get("Disc.Drive.NoFurtherInformation"));
        }

        return new DiscDriveInfoPresentation(title, string.Join(Environment.NewLine, lines));
    }

    private static void Add(ICollection<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            lines.Add($"{label}: {value.Trim()}");
    }

    private static bool HasDriveNameInformationGain(string? driveName, string? vendor, string? product)
    {
        if (string.IsNullOrWhiteSpace(driveName) ||
            string.Equals(driveName.Trim(), "CD-Laufwerk", StringComparison.OrdinalIgnoreCase)) // persisted legacy fallback
            return false;
        var normalized = driveName.Trim();
        var combined = string.Join(" ", new[] { vendor, product }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        return string.IsNullOrWhiteSpace(combined) || !string.Equals(normalized, combined, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDriveLabel(string? root)
    {
        var normalized = DiscDriveConfigurationService.NormalizeRootPath(root);
        return normalized.Length >= 2 ? normalized[..2] : "?";
    }
}
