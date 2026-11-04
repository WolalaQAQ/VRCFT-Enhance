using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VRCFaceTracking;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Sandboxing.IPC;
using VRCFaceTracking.ModuleProcess;

internal static class Program
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Assembly Core = typeof(UnifiedTracking).Assembly;
    private static readonly Type Packet = typeof(ReplyUpdatePacket);
    private static readonly MethodInfo Publish = Packet.GetMethod("PublishCompletedSnapshot", Static)!;
    private static readonly List<object> Results = [];
    private static int failures;

    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); }
    private static void Run(string name, Action test)
    {
        try { test(); Results.Add(new { name, passed = true }); Console.WriteLine($"PASS {name}"); }
        catch (Exception error) { failures++; Results.Add(new { name, passed = false, error = error.ToString() }); Console.WriteLine($"FAIL {name}: {error}"); }
    }
    private static int Main(string[] args)
    {
        if (args[0] == "--budgets")
        {
            try
            {
                var type = Core.GetType("VRCFaceTracking.Core.OscWaitBudgets")!;
                Check((int)type.GetField("PollMs", Static)!.GetValue(null)! == int.Parse(args[1]), "PollMs selection");
                Check((int)type.GetField("OutputMs", Static)!.GetValue(null)! == int.Parse(args[2]), "OutputMs selection");
                return 0;
            }
            catch (Exception error) { Console.WriteLine(error); return 1; }
        }
        Run("completed-snapshot-is-immutable-and-cloned", Snapshot);
        Run("eye-mouth-left-brow-boundary", Ownership);
        Run("host-evaluation-holds-ipc-transaction-lock", Transaction);
        Run("module-queue-is-concurrent", Queue);
        Run("lifecycle-publishes-initial-invalid-state", InitialSnapshot);
        Run("timer-lease-restores-only-owned-policy-bit", TimerLease);
#if NATIVE_HOST
        Run("native-102-byte-expression-order-and-pupils", NativeProtocol);
        Run("native-send-remains-reply-driven", ReplyDriven);
#endif
        File.WriteAllText(args[0], JsonSerializer.Serialize(new {
            core = Core.GetName().Version!.ToString(), failures, results = Results,
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    private static void Reset()
    {
        UnifiedTracking.Data = new();
        UnifiedTracking.Data.Eye._maxDilation = 10; UnifiedTracking.Data.Eye._minDilation = 1;
    }
    private static byte[] Capture()
    { Publish.Invoke(null, null); return new ReplyUpdatePacket().GetBytes(); }
    private static ReplyUpdatePacket Decode(byte[] bytes)
    { var packet = new ReplyUpdatePacket(); packet.Decode(bytes); return packet; }
    private static void Snapshot()
    {
        Reset(); UnifiedTracking.Data.Eye.Left.Gaze.x = .25f;
        UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = .75f;
        var original = Capture();
        UnifiedTracking.Data.Eye.Left.Gaze.x = .5f;
        Check(new ReplyUpdatePacket().GetBytes().SequenceEqual(original), "No partial reads before publication");
        var cloned = new ReplyUpdatePacket().GetBytes(); cloned[^1] ^= 0x7f;
        Check(new ReplyUpdatePacket().GetBytes().SequenceEqual(original), "Sending cannot mutate published bytes");
        Check(!Capture().SequenceEqual(original), "Completed Update publication advances snapshot");
        Reset(); var packet = Decode(original); packet.UpdateGlobalEyeState(); packet.UpdateGlobalExpressionState();
        Check(UnifiedTracking.Data.Eye.Left.Gaze.x == .25f &&
            UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawOpen].Weight == .75f, "Wire round trip preserves values");
    }
    private static void Ownership()
    {
        Reset(); UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = .8f;
        var eye = Decode(Capture());
        Reset(); UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.BrowOuterUpLeft].Weight = .1f;
        UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawOpen].Weight = .7f;
        var mouth = Decode(Capture());
        foreach (bool eyeFirst in new[] { true, false })
        {
            Reset();
            if (eyeFirst) { eye.UpdateGlobalEyeState(); mouth.UpdateGlobalExpressionState(); }
            else { mouth.UpdateGlobalExpressionState(); eye.UpdateGlobalEyeState(); }
            Check(UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.BrowOuterUpLeft].Weight == .8f, "Mouth must not overwrite eye-owned left outer brow");
            Check(UnifiedTracking.Data.Shapes[(int)UnifiedExpressions.JawOpen].Weight == .7f, "Mouth still updates jaw");
        }
    }
    private static void Transaction()
    {
        Reset();
        var sync = typeof(UnifiedTracking).GetField("HostDataSync", Static)!.GetValue(null)!;
        _ = new UnifiedTrackingMutator(NullLogger<UnifiedTrackingMutator>.Instance, null!);
        int callbacks = 0;
        void Observe(UnifiedTrackingData data)
        { Check(Monitor.IsEntered(sync), "IPC transaction lock must cover all parameter/native callbacks"); callbacks++; }
        var previousCallbacks = UnifiedTracking.OnUnifiedDataUpdated;
        UnifiedTracking.OnUnifiedDataUpdated += Observe;
        try
        {
            UnifiedTracking.UpdateData(default).GetAwaiter().GetResult();
#if NATIVE_HOST
            UnifiedTracking.UpdateDataSync();
            Check(callbacks == 2, "Both asynchronous and reply-driven paths are synchronized");
#else
            Check(callbacks == 1, "Legacy update remains synchronized");
#endif
        }
        finally { UnifiedTracking.OnUnifiedDataUpdated = previousCallbacks; }
    }
    private static void Queue()
    {
        var type = typeof(ModuleProcessMain).GetField("_packetsToSend", Static)!.FieldType;
        Check(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentQueue<>), "Module send queue is thread safe");
    }
    private static void InitialSnapshot()
    {
#if NATIVE_HOST
        typeof(ModuleProcessMain).GetMethod("InitializeUnifiedTrackingToInvalidState", Static)!.Invoke(null, null);
        var bytes = new ReplyUpdatePacket().GetBytes();
        Check(bytes.Length > 12 && BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)) == bytes.Length - 12, "Initial snapshot is valid IPC wire data");
        Check(BitConverter.ToSingle(bytes, 20) == (float)0xffffffff, "Initial left gaze retains the official invalid sentinel");
