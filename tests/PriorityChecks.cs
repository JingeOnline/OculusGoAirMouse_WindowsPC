using System.Diagnostics;
using OculusGoAirMouse.Services;

static class PriorityChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool result, string message) { if (!result) throw new Exception(message); checks++; }
        var policy = new CpuPriorityPolicy();
        Check(!policy.Update(.8, true), "Single busy sample boosted priority");
        Check(policy.Update(.8, true), "Sustained busy CPU did not boost priority");
        for (int i = 0; i < 4; i++) Check(policy.Update(.6, true), "Quiet hysteresis restored priority early");
        Check(!policy.Update(.6, true), "Sustained quiet CPU did not restore priority");
        policy.Update(.9, true); policy.Update(.9, true);
        Check(policy.Update(.7, true), "Middle band caused priority flapping");
        Check(!policy.Update(.99, false), "Inactive mouse left process boosted");
        Check(!policy.Update(null, true) && !policy.Update(double.NaN, true) && !policy.Update(2, true), "Invalid samples triggered boost");
        Check(!policy.Update(.9, true), "First busy sample unexpectedly boosted");
        policy.Update(.7, true);
        Check(!policy.Update(.9, true), "Non-consecutive busy samples triggered boost");
        Check(policy.Update(.9, true), "Second consecutive busy sample missed boost");
        var platform = new FakePlatform(); int active = 1;
        var service = new AdaptivePriorityService(() => Volatile.Read(ref active) != 0, platform, TimeSpan.FromMilliseconds(5));
        await Until(() => platform.Priority == ProcessPriorityClass.AboveNormal);
        Check(platform.Priority == ProcessPriorityClass.AboveNormal, "Monitor did not apply elevated priority");
        Volatile.Write(ref active, 0);
        await Until(() => platform.Priority == ProcessPriorityClass.Normal);
        Check(platform.Priority == ProcessPriorityClass.Normal, "Inactive mouse did not restore process priority");
        Task first = service.DisposeAsync().AsTask(), second = service.DisposeAsync().AsTask();
        await Task.WhenAll(first, second);
        Check(ReferenceEquals(first, second) && platform.Disposals == 1, "Priority service shutdown was not idempotent");
        int reads = platform.Reads; await Task.Delay(30);
        Check(reads == platform.Reads, "Priority monitor kept running after disposal");
        platform = new FakePlatform { ThrowRead = true };
        service = new AdaptivePriorityService(() => true, platform, TimeSpan.FromMilliseconds(5));
        await Until(() => platform.Reads >= 2); platform.ThrowRead = false;
        await Until(() => platform.Priority == ProcessPriorityClass.AboveNormal);
        await service.DisposeAsync();
        Check(platform.Priority == ProcessPriorityClass.Normal && platform.Disposals == 1, "Monitor failed to recover from sampling error or restore original priority");
        platform = new FakePlatform { ThrowSetOnce = true };
        service = new AdaptivePriorityService(() => true, platform, TimeSpan.FromMilliseconds(5));
        await Until(() => platform.Priority == ProcessPriorityClass.AboveNormal);
        await service.DisposeAsync();
        Check(platform.Priority == ProcessPriorityClass.Normal, "Priority-setting failure stopped monitor");
        platform = new FakePlatform { Priority = ProcessPriorityClass.High };
        service = new AdaptivePriorityService(() => true, platform, TimeSpan.FromMilliseconds(5));
        await Until(() => platform.Reads >= 3);
        Check(platform.Priority == ProcessPriorityClass.High, "Monitor lowered inherited elevated priority"); await service.DisposeAsync();
        using var native = new WindowsPriorityPlatform();
        native.ReadCpuLoad(); await Task.Delay(30);
        double? load = native.ReadCpuLoad();
        Check(load.HasValue && load >= 0 && load <= 1, "Native CPU sample invalid");
        return checks;
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate()) await Task.Delay(5, timeout.Token);
    }
    private sealed class FakePlatform : IPriorityPlatform
    {
        private int priority = (int)ProcessPriorityClass.Normal;
        public int Reads, Disposals;
        public volatile bool ThrowRead, ThrowSetOnce;
        public ProcessPriorityClass Priority
        {
            get => (ProcessPriorityClass)Volatile.Read(ref priority);
            set { if (ThrowSetOnce) { ThrowSetOnce = false; throw new InvalidOperationException("priority denied"); } Volatile.Write(ref priority, (int)value); }
        }
        public double? ReadCpuLoad() { Interlocked.Increment(ref Reads); if (ThrowRead) throw new InvalidOperationException("sample failure"); return .9; }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
}
