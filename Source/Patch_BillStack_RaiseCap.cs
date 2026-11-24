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
    /// <para>This class is patched in MANUALLY from <see cref="WBD_Mod"/> ONLY when
    /// <c>Settings.nativeRaiseBillCap</c> is on AND <see cref="BillCapCompat.Active"/> is false (i.e. no
    /// external No Max Bills mod has already raised the cap). It carries no <c>[HarmonyPatch]</c>
    /// attribute so it is never auto-applied by a blanket PatchAll.</para>
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

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            int cap = WBD_Mod.Settings != null ? WBD_Mod.Settings.maxBillsCap : 50;
            int replaced = 0;

            foreach (var instruction in instructions)
            {
                if (replaced == 0 && LoadsIntegerValue(instruction, BillStack.MaxCount))
                {
                    instruction.opcode = OpCodes.Ldc_I4;
                    instruction.operand = cap;
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
}