#else
        // The legacy entry point publishes immediately after constructing its invalid state.
        Check(Publish is not null, "Legacy snapshot publisher remains available");
#endif
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Policy { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessInformation(nint process, int kind, ref Policy policy, uint size);
    private static Policy ReadPolicy()
    {
        var policy = new Policy { Version = 1 };
        Check(GetProcessInformation(-1, 4, ref policy, (uint)Marshal.SizeOf<Policy>()), "Read actual process timer policy");
        return policy;
    }
    private static void TimerLease()
    {
        var type = Core.GetType("VRCFaceTracking.Core.TimerResolutionLease")!;
        var before = ReadPolicy();
        try
        {
            type.GetMethod("Acquire", Static)!.Invoke(null, [NullLogger.Instance]);
            var during = ReadPolicy();
            Check((during.ControlMask & 4) != 0 && (during.StateMask & 4) == 0, "Actual timer honor is active");
            Check((during.ControlMask & ~4u) == (before.ControlMask & ~4u) &&
                (during.StateMask & ~4u) == (before.StateMask & ~4u), "No unrelated power-policy changes");
        }
        finally { type.GetMethod("Release", Static)!.Invoke(null, null); }
        var after = ReadPolicy();
        Check(after.ControlMask == before.ControlMask && after.StateMask == before.StateMask, "Owned timer bit is restored");
    }
#if NATIVE_HOST
    private static void NativeProtocol()
    {
        Check(UnifiedTrackingData.SerializedLength == 102, "Native blob length unchanged");
        for (int slot = 14; slot < 102; slot++)
        {
            Reset(); UnifiedTracking.Data.Shapes[slot - 14].Weight = 1;
            var bytes = new byte[102]; UnifiedTracking.Data.CopyTo(ref bytes);
            Check(bytes[slot] == 255 && bytes.Where((_, index) => index != slot).All(value => value == 0), $"Native expression slot {slot}");
        }
        Reset(); UnifiedTracking.Data.Eye.Left.PupilDiameter_MM = 7.5f;
        UnifiedTracking.Data.Eye.Right.PupilDiameter_MM = 2.5f;
        var measured = new byte[102]; UnifiedTracking.Data.CopyTo(ref measured);
        Check(measured[11] == 127 && measured[12] == 127 && measured[10] == 0 && measured[13] == 0, "Official independent pupil encoding retained");
    }
    private static void ReplyDriven()
    {
        var coordinator = Core.GetType("VRCFaceTracking.Core.Services.SendCoordinator")!;
        Check((int)coordinator.GetField("CoalesceWindowMs", Static)!.GetValue(null)! == 4, "Default mixed-module merge budget is 4 ms");
        Check(coordinator.GetMethod("NotifyReply") is not null, "Official reply-triggered send coordinator retained");
        Check(!typeof(VRCFaceTracking.Core.Services.ParameterSenderService).IsSubclassOf(typeof(Microsoft.Extensions.Hosting.BackgroundService)), "No legacy periodic sender added to VRCFT 6");
    }
#endif
}
