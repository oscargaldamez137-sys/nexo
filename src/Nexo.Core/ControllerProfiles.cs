using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexo.Core
{
    // Versioned format references and limitations: LEEME.md, "Mandos y configuración portable".
    internal static class ControllerProfiles
    {
        internal static Dictionary<string, string> Pairs(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in text.Split('|'))
            {
                int split = line.IndexOf('=');
                result.Add(line.Substring(0, split), line.Substring(split + 1));
            }
            return result;
        }

        internal static Dictionary<string, string> Ps2Keyboard => Pairs(
            "Up=Keyboard/Up|Right=Keyboard/Right|Down=Keyboard/Down|Left=Keyboard/Left|" +
            "LUp=Keyboard/W|LRight=Keyboard/D|LDown=Keyboard/S|LLeft=Keyboard/A|" +
            "RUp=Keyboard/T|RRight=Keyboard/H|RDown=Keyboard/G|RLeft=Keyboard/F|" +
            "Start=Keyboard/Return|Select=Keyboard/Backspace|Triangle=Keyboard/I|Circle=Keyboard/L|" +
            "Cross=Keyboard/K|Square=Keyboard/J|L1=Keyboard/Q|L2=Keyboard/1|L3=Keyboard/2|R1=Keyboard/E|R2=Keyboard/3|R3=Keyboard/4");

        internal static Dictionary<string, string> Ps2Defaults
        {
            get
            {
                var d = Ps2Keyboard;
                foreach (var p in Pairs("Type=DualShock2|InvertL=0|InvertR=0|Deadzone=0|AxisScale=1.33|LargeMotorScale=1|SmallMotorScale=1|ButtonDeadzone=0|PressureModifier=0.5|Analog=|LargeMotor=|SmallMotor=|Pressure=|AnalogLimiter=")) d.Add(p.Key, p.Value);
                return d;
            }
        }

        internal static Dictionary<string, string> Ps2Pad(int? xinput)
        {
            var d = Pairs("Up=DPadUp|Right=DPadRight|Down=DPadDown|Left=DPadLeft|Triangle=FaceNorth|Circle=FaceEast|Cross=FaceSouth|Square=FaceWest|Select=Back|Start=Start|L1=LeftShoulder|L2=+LeftTrigger|R1=RightShoulder|R2=+RightTrigger|L3=LeftStick|R3=RightStick|LUp=-LeftY|LRight=+LeftX|LDown=+LeftY|LLeft=-LeftX|RUp=-RightY|RRight=+RightX|RDown=+RightY|RLeft=-RightX|Analog=Guide|LargeMotor=LargeMotor|SmallMotor=SmallMotor");
            if (xinput.HasValue) { d["Cross"] = "A"; d["Circle"] = "B"; d["Square"] = "X"; d["Triangle"] = "Y"; }
            string prefix = xinput.HasValue ? "XInput-" + xinput.Value + "/" : "SDL-0/";
            foreach (var key in d.Keys.ToArray()) d[key] = prefix + d[key];
            d.Add("Type", "DualShock2");
            return d;
        }

        internal static Dictionary<string, string> DolphinKeyboard => Pairs(
            "Buttons/A=`X`|Buttons/B=`Z`|Buttons/X=`C`|Buttons/Y=`S`|Buttons/Z=`D`|Buttons/Start=`RETURN`|" +
            "Main Stick/Up=`UP`|Main Stick/Down=`DOWN`|Main Stick/Left=`LEFT`|Main Stick/Right=`RIGHT`|Main Stick/Modifier=`Shift`|" +
            "C-Stick/Up=`I`|C-Stick/Down=`K`|C-Stick/Left=`J`|C-Stick/Right=`L`|C-Stick/Modifier=`Ctrl`|" +
            "Triggers/L=`Q`|Triggers/R=`W`|D-Pad/Up=`T`|D-Pad/Down=`G`|D-Pad/Left=`F`|D-Pad/Right=`H`|" +
            "Triforce/Test=`1`|Triforce/Service=`2`|Triforce/Coin=`3`");

        internal static Dictionary<string, string> DolphinPad(int index) => Pairs(
            "Device=XInput/" + index + "/Gamepad|Buttons/A=`Button A`|Buttons/B=`Button B`|Buttons/X=`Button X`|Buttons/Y=`Button Y`|Buttons/Z=`Shoulder R`|Buttons/Start=`Start`|" +
            "Main Stick/Up=`Left Y+`|Main Stick/Down=`Left Y-`|Main Stick/Left=`Left X-`|Main Stick/Right=`Left X+`|Main Stick/Modifier=|Main Stick/Dead Zone=15|" +
            "C-Stick/Up=`Right Y+`|C-Stick/Down=`Right Y-`|C-Stick/Left=`Right X-`|C-Stick/Right=`Right X+`|C-Stick/Modifier=|C-Stick/Dead Zone=15|" +
            "Triggers/L=`Trigger L` > 0.9|Triggers/R=`Trigger R` > 0.9|Triggers/L-Analog=`Trigger L`|Triggers/R-Analog=`Trigger R`|" +
            "D-Pad/Up=`Pad N`|D-Pad/Down=`Pad S`|D-Pad/Left=`Pad W`|D-Pad/Right=`Pad E`|Rumble/Motor=`Motor L`");

        // Emulated Wii Remote + Nunchuk on an Xbox pad: pointer on the right stick, swing on B,
        // Nunchuk stick on the left stick and Nunchuk swing on RB.
        internal static Dictionary<string, string> DolphinWiimote(int index) => Pairs(
            "Device=XInput/" + index + "/Gamepad|Buttons/A=`Button A`|Buttons/B=`Trigger R`|Buttons/1=`Button X`|Buttons/2=`Button Y`|" +
            "Buttons/-=`Back`|Buttons/+=`Start`|Buttons/Home=`Thumb R`|D-Pad/Up=`Pad N`|D-Pad/Down=`Pad S`|D-Pad/Left=`Pad W`|D-Pad/Right=`Pad E`|" +
            "IR/Up=`Right Y+`|IR/Down=`Right Y-`|IR/Left=`Right X-`|IR/Right=`Right X+`|IR/Relative Input=True|" +
            "Shake/X=`Button B`|Shake/Y=`Button B`|Shake/Z=`Button B`|Rumble/Motor=`Motor L`|Extension=Nunchuk|" +
            "Nunchuk/Buttons/C=`Shoulder L`|Nunchuk/Buttons/Z=`Trigger L`|" +
            "Nunchuk/Stick/Up=`Left Y+`|Nunchuk/Stick/Down=`Left Y-`|Nunchuk/Stick/Left=`Left X-`|Nunchuk/Stick/Right=`Left X+`|Nunchuk/Stick/Dead Zone=15|Nunchuk/Stick/Calibration=100.00|" +
            "Nunchuk/Shake/X=`Shoulder R`|Nunchuk/Shake/Y=`Shoulder R`|Nunchuk/Shake/Z=`Shoulder R`");

        // Dolphin 2609 Wiimote::LoadDefaults / Nunchuk::LoadDefaults on Windows.
        internal static Dictionary<string, string> DolphinWiimoteKeyboard
        {
            get
            {
                var d = Pairs("Device=DInput/0/Keyboard Mouse|Source=1|Buttons/A=`Click 0`|Buttons/B=`Click 1`|Buttons/1=`1`|Buttons/2=`2`|Buttons/-=Q|Buttons/+=E|Buttons/Home=RETURN|" +
                    "D-Pad/Up=UP|D-Pad/Down=DOWN|D-Pad/Left=LEFT|D-Pad/Right=RIGHT|IR/Up=`Cursor Y-`|IR/Down=`Cursor Y+`|IR/Left=`Cursor X-`|IR/Right=`Cursor X+`|IR/Relative Input=False|" +
                    "Extension=Nunchuk|Nunchuk/Buttons/C=LCONTROL|Nunchuk/Buttons/Z=LSHIFT|Nunchuk/Stick/Up=W|Nunchuk/Stick/Down=S|Nunchuk/Stick/Left=A|Nunchuk/Stick/Right=D|" +
                    "Nunchuk/Stick/Calibration=100.00 141.42 100.00 141.42 100.00 141.42 100.00 141.42");
                foreach (var axis in new[] { "X", "Y", "Z" }) { d["Shake/" + axis] = "`Click 2`"; d["Nunchuk/Shake/" + axis] = "`Click 2`"; }
                foreach (var axis in new[] { "Up", "Down", "Left", "Right", "Forward", "Backward" }) d["IMUAccelerometer/" + axis] = "`Accel " + axis + "`";
                foreach (var axis in new[] { "Pitch Up", "Pitch Down", "Roll Left", "Roll Right", "Yaw Left", "Yaw Right" }) d["IMUGyroscope/" + axis] = "`Gyro " + axis + "`";
                for (int i = 1; i <= 4; i++) foreach (var axis in new[] { "X", "Y", "Size" }) d["IRPassthrough/Object " + i + " " + axis] = "`IR Object " + i + " " + axis + "`";
                return d;
            }
        }

        internal static Dictionary<string, string> Ps3Keyboard => Pairs(
            "Left Stick Left=A|Left Stick Down=S|Left Stick Right=D|Left Stick Up=W|Right Stick Left=Del|Right Stick Down=End|Right Stick Right=PgDown|Right Stick Up=Home|" +
            "Start=Return|Select=Space|PS Button=Backspace|Square=Z|Cross=X|Circle=C|Triangle=V|Left=Left|Down=Down|Right=Right|Up=Up|R1=E|R2=T|R3=G|L1=Q|L2=R|L3=F");

        internal static Dictionary<string, string> Ps3Pad => Pairs(
            "Left Stick Left=LS X-|Left Stick Down=LS Y-|Left Stick Right=LS X+|Left Stick Up=LS Y+|Right Stick Left=RS X-|Right Stick Down=RS Y-|Right Stick Right=RS X+|Right Stick Up=RS Y+|" +
            "Start=Start|Select=Back|PS Button=Guide|Square=X|Cross=A|Circle=B|Triangle=Y|Left=Left|Down=Down|Right=Right|Up=Up|R1=RB|R2=RT|R3=RS|L1=LB|L2=LT|L3=LS|" +
            "Left Stick Deadzone=7849|Right Stick Deadzone=8689|Left Trigger Threshold=30|Right Trigger Threshold=30|Left Stick Anti-Deadzone=4259|Right Stick Anti-Deadzone=4259");

        internal static Dictionary<string, string> Ps3Defaults
        {
            get
            {
                var d = Ps3Keyboard;
                d.Add("Vibration Threshold", "63");
                foreach (var p in Pairs("IR Nose=|IR Tail=|IR Left=|IR Right=|Tilt Left=|Tilt Right=|Orientation Reset Button=|Orientation Enabled=false|Pressure Intensity Button=|Pressure Intensity Percent=50|Pressure Intensity Toggle Mode=false|Pressure Intensity Deadzone=0|Analog Limiter Button=|Analog Limiter Toggle Mode=false|Left Stick Multiplier=100|Right Stick Multiplier=100|Left Stick Deadzone=0|Right Stick Deadzone=0|Left Stick Anti-Deadzone=0|Right Stick Anti-Deadzone=0|Left Trigger Threshold=0|Right Trigger Threshold=0|Left Pad Squircling Factor=4000|Right Pad Squircling Factor=4000|Color Value R=0|Color Value G=0|Color Value B=0|Blink LED when battery is below 20%=true|Use LED as a battery indicator=false|LED battery indicator brightness=50|Player LED enabled=true|Large Vibration Motor Multiplier=100|Small Vibration Motor Multiplier=100|Switch Vibration Motors=false|Mouse Movement Mode=Relative|Mouse Deadzone X Axis=60|Mouse Deadzone Y Axis=60|Mouse Acceleration X Axis=200|Mouse Acceleration Y Axis=250|Left Stick Lerp Factor=100|Right Stick Lerp Factor=100|Analog Button Lerp Factor=100|Trigger Lerp Factor=100|Device Class Type=0|Vendor ID=0|Product ID=0")) d.Add(p.Key, p.Value);
                return d;
            }
        }
    }
}
