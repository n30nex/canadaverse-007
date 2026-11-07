using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Canadaverse;

static class LauncherTests {
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, message); }
    static int Main(string[] args) {
        try {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string scratch = Path.Combine(Path.GetFullPath(args[0]), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
            Check(Core.MasterUri("https://lobbies.canadaverse.org").Scheme == "https", "HTTPS origin");
            Check(Core.MasterUri("http://127.0.0.1:18707").IsLoopback, "local test origin");
            Reject(() => Core.MasterUri("http://example.com"), "Reject public plaintext");
            Reject(() => Core.MasterUri("https://user:password@example.com"), "Reject embedded credentials");
            Reject(() => Core.MasterUri("https://example.com/path"), "Reject server path");
            Reject(() => Core.MasterUri("https://example.com?token=secret"), "Reject query credentials");
            var data = new byte[Core.RomSize]; data[0] = 42; string hash = Core.Hash(data, true);
            string zip = Path.Combine(scratch, "backup.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) using (var stream = archive.CreateEntry("../../escape.z64").Open()) stream.Write(data, 0, data.Length);
            Check(Core.ReadRom(zip, hash).SequenceEqual(data), "Read a ZIP without extracting its names");
            Check(!File.Exists(Path.Combine(scratch, "escape.z64")), "No archive path traversal");
            Reject(() => Core.ReadRom(zip), "Reject wrong real-ROM hash");
            Reject(() => Core.InstallEngineArchive(scratch, zip), "Reject unofficial engine archive");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) using (var stream = archive.CreateEntry("second.z64").Open()) stream.Write(data, 0, data.Length);
            Reject(() => Core.ReadRom(zip, hash), "Reject ambiguous ZIP");
            string fake = Path.Combine(scratch, "ProjectMidas.exe"); File.WriteAllText(fake, "synthetic executable fixture");
            Reject(() => Core.InspectEngine(fake, false), "Reject untrusted executable");
            Reject(() => Core.InspectEngine(fake, true), "Reject missing SDL");
            File.WriteAllText(Path.Combine(scratch, "SDL2.dll"), "synthetic library fixture");
            Check(Core.InspectEngine(fake, true) == Core.HashFile(fake), "Opt-in developer executable identity");
            var settings = new Settings { Host = "Test agent", Master = "http://127.0.0.1:18707" };
            Core.Save(scratch, settings); settings.Host = "Changed"; Core.Save(scratch, settings);
            Check(Core.Load(scratch).Host == "Changed", "Atomic settings roundtrip");
            using (var stream = new MemoryStream(new byte[20])) Reject(() => Core.ReadBounded(stream, 10), "Reject oversize body");
            var decoded = Core.Json.Deserialize<Dictionary<string, object>>("{\"schema\":1,\"rooms\":[{\"id\":\"example\"}]}");
            Check(Core.RoomRows(decoded).Length == 1, "Decode a real JSON room collection");
            Reject(() => Core.RoomRows(new Dictionary<string, object> { {"schema", 2}, {"rooms", new object[0]} }), "Reject future schemas");
            if (args.Length > 1) Network(args[1]).GetAwaiter().GetResult();
            if (args.Length > 2) {
                string installed = Core.InstallEngineArchive(Path.Combine(scratch, "installation"), args[2]);
                Check(Core.InspectEngine(installed, false) == (string)Core.EngineLock["exe_sha256"], "Official engine ZIP installed and verified");
            }
            Console.WriteLine("PASS: " + assertions + " native launcher assertions"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
    }
    static async Task Network(string origin) {
        using (var client = new DirectoryClient(origin)) {
            var info = await client.Request("GET", "/v1/info"); Check(!(bool)info["game_networking"], "Honest networking capability");
            var body = new Dictionary<string, object> {
                {"name", "Native client integration test"}, {"host", "Test client"}, {"map", "Facility"}, {"mode", "deathmatch"}, {"capacity", 4},
                {"build", new Dictionary<string, object> { {"engine_commit", Core.EngineLock["commit"]}, {"engine_sha256", Core.EngineLock["exe_sha256"]}, {"rom_sha1", Core.RomHash}, {"transport", "none"}, {"mods", new object[0]} }}
            };
            var created = await client.Request("POST", "/v1/rooms", body); var room = (Dictionary<string, object>)created["room"];
            string id = (string)room["id"], token = (string)created["host_token"];
            Check(!(bool)room["joinable"], "No false join capability");
            var list = await client.Request("GET", "/v1/rooms"); Check(Core.RoomRows(list).Any(o => (string)((Dictionary<string, object>)o)["id"] == id), "Created room visible to browser");
            await client.Request("POST", "/v1/rooms/" + id + "/heartbeat", new { status = "staging" }, token);
            await client.Request("DELETE", "/v1/rooms/" + id, null, token);
            list = await client.Request("GET", "/v1/rooms"); Check(!Core.RoomRows(list).Any(o => (string)((Dictionary<string, object>)o)["id"] == id), "Room closes immediately");
        }
    }
}
