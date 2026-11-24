using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace BetterWorkbenchDefaults
{
    /// <summary>
    /// Converts between the stored &lt;config&gt; XML and the live template objects, using the very same
    /// <c>ExposeData</c> code the 1.x files were written with (so values mean exactly what they meant before).
    ///
    /// The one difference to 1.x: before a stored element is handed to Scribe, a CLONE of it is sanitised. Every def
    /// reference Scribe would try to resolve (filter allowedDefs, special filters, override root) that does not
    /// exist in this game is removed from the clone. Result: no red "Could not find def" errors for presets made with
    /// a mod that is switched off now, and the stored original keeps those names untouched (M4).
    /// </summary>
    internal static class SmdScribe
    {
        private static string TempDir => Path.Combine(SmdPaths.LibraryFolder, "tmp");

        public static bool Busy => Scribe.mode != LoadSaveMode.Inactive;

        /// <summary>Write <paramref name="expose"/> into a fresh &lt;config&gt; element. Null when Scribe is busy or on error.</summary>
        public static XElement Write(Action expose)
        {
            if (Busy)
                return null;
            string path = null;
            try
            {
                Directory.CreateDirectory(TempDir);
                path = Path.Combine(TempDir, "w_" + Guid.NewGuid().ToString("N") + ".xml");
                Scribe.saver.InitSaving(path, "config");
                try
                {
                    expose();
                }
                finally
                {
                    Scribe.saver.FinalizeSaving();
                }
                var root = Lib.XmlSafe.LoadFile(path).Root;
                return new XElement(root) { Name = "config" };
            }
            catch (Exception e)
            {
                WBD_Log.Error("Could not serialise a preset: " + e);
                Scribe.ForceStop();
                return null;
            }
            finally
            {
                TryDelete(path);
            }
        }

        /// <summary>
        /// Read a stored &lt;config&gt; element into a live object via <paramref name="expose"/>. Returns false when
        /// Scribe is busy (game saving/loading; try again later) or the element could not be read.
        /// </summary>
        public static bool Read(XElement config, Action expose)
        {
            if (config == null || Busy)
                return false;
            string path = null;
            try
            {
                var clone = new XElement(config) { Name = "smd" };
                Sanitize(clone);
                Directory.CreateDirectory(TempDir);
                path = Path.Combine(TempDir, "r_" + Guid.NewGuid().ToString("N") + ".xml");
                new XDocument(clone).Save(path);
                Scribe.loader.InitLoading(path);
                try
                {
                    expose();
                }
                finally
                {
                    Scribe.loader.FinalizeLoading();
                }
                return true;
            }
            catch (Exception e)
            {
                WBD_Log.Error("Could not read a stored preset: " + e);
                Scribe.ForceStop();
                return false;
            }
            finally
            {
                TryDelete(path);
            }
        }

        /// <summary>
        /// Remove every def reference Scribe would resolve and that this game does not have. Only filter internals are
        /// Scribe def references in our templates; everything else (recipes, drugs, work types, ...) is stored as a
        /// plain string and resolved silently by the templates themselves.
        /// </summary>
        internal static void Sanitize(XElement root)
        {
            foreach (var list in root.DescendantsAndSelf("allowedDefs").ToList())
                foreach (var li in list.Elements("li").ToList())
                    if (DefDatabase<ThingDef>.GetNamedSilentFail(li.Value.Trim()) == null)
                        li.Remove();
            foreach (var list in root.DescendantsAndSelf("disallowedSpecialFilters").ToList())
                foreach (var li in list.Elements("li").ToList())
                    if (DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(li.Value.Trim()) == null)
                        li.Remove();
            foreach (var el in root.Descendants("overrideRootDef").ToList())
                if (DefDatabase<ThingCategoryDef>.GetNamedSilentFail(el.Value.Trim()) == null)
                    el.Remove();
        }

        private static void TryDelete(string path)
        {
            if (path == null)
                return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // a leftover temp file is harmless; the folder is cleaned on the next start
            }
        }

        /// <summary>Remove temp files left by a crash (called once when the library loads).</summary>
        public static void CleanTemp()
        {
            try
            {
                if (Directory.Exists(TempDir))
                    foreach (var f in Directory.GetFiles(TempDir, "*.xml"))
                        TryDelete(f);
            }
            catch
            {
            }
        }
    }
}
