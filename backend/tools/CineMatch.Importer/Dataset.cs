using System.IO.Compression;
using System.Text;

namespace CineMatch.Importer;

/// <summary>One of the MovieLens dataset sizes and where its files live.</summary>
public record Dataset(string Key, string ZipName, string Folder, bool Csv, bool HasTags, bool HasLinks, Encoding Encoding)
{
    private const string BaseUrl = "https://files.grouplens.org/datasets/movielens/";

    public static readonly IReadOnlyDictionary<string, Dataset> All = new[]
    {
        new Dataset("small", "ml-latest-small.zip", "ml-latest-small", Csv: true, HasTags: true, HasLinks: true, Encoding.UTF8),
        new Dataset("1m", "ml-1m.zip", "ml-1m", Csv: false, HasTags: false, HasLinks: false, Encoding.Latin1),
        new Dataset("10m", "ml-10m.zip", "ml-10M100K", Csv: false, HasTags: true, HasLinks: false, Encoding.UTF8),
        new Dataset("32m", "ml-32m.zip", "ml-32m", Csv: true, HasTags: true, HasLinks: true, Encoding.UTF8),
    }.ToDictionary(d => d.Key);

    private string Extension => Csv ? "csv" : "dat";

    public string File(string dataDirectory, string name) => Path.Combine(dataDirectory, Folder, $"{name}.{Extension}");

    /// <summary>Downloads and unzips the dataset unless its files are already in the data directory.</summary>
    public async Task EnsureDownloadedAsync(string dataDirectory)
    {
        if (System.IO.File.Exists(File(dataDirectory, "ratings")))
        {
            Console.WriteLine($"Dataset '{Key}' is already in {Path.Combine(dataDirectory, Folder)}");
            return;
        }

        Directory.CreateDirectory(dataDirectory);
        var zipPath = Path.Combine(dataDirectory, ZipName);
        if (!System.IO.File.Exists(zipPath))
        {
            Console.WriteLine($"Downloading {BaseUrl}{ZipName} ...");
            var partPath = zipPath + ".part";
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            await using (var source = await http.GetStreamAsync(BaseUrl + ZipName))
            await using (var target = System.IO.File.Create(partPath))
            {
                await source.CopyToAsync(target);
            }
            System.IO.File.Move(partPath, zipPath, overwrite: true);
        }

        Console.WriteLine("Unzipping ...");
        ZipFile.ExtractToDirectory(zipPath, dataDirectory, overwriteFiles: true);
    }
}
