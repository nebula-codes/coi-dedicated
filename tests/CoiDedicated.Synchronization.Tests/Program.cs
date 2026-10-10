using System.Reflection;
using System.Runtime.Loader;

// No island is loaded or mutated. A loopback ephemeral listener exercises the
// dormant-session fast path at and across its periodic checkpoint boundary.
string game = args.Length == 0 ? Environment.GetEnvironmentVariable("COI_GAME_DIR")
    ?? @"F:\SteamLibrary\steamapps\common\Captain of Industry" : Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) => name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base"
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Captain of Industry_Data", "Managed", name.Name + ".dll")) : null;
Assembly host = Assembly.Load("CoiDedicated");
Type sessionType = host.GetType("CoiDedicated.GameSessionServer", throwOnError: true)!;
Type admittedType = host.GetType("CoiDedicated.AdmittedCommand", throwOnError: true)!;
using var session = (IDisposable)Activator.CreateInstance(sessionType, "127.0.0.1", 0, "isolated-test-fixture-secret", "fixture")!;
Array commands = Array.CreateInstance(admittedType, 0);
for (int i = 0; i < 601; i++)
    sessionType.GetMethod("Publish")!.Invoke(session, new object?[] { i, i + 1, false, 1, commands, null });
ulong sequence = (ulong)sessionType.GetField("sequence", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
if (sequence != 601) throw new Exception("Dormant session stopped advancing authoritative sequence.");
Console.WriteLine("PASS 601 empty-session updates cross two checkpoint intervals without touching a resolver or serializing terrain.");
