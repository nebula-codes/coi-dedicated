if (args.Contains("--serve", StringComparer.Ordinal)) return await ServerSupervisorTests.RunFixtureAsync(args);
try
{
    await ControlConnectionTests.RunAsync();
    await ProfileStoreTests.RunAsync();
    await ServerSupervisorTests.RunAsync();
    Console.WriteLine("All manager protocol and process tests passed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
