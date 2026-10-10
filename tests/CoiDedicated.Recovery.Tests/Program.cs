using CoiDedicated.Client;

var retry = new ReconnectPolicy();
Check(retry.Schedule(0) && !retry.TryStart(1) && retry.TryStart(2), "Initial retry timing failed.");
Check(!retry.TryStart(2), "Same retry started twice.");
retry.Connected(3);
retry.ObserveHealthy(4);
Check(retry.Schedule(4) && !retry.TryStart(8) && retry.TryStart(9), "Repeated divergence did not retain attempt budget.");
Check(retry.Schedule(10) && !retry.TryStart(24) && retry.TryStart(25), "Final backoff failed.");
Check(!retry.Schedule(26) && !retry.TryStart(100), "Repeated failures bypassed retry limit.");
retry.Cancel();
Check(!retry.TryStart(100), "Canceled retry fired.");
Check(retry.Schedule(100) && retry.TryStart(102), "Manual cancellation did not reset retry budget.");
retry.Connected(103);
retry.ObserveHealthy(162);
Check(retry.Attempts == 1, "Brief success reset failure-loop protection.");
retry.ObserveHealthy(163);
Check(retry.Attempts == 0, "Healthy connection did not reset retry budget.");
Console.WriteLine("PASS bounded reconnect delays, cancellation, repeated-desync limit and healthy-session reset.");
static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
