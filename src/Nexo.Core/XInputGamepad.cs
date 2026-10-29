using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Nexo.Core
{
    public enum PadButton { Left, Right, Up, Down, A, B, X, Y, LB, RB, Start, Back }

    internal struct PadSnapshot
    {
        internal int Controller;
        internal ushort Buttons;
        internal short X, Y;
        internal PadSnapshot(ushort buttons, short x = 0, short y = 0, int controller = 0)
        { Buttons = buttons; X = x; Y = y; Controller = controller; }
    }

    public sealed class XInputGamepad
    {
        private readonly Func<PadSnapshot?> read;
        private readonly Dictionary<PadButton, long> held = new Dictionary<PadButton, long>();
        private int lastController = -1;
        private int selected = -1;
        private bool fallback, unavailable;
        // The UI owns the polling instance. Share its selected XInput slot with launch preparation;
        // SDL indices are deliberately never inferred from this number.
        private static int observedController = -1;
        private long lastTime = long.MinValue;
        public bool Connected { get; private set; }
        public XInputGamepad() { read = ReadNative; }
        internal XInputGamepad(Func<PadSnapshot?> read) { this.read = read; }

        public IReadOnlyList<PadButton> Poll(long nowMs)
        {
            var snapshot = read();
            Connected = snapshot.HasValue;
            if (!Connected) { held.Clear(); lastController = -1; System.Threading.Volatile.Write(ref observedController, -1); return new PadButton[0]; }
            var pad = snapshot.Value;
            System.Threading.Volatile.Write(ref observedController, pad.Controller);
            if (pad.Controller != lastController || nowMs < lastTime) held.Clear();
            lastController = pad.Controller; lastTime = nowMs;
            var down = new HashSet<PadButton>();
            var buttons = pad.Buttons;
            bool left = (buttons & 4) != 0, right = (buttons & 8) != 0;
            bool up = (buttons & 1) != 0, bottom = (buttons & 2) != 0;
            if (!left && !right) { left = pad.X < -8000; right = pad.X > 8000; }
            if (!up && !bottom) { up = pad.Y > 8000; bottom = pad.Y < -8000; }
            if (left != right) down.Add(left ? PadButton.Left : PadButton.Right);
            if (up != bottom) down.Add(up ? PadButton.Up : PadButton.Down);
            Add(down, buttons, 0x1000, PadButton.A); Add(down, buttons, 0x2000, PadButton.B);
            Add(down, buttons, 0x4000, PadButton.X); Add(down, buttons, 0x8000, PadButton.Y);
            Add(down, buttons, 0x0100, PadButton.LB); Add(down, buttons, 0x0200, PadButton.RB);
            Add(down, buttons, 0x0010, PadButton.Start); Add(down, buttons, 0x0020, PadButton.Back);
            var result = new List<PadButton>();
            foreach (PadButton button in Enum.GetValues(typeof(PadButton)))
            {
                if (!down.Contains(button)) { held.Remove(button); continue; }
                if (!held.TryGetValue(button, out var repeat)) { result.Add(button); held[button] = nowMs + 380; }
                else if (button <= PadButton.Down && nowMs >= repeat) { result.Add(button); held[button] = nowMs + 140; }
            }
            return result.AsReadOnly();
        }

        private static void Add(HashSet<PadButton> down, ushort flags, int mask, PadButton button)
        { if ((flags & mask) != 0) down.Add(button); }

        internal static int? SetupControllerIndex()
        {
            var probe = new XInputGamepad();
            return ChooseControllerIndex(System.Threading.Volatile.Read(ref observedController),
                index => probe.TryRead((uint)index, out var unused));
        }

        internal static int? ChooseControllerIndex(int observed, Func<int, bool> connected)
        {
            if (observed >= 0 && observed < 4 && connected(observed)) return observed;
            for (int i = 0; i < 4; i++) if (connected(i)) return i;
            return null;
        }

        private PadSnapshot? ReadNative()
        {
            if (unavailable) return null;
            if (selected >= 0 && TryRead((uint)selected, out var active)) return active;
            for (uint i = 0; i < 4; i++)
                if (TryRead(i, out var pad)) { selected = (int)i; return pad; }
            selected = -1;
            return null;
        }

        private bool TryRead(uint index, out PadSnapshot result)
        {
            result = default(PadSnapshot);
            if (unavailable) return false;
            NativeState state;
            uint error;
            try
            {
                if (!fallback)
                {
                    try { error = GetState14(index, out state); }
                    catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
                    { fallback = true; error = GetState910(index, out state); }
                }
                else error = GetState910(index, out state);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException)
            { unavailable = true; return false; }
            if (error != 0) return false;
            result = new PadSnapshot(state.Gamepad.Buttons, state.Gamepad.X, state.Gamepad.Y, (int)index);
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short X, Y, RightX, RightY; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeState { public uint Packet; public NativePad Gamepad; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState14(uint index, out NativeState state);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState910(uint index, out NativeState state);
    }
}
