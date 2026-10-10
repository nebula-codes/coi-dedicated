using BepInEx.Configuration;
using CoiDedicated.Client;

string directory = Path.Combine(Path.GetTempPath(), "coi-connection-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
string configPath = Path.Combine(directory, "client.cfg");
string keyPath = Path.Combine(directory, "Player files", "join.key");
Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
File.WriteAllText(keyPath, "fixture-secret-must-never-be-in-config");
var config = new ConfigFile(configPath, false);
var settings = new ConnectionSettings(config);
settings.Save("  example.lan  ", 28561, "\"" + keyPath + "\"", "Player_1");
var restarted = new ConnectionSettings(new ConfigFile(configPath, false));
Check(restarted.Host == "example.lan" && restarted.Port == 28561 && restarted.Name == "Player_1", "Connection settings did not reload.");
Check(restarted.KeyFile == Path.GetFullPath(keyPath), "Quoted path with spaces did not persist as an absolute path.");
Check(!File.ReadAllText(configPath).Contains(File.ReadAllText(keyPath)), "Key contents leaked into the config.");
Check(config.SaveOnConfigSet, "SaveOnConfigSet was not restored.");
string saved = File.ReadAllText(configPath);
try { settings.Save("other.lan", 0, keyPath, "Player_2"); throw new Exception("Invalid port accepted."); }
catch (ArgumentException) { }
Check(File.ReadAllText(configPath) == saved, "Invalid settings replaced saved values.");
Console.WriteLine("PASS actual BepInEx config reload retains host, port, player name and normalized key path without key contents.");
Console.WriteLine("PASS invalid connection changes leave saved settings intact.");
static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
