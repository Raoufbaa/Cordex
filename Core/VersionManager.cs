using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Cordex.Core;

public class VersionInfo
{
    [JsonPropertyName("DownloadUrl")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("ReleaseNotes")]
    public string ReleaseNotes { get; set; } = "";

    [JsonPropertyName("SupportedVersions")]
    public string[] SupportedVersions { get; set; } = Array.Empty<string>();

    [JsonPropertyName("AppDisabled")]
    public bool AppDisabled { get; set; }

    [JsonPropertyName("AppDisabledMessage")]
    public string AppDisabledMessage { get; set; } = "";
}

public class VersionCheckResult
{
    public bool IsSupported { get; set; }
    public bool IsDisabled { get; set; }
    public bool UpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string Message { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}

public static class VersionManager
{
    private const string VersionCheckUrl = "https://raoufbaa.github.io/Cordex/App_version.json";
    private const string LatestReleaseUrl = "https://github.com/Raoufbaa/Cordex/releases/latest";
    private static readonly HttpClient _httpClient = new();

    public static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }

    public static async Task<VersionCheckResult> CheckVersionAsync()
    {
        var currentVersion = GetCurrentVersion();
        var result = new VersionCheckResult
        {
            CurrentVersion = currentVersion,
            IsSupported = true,
            IsDisabled = false,
            UpdateAvailable = false
        };

        try
        {
            var response = await _httpClient.GetStringAsync(VersionCheckUrl);
            var versionInfo = JsonSerializer.Deserialize<VersionInfo>(response);

            if (versionInfo == null)
            {
                result.Message = "Unable to check for updates.";
                return result;
            }

            // Check if app is disabled
            if (versionInfo.AppDisabled)
            {
                result.IsDisabled = true;
                result.Message = versionInfo.AppDisabledMessage;
                return result;
            }

            // Check if current version is supported
            if (!versionInfo.SupportedVersions.Contains(currentVersion))
            {
                result.IsSupported = false;
                result.Message = "This version is no longer supported. Please update to the latest version.";
                result.DownloadUrl = versionInfo.DownloadUrl;
                result.ReleaseNotes = versionInfo.ReleaseNotes;
                return result;
            }

            // Check for updates
            var latestVersion = await GetLatestVersionAsync();
            if (!string.IsNullOrEmpty(latestVersion))
            {
                result.LatestVersion = latestVersion;
                
                if (CompareVersions(currentVersion, latestVersion) < 0)
                {
                    result.UpdateAvailable = true;
                    result.Message = $"A new version ({latestVersion}) is available!";
                    result.DownloadUrl = versionInfo.DownloadUrl;
                    result.ReleaseNotes = versionInfo.ReleaseNotes;
                }
            }
        }
        catch (Exception ex)
        {
            result.Message = $"Unable to check for updates: {ex.Message}";
        }

        return result;
    }

    private static async Task<string> GetLatestVersionAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Add("User-Agent", "Cordex");
            
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
            
            // Extract version from URL like: https://github.com/Raoufbaa/Cordex/releases/tag/v1.1.6
            var match = Regex.Match(finalUrl, @"/tag/v?(\d+\.\d+\.\d+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        catch
        {
            // Ignore errors when fetching latest version
        }

        return "";
    }

    private static int CompareVersions(string version1, string version2)
    {
        var v1Parts = version1.Split('.').Select(int.Parse).ToArray();
        var v2Parts = version2.Split('.').Select(int.Parse).ToArray();

        for (int i = 0; i < Math.Max(v1Parts.Length, v2Parts.Length); i++)
        {
            int v1 = i < v1Parts.Length ? v1Parts[i] : 0;
            int v2 = i < v2Parts.Length ? v2Parts[i] : 0;

            if (v1 < v2) return -1;
            if (v1 > v2) return 1;
        }

        return 0;
    }

    public static async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, Action<int>? progressCallback = null)
    {
        try
        {
            // Get the actual download URL by following redirects
            var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            request.Headers.Add("User-Agent", "Cordex");
            
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";

            // Find the installer asset URL
            var pageContent = await _httpClient.GetStringAsync(finalUrl);
            var installerMatch = Regex.Match(pageContent, @"href=""([^""]+CordexSetup\.exe)""");
            
            string installerUrl;
            if (installerMatch.Success)
            {
                installerUrl = installerMatch.Groups[1].Value;
                if (!installerUrl.StartsWith("http"))
                {
                    installerUrl = "https://github.com" + installerUrl;
                }
            }
            else
            {
                // Fallback: construct expected URL
                var versionMatch = Regex.Match(finalUrl, @"/tag/v?(\d+\.\d+\.\d+)");
                if (!versionMatch.Success) return false;
                
                var version = versionMatch.Groups[1].Value;
                installerUrl = $"https://github.com/Raoufbaa/Cordex/releases/download/v{version}/CordexSetup.exe";
            }

            // Download installer
            var tempPath = Path.Combine(Path.GetTempPath(), "CordexSetup.exe");
            
            using (var downloadResponse = await _httpClient.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                downloadResponse.EnsureSuccessStatusCode();
                
                var totalBytes = downloadResponse.Content.Headers.ContentLength ?? 0;
                using var contentStream = await downloadResponse.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                
                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (int)((totalRead * 100) / totalBytes);
                        progressCallback?.Invoke(progress);
                    }
                }
            }

            // Launch installer and exit
            var startInfo = new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true,
                Arguments = "/SILENT"
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
