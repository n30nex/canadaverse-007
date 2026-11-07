using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Canadaverse {
    public sealed class Settings {
        public string Engine = "";
        public string Rom = "";
        public string Master = "https://lobbies.canadaverse.org";
        public string Host = "Agent";
        public bool Development = false;
    }

    public static class Core {
        public const string Version = "0.2.0-preview.1";
        public const int RomSize = 12582912;
        public const string RomHash = "abe01e4aeb033b6c0836819f549c791b26cfde83";
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1048576, RecursionLimit = 24 };
        public static readonly Dictionary<string, object> Lock = ReadLock();
        public static object[] RoomRows(Dictionary<string, object> response) {
            object schema, items;
            if (!response.TryGetValue("schema", out schema) || Convert.ToInt32(schema) != 1 || !response.TryGetValue("rooms", out items))
                throw new InvalidDataException("Unsupported directory version.");
            var rows = items as IList;
            if (rows == null || rows.Count > 200) throw new InvalidDataException("Invalid room list.");
            return rows.Cast<object>().ToArray();
        }
        public static Dictionary<string, object> EngineLock { get { return (Dictionary<string, object>)Lock["engine"]; } }
        static Dictionary<string, object> ReadLock() {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("versions.lock.json"))
            using (var reader = new StreamReader(stream)) return Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
        }
        public static string Hash(byte[] bytes, bool sha1 = false) {
            using (HashAlgorithm algorithm = sha1 ? (HashAlgorithm)SHA1.Create() : SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static string HashFile(string path) {
            using (var stream = File.OpenRead(path)) using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static byte[] ReadBounded(Stream stream, long limit) {
            using (var output = new MemoryStream()) {
                var buffer = new byte[65536]; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) {
                    if (output.Length + count > limit) throw new InvalidDataException("File exceeds the allowed size.");
                    output.Write(buffer, 0, count);
                }
                return output.ToArray();
            }
        }
        public static byte[] ReadRom(string path, string expectedHash = RomHash) {
            if (!File.Exists(path)) throw new InvalidDataException("Choose your GoldenEye USA .z64 ROM or ZIP backup in Setup.");
            byte[] data;
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) {
                using (var archive = ZipFile.OpenRead(path)) {
                    if (archive.Entries.Count > 100) throw new InvalidDataException("The ROM ZIP contains too many files.");
                    var matches = archive.Entries.Where(e => Path.GetExtension(e.FullName).Equals(".z64", StringComparison.OrdinalIgnoreCase) && e.Length == RomSize).ToList();
                    if (matches.Count != 1) throw new InvalidDataException("The ZIP must contain exactly one 12 MiB USA .z64 ROM.");
                    using (var stream = matches[0].Open()) data = ReadBounded(stream, RomSize);
                }
            } else {
                if (!Path.GetExtension(path).Equals(".z64", StringComparison.OrdinalIgnoreCase) || new FileInfo(path).Length != RomSize)
                    throw new InvalidDataException("Expected a 12 MiB USA .z64 ROM, or a ZIP containing one.");
                using (var stream = File.OpenRead(path)) data = ReadBounded(stream, RomSize);
            }
            if (data.Length != RomSize || Hash(data, true) != expectedHash)
                throw new InvalidDataException("Unsupported ROM. Expected GoldenEye 007 USA, SHA1 " + expectedHash + ".");
            return data;
        }
        public static string InspectEngine(string path, bool development) {
            if (!File.Exists(path) || !Path.GetFileName(path).Equals("ProjectMidas.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose ProjectMidas.exe in Setup, or install the pinned engine.");
            string digest = HashFile(path);
            if (digest != (string)EngineLock["exe_sha256"] && !development)
                throw new InvalidDataException("Engine does not match the verified release. Enable local source builds only for an executable you built or trust.");
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(path), "SDL2.dll")))
                throw new InvalidDataException("SDL2.dll is missing next to the engine.");
            return digest;
        }
        public static Settings Load(string state) {
            string path = Path.Combine(state, "settings.json");
            if (!File.Exists(path)) return new Settings();
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Settings file is too large.");
            return Json.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        public static void Save(string state, Settings settings) {
            Directory.CreateDirectory(state);
            string path = Path.Combine(state, "settings.json");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, Json.Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null, true); else File.Move(temporary, path);
        }
        public static string CacheRom(string state, byte[] data) {
            string cache = Path.Combine(state, "rom-cache"); Directory.CreateDirectory(cache);
            string rom = Path.Combine(cache, RomHash + ".z64");
            if (!File.Exists(rom) || new FileInfo(rom).Length != RomSize || Hash(File.ReadAllBytes(rom), true) != RomHash) {
                string temporary = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllBytes(temporary, data);
                if (File.Exists(rom)) File.Replace(temporary, rom, null, true); else File.Move(temporary, rom);
            }
            return rom;
        }
        public static Process Launch(Settings settings, string state) {
            InspectEngine(settings.Engine, settings.Development);
            string rom = CacheRom(state, ReadRom(settings.Rom));
            return Process.Start(new ProcessStartInfo {
                FileName = Path.GetFullPath(settings.Engine), Arguments = "--rom-file \"" + rom + "\"",
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.Engine)), UseShellExecute = false
            });
        }
        public static Uri MasterUri(string input) {
            Uri uri;
            if (!Uri.TryCreate(input, UriKind.Absolute, out uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
                throw new InvalidDataException("Master server must be an HTTPS origin, for example https://lobbies.canadaverse.org.");
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
                throw new InvalidDataException("Use HTTPS. Plain HTTP is allowed only for a local test server.");
            return uri;
        }
        public static Dictionary<string, object> RoomBody(Settings settings, string name, string map, int capacity) {
            string digest = InspectEngine(settings.Engine, settings.Development);
            ReadRom(settings.Rom);
            // No file contents, local paths, saves or credentials leave the PC.
            return new Dictionary<string, object> {
                {"name", name.Trim()}, {"host", settings.Host.Trim()}, {"map", map}, {"mode", "deathmatch"}, {"capacity", capacity},
                {"build", new Dictionary<string, object> {
                    {"engine_commit", EngineLock["commit"]}, {"engine_sha256", digest}, {"rom_sha1", RomHash},
                    {"transport", "none"}, {"mods", new object[0]}
                }}
            };
        }
        public static async Task<string> InstallEngine(string state, Action<string> progress) {
            string root = Path.GetFullPath(Path.Combine(state, "engines")); Directory.CreateDirectory(root);
            string destination = Path.Combine(root, "midas-1.0.2b");
            if (Directory.Exists(destination)) { InspectEngine(Path.Combine(destination, "ProjectMidas.exe"), false); return Path.Combine(destination, "ProjectMidas.exe"); }
            progress("Downloading the pinned Project Midas preview…");
            using (var client = new HttpClient()) {
                client.Timeout = TimeSpan.FromMinutes(4);
                client.MaxResponseContentBufferSize = 64 * 1024 * 1024;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CanadaverseLauncher/0.2.0");
                using (var response = await client.GetAsync((string)EngineLock["release_url"])) {
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage.RequestUri.Scheme != "https") throw new InvalidDataException("Download redirected away from HTTPS.");
                    byte[] data = await response.Content.ReadAsByteArrayAsync();
                    return await Task.Run(() => InstallEngineBytes(state, data));
                }
            }
        }
        public static string InstallEngineArchive(string state, string archive) {
            if (!File.Exists(archive)) throw new InvalidDataException("Choose the official Project Midas Windows ZIP.");
            using (var stream = File.OpenRead(archive)) return InstallEngineBytes(state, ReadBounded(stream, 64 * 1024 * 1024));
        }
        static string InstallEngineBytes(string state, byte[] data) {
                    if (Hash(data) != (string)EngineLock["archive_sha256"]) throw new InvalidDataException("Engine archive checksum failed. Nothing was installed.");
                    string root = Path.GetFullPath(Path.Combine(state, "engines")); Directory.CreateDirectory(root);
                    string destination = Path.Combine(root, "midas-1.0.2b");
                    if (Directory.Exists(destination)) { InspectEngine(Path.Combine(destination, "ProjectMidas.exe"), false); return Path.Combine(destination, "ProjectMidas.exe"); }
                    string staging = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(staging);
                    using (var memory = new MemoryStream(data)) using (var zip = new ZipArchive(memory)) {
                        if (zip.Entries.Count > 200) throw new InvalidDataException("Unexpected engine archive.");
                        long total = 0;
                        foreach (var entry in zip.Entries) {
                            const string prefix = "ProjectMidas-v1.0.2b-windows/";
                            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected archive root.");
                            string relative = entry.FullName.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
                            if (relative.Length == 0) continue;
                            string target = Path.GetFullPath(Path.Combine(staging, relative));
                            if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe archive path.");
                            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) { Directory.CreateDirectory(target); continue; }
                            total += entry.Length;
                            if (total > 128 * 1024 * 1024) throw new InvalidDataException("Engine archive exceeds size limit.");
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            using (var stream = entry.Open()) File.WriteAllBytes(target, ReadBounded(stream, entry.Length));
                        }
                    }
                    InspectEngine(Path.Combine(staging, "ProjectMidas.exe"), false);
                    Directory.Move(staging, destination);
            return Path.Combine(destination, "ProjectMidas.exe");
        }
    }

    public sealed class DirectoryClient : IDisposable {
        readonly HttpClient http;
        public DirectoryClient(string origin) {
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            http.BaseAddress = Core.MasterUri(origin); http.Timeout = TimeSpan.FromSeconds(8);
            http.MaxResponseContentBufferSize = 1048576;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("CanadaverseLauncher/0.2.0");
        }
        public async Task<Dictionary<string, object>> Request(string method, string path, object body = null, string token = null) {
            using (var request = new HttpRequestMessage(new HttpMethod(method), path)) {
                if (body != null) request.Content = new StringContent(Core.Json.Serialize(body), Encoding.UTF8, "application/json");
                if (token != null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                using (var response = await http.SendAsync(request)) {
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    string text = Encoding.UTF8.GetString(bytes);
                    Dictionary<string, object> result;
                    try { result = Core.Json.Deserialize<Dictionary<string, object>>(text); }
                    catch { throw new InvalidDataException("The master server returned an unsupported response (HTTP " + (int)response.StatusCode + ")."); }
                    if (!response.IsSuccessStatusCode) throw new InvalidDataException("Master server: " + (result != null && result.ContainsKey("detail") ? Convert.ToString(result["detail"]) : "HTTP " + (int)response.StatusCode));
                    if (result == null) throw new InvalidDataException("Empty directory response.");
                    return result;
                }
            }
        }
        public void Dispose() { http.Dispose(); }
    }
}
