using System.IO;
using System.Text.Json;
using NetworkProfiles.Models;

namespace NetworkProfiles.Services;

/// <summary>
/// Stores profiles under the user's LocalAppData folder. Import/export handles data only;
/// importing never applies network settings.
/// </summary>
public sealed class ProfileStore
{
    private const int MaxImportBytes = 1_048_576;
    private const int MaxProfiles = 500;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string StorePath { get; }

    public ProfileStore(string? storePath = null)
    {
        StorePath = storePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkProfiles",
            "profiles.json");
    }

    public IReadOnlyList<NetworkProfile> Load()
    {
        if (!File.Exists(StorePath))
            return [];

        var profiles = DeserializeChecked(File.ReadAllText(StorePath));
        ValidateCollection(profiles);
        return profiles;
    }

    public void Save(IEnumerable<NetworkProfile> profiles)
    {
        var collection = profiles.ToList();
        ValidateCollection(collection);

        var directory = Path.GetDirectoryName(Path.GetFullPath(StorePath))
            ?? throw new InvalidOperationException("Profil klasörü belirlenemedi.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".profiles-{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(collection, JsonOptions));
            File.Move(temporaryPath, StorePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public IReadOnlyList<NetworkProfile> ImportFrom(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("İçe aktarılacak profil dosyası bulunamadı.", path);
        if (info.Length > MaxImportBytes)
            throw new InvalidDataException("Profil dosyası 1 MB sınırını aşıyor.");

        var imported = DeserializeChecked(File.ReadAllText(path));
        ValidateCollection(imported);
        return imported;
    }

    public void ExportTo(string path, IEnumerable<NetworkProfile> profiles)
    {
        var collection = profiles.ToList();
        ValidateCollection(collection);
        File.WriteAllText(path, JsonSerializer.Serialize(collection, JsonOptions));
    }

    private static List<NetworkProfile> DeserializeChecked(string json)
    {
        var profiles = JsonSerializer.Deserialize<List<NetworkProfile>>(json, JsonOptions)
            ?? throw new InvalidDataException("Profil dosyası boş veya geçersiz JSON içeriyor.");
        if (profiles.Count > MaxProfiles)
            throw new InvalidDataException($"En fazla {MaxProfiles} profil içe aktarılabilir.");
        return profiles;
    }

    private static void ValidateCollection(IReadOnlyCollection<NetworkProfile> profiles)
    {
        if (profiles.Count > MaxProfiles)
            throw new InvalidDataException($"En fazla {MaxProfiles} profil saklanabilir.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (profile is null)
                throw new InvalidDataException("Profil listesi boş/null kayıt içeremez.");

            var errors = ProfileValidationService.Validate(profile);
            if (errors.Count > 0)
                throw new InvalidDataException($"'{profile.Name}' profili geçersiz: {string.Join(" ", errors)}");

            if (!Guid.TryParseExact(profile.Id, "N", out _) || !ids.Add(profile.Id))
                throw new InvalidDataException("Profil kimlikleri geçerli ve benzersiz olmalıdır.");
        }
    }
}