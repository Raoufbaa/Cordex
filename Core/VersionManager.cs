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
                
                // Try to get release notes from GitHub
                var latestVersion = await GetLatestVersionAsync();
                if (!string.IsNullOrEmpty(latestVersion))
                {
                    result.LatestVersion = latestVersion;
                    var releaseNotes = await GetReleaseNotesAsync(latestVersion);
                    result.ReleaseNotes = !string.IsNullOrEmpty(releaseNotes) ? releaseNotes : versionInfo.ReleaseNotes;
                }
                else
                {
                    result.ReleaseNotes = versionInfo.ReleaseNotes;
                }
                
                return result;
            }

            // Check for updates
            var latestVer = await GetLatestVersionAsync();
            if (!string.IsNullOrEmpty(latestVer))
            {
                result.LatestVersion = latestVer;
                
                if (CompareVersions(currentVersion, latestVer) < 0)
                {
                    result.UpdateAvailable = true;
                    result.Message = $"A new version ({latestVer}) is available!";
                    result.DownloadUrl = versionInfo.DownloadUrl;
                    
                    // Get release notes from GitHub
                    var releaseNotes = await GetReleaseNotesAsync(latestVer);
                    result.ReleaseNotes = !string.IsNullOrEmpty(releaseNotes) ? releaseNotes : versionInfo.ReleaseNotes;
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

    private static async Task<string> GetReleaseNotesAsync(string version)
    {
        try
        {
            // Try GitHub API first
            var apiUrl = $"https://api.github.com/repos/Raoufbaa/Cordex/releases/tags/v{version}";
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("User-Agent", "Cordex");
            request.Headers.Add("Accept", "application/vnd.github.v3+json");
            
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonSerializer.Deserialize<JsonElement>(content);
                
                if (jsonDoc.TryGetProperty("body", out var bodyElement))
                {
                    var body = bodyElement.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        // Clean up markdown formatting for display
                        body = body.Replace("## ", "").Replace("### ", "").Replace("**", "");
                        return body.Trim();
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
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

            // Extract version from URL
            var versionMatch = Regex.Match(finalUrl, @"/tag/v?(\d+\.\d+\.\d+)");
            if (!versionMatch.Success)
            {
                return false;
            }

            var version = versionMatch.Groups[1].Value;

            // Try multiple possible installer names
            var possibleInstallerNames = new[]
            {
                "Cordex_Setup.exe",
                "CordexSetup.exe",
                $"Cordex_Setup-{version}.exe",
                $"Cordex-Setup-{version}.exe",
                $"CordexSetup-{version}.exe",
                "Cordex-Setup.exe"
            };

            string? installerUrl = null;

            // First, try to find the installer in the release page
            try
            {
                var pageContent = await _httpClient.GetStringAsync(finalUrl);
                
                foreach (var installerName in possibleInstallerNames)
                {
                    var pattern = $@"href=""([^""]+{Regex.Escape(installerName)})""";
                    var installerMatch = Regex.Match(pageContent, pattern);
                    
                    if (installerMatch.Success)
                    {
                        installerUrl = installerMatch.Groups[1].Value;
                        if (!installerUrl.StartsWith("http"))
                        {
                            installerUrl = "https://github.com" + installerUrl;
                        }
                        break;
                    }
                }
            }
            catch
            {
                // Ignore page parsing errors
            }

            // If not found, try direct URLs
            if (string.IsNullOrEmpty(installerUrl))
            {
                foreach (var installerName in possibleInstallerNames)
                {
                    var testUrl = $"https://github.com/Raoufbaa/Cordex/releases/download/v{version}/{installerName}";
                    
                    try
                    {
                        var headRequest = new HttpRequestMessage(HttpMethod.Head, testUrl);
                        headRequest.Headers.Add("User-Agent", "Cordex");
                        var headResponse = await _httpClient.SendAsync(headRequest);
                        
                        if (headResponse.IsSuccessStatusCode)
                        {
                            installerUrl = testUrl;
                            break;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            if (string.IsNullOrEmpty(installerUrl))
            {
                return false;
            }

            // Report initial progress
            progressCallback?.Invoke(0);

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
                int lastReportedProgress = 0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (int)((totalRead * 100) / totalBytes);
                        
                        // Only report if progress changed by at least 1%
                        if (progress != lastReportedProgress)
                        {
                            lastReportedProgress = progress;
                            progressCallback?.Invoke(progress);
                        }
                    }
                    else
                    {
                        // If we don't know total size, report indeterminate progress
                        progressCallback?.Invoke(-1);
                    }
                }
                
                // Ensure we report 100% at the end
                progressCallback?.Invoke(100);
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
