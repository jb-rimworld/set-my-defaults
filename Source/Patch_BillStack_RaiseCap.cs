using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Self-contained Harmony transpiler that raises RimWorld's per-workbench bill limit, replicating
    /// the well-understood approach of "No Max Bills Redux" but without any hard dependency on it.
    ///
    /// <para>Vanilla <c>BillStack.MaxCount</c> is a compile-time constant (15), so the C# compiler inlines
    /// every read of it as an integer-load (<c>Ldc</c>) at the call site. We therefore scan the IL of the
    /// two methods that gate bill display/adding — <see cref="BillStack.DoListing"/> and the private
    /// <c>ITab_Bills.FillTab</c> — for the instruction that loads the literal 15 and rewrite it to the
    /// player's configured cap (<see cref="WBD_Settings.EffectiveBillCap"/>). A single-hit guard throws if
    /// the literal is not found exactly once, so a future vanilla IL change degrades to "feature off"
    /// (the patch is registered through <c>WBD_Mod.TryPatch</c>, which swallows the throw) rather than
    /// silently mis-targeting.</para>
    ///
    /// <para>Since 2.0 this class is patched in MANUALLY from <see cref="WBD_Mod"/> whenever
    /// <see cref="BillCapCompat.Active"/> is false (no external No Max Bills mod), regardless of the setting:
    /// <see cref="BillCap.Current"/> returns vanilla's 15 while the option is off, so the patch is inert until the
    /// player ticks the box. It carries no <c>[HarmonyPatch]</c> attribute so it is never auto-applied.</para>
    /// </summary>
    public static class Patch_BillStack_RaiseCap
    {
        /// <summary>The two vanilla methods that read <c>BillStack.MaxCount</c> for UI gating.</summary>
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var doListing = typeof(BillStack).GetMethod(nameof(BillStack.DoListing));
            if (doListing != null)
                yield return doListing;

            var fillTab = AccessTools.Method(typeof(ITab_Bills), "FillTab");
            if (fillTab != null)
                yield return fillTab;
        }

        private static readonly MethodInfo CurrentCap = AccessTools.Method(typeof(BillCap), nameof(BillCap.Current));

        /// <summary>
        /// Replaces the single inlined load of the literal 15 with a call to <see cref="BillCap.Current"/>, so the limit
        /// is read live every frame: ticking the box or moving the slider takes effect immediately, no restart
        /// (2.0, M3). The instruction object is mutated in place, so labels and exception blocks stay attached.
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            int replaced = 0;

            foreach (var instruction in instructions)
            {
                if (replaced == 0 && LoadsIntegerValue(instruction, BillStack.MaxCount))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = CurrentCap;
                    replaced++;
                }
                yield return instruction;
            }

            if (replaced != 1)
            {
                // Surfaced to TryPatch, which logs and leaves the mod's other patches intact.
                throw new InvalidOperationException(
                    "BetterWorkbenchDefaults: bill-cap transpiler expected exactly one load of BillStack.MaxCount in " +
                    (original?.DeclaringType?.FullName ?? "?") + "." + (original?.Name ?? "?") +
                    " but replaced " + replaced + ".");
            }
        }

        /// <summary>
        /// True when <paramref name="instruction"/> loads the integer <paramref name="value"/> via any of
        /// the IL integer-load opcodes (Ldc_I4_M1..Ldc_I4_8, Ldc_I4_S, Ldc_I4). Ported from No Max Bills
        /// Redux's CodeInstruction.LoadsIntegerValue helper.
        /// </summary>
        private static bool LoadsIntegerValue(CodeInstruction instruction, int value)
        {
            return LoadsIntegerValue(instruction, out long actual) && actual == value;
        }

        private static bool LoadsIntegerValue(CodeInstruction instruction, out long value)
        {
            int opcode = (int)(uint)(ushort)instruction.opcode.Value;

            if (opcode == 0x001F) // Ldc_I4_S
            {
                value = (sbyte)Convert.ToInt32(instruction.operand);
                return true;
            }
            if (opcode >= 0x0015) // Ldc_I4_M1
            {
                if (opcode <= 0x001E) // Ldc_I4_8
                {
                    value = opcode - 0x0016;
                    return true;
                }
                if (opcode == 0x0020) // Ldc_I4
                {
                    value = Convert.ToInt32(instruction.operand);
                    return true;
                }
                if (opcode == 0x0021) // Ldc_I8
                {
                    value = Convert.ToInt64(instruction.operand);
                    return true;
                }
            }

            value = -1;
            return false;
        }
    }

    /// <summary>The live bill limit read by the patched vanilla UI (see <see cref="Patch_BillStack_RaiseCap"/>).</summary>
    public static class BillCap
    {
        public static int Current()
        {
            var s = WBD_Mod.Settings;
            if (s == null || !s.nativeRaiseBillCap)
                return BillStack.MaxCount;
            return s.maxBillsCap < BillStack.MaxCount ? BillStack.MaxCount : (s.maxBillsCap > 250 ? 250 : s.maxBillsCap);
        }
    }
}
